// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Online.API;
using osu.Game.Rulesets.Filter;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;

namespace osu.Game.Rulesets.Kumori.BPM
{
    public enum KumoriStarRatingFilterMode
    {
        Disabled,

        [Description("Star rating pre-mod")]
        PreMod,

        [Description("Star rating post-mod")]
        PostMod,
    }

    public readonly struct KumoriStarRatingRange
    {
        public readonly double? Minimum;
        public readonly double? Maximum;

        public bool HasFilter => Minimum != null || Maximum != null;

        private KumoriStarRatingRange(double? minimum, double? maximum)
        {
            Minimum = minimum;
            Maximum = maximum;
        }

        public bool Contains(double rating)
        {
            double displayedRating = Math.Floor(rating * 100) / 100;
            return (Minimum == null || displayedRating >= Minimum.Value)
                   && (Maximum == null || displayedRating <= Maximum.Value);
        }

        public static KumoriStarRatingRange Parse(string minimum, string maximum) => new KumoriStarRatingRange(parseBound(minimum), parseBound(maximum));

        public static bool TryParseBound(string text, out double value)
        {
            bool parsed = double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            return parsed && double.IsFinite(value) && value >= 0;
        }

        private static double? parseBound(string text) => TryParseBound(text, out double value) ? value : null;
    }

    /// <summary>
    /// Applies Kumori's global star range through osu!'s supported custom-ruleset song-select filter hook.
    /// </summary>
    public sealed class KumoriRulesetFilterCriteria : IRulesetFilterCriteria
    {
        private readonly object cacheLock = new object();
        private CachedEvaluation? cachedEvaluation;
        private int evaluationLogged;

        internal int CachedCriteriaBuildCount { get; private set; }

        public bool Matches(BeatmapInfo beatmapInfo, FilterCriteria criteria)
        {
            CachedEvaluation evaluation = getCachedEvaluation(criteria);

            if (evaluation.Mode == KumoriStarRatingFilterMode.Disabled || !evaluation.Range.HasFilter)
                return true;

            if (evaluation.Mode == KumoriStarRatingFilterMode.PreMod)
                return evaluation.Range.Contains(beatmapInfo.StarRating);

            if (evaluation.ProfileKey == null)
                return true;

            // An index is populated only by an explicit Calculate Maps pass. Until then, preserve
            // the full carousel rather than presenting an apparently empty or partially-filtered library.
            if (!KumoriStarRatingIndex.TryGet(evaluation.ProfileKey, beatmapInfo, out double rating, out bool unavailable))
                return true;

            return !unavailable && evaluation.Range.Contains(rating);
        }

        private CachedEvaluation getCachedEvaluation(FilterCriteria criteria)
        {
            CachedEvaluation? evaluation = Volatile.Read(ref cachedEvaluation);
            long activeStateRevision = KumoriStarFilterState.Revision;

            if (ReferenceEquals(evaluation?.Criteria, criteria) && evaluation.ActiveStateRevision == activeStateRevision)
                return evaluation;

            lock (cacheLock)
            {
                evaluation = cachedEvaluation;
                activeStateRevision = KumoriStarFilterState.Revision;

                if (ReferenceEquals(evaluation?.Criteria, criteria) && evaluation.ActiveStateRevision == activeStateRevision)
                    return evaluation;

                KumoriModBPMAdjust? bpm = criteria.Mods?.OfType<KumoriModBPMAdjust>().SingleOrDefault();
                KumoriStarRatingFilterMode mode = bpm?.StarFilterMode.Value ?? KumoriStarRatingFilterMode.Disabled;
                KumoriStarRatingRange range = bpm == null
                    ? default
                    : KumoriStarRatingRange.Parse(bpm.StarFilterMinimum.Value, bpm.StarFilterMaximum.Value);
                string? criteriaProfileKey = bpm != null && criteria.Ruleset != null
                    ? KumoriStarRatingIndex.CreateProfileKey(criteria.Ruleset, criteria.Mods ?? Array.Empty<Mod>())
                    : null;

                if (KumoriStarFilterState.TryGet(out string activeProfileKey, out KumoriStarRatingFilterMode activeMode,
                                                 out KumoriStarRatingRange activeRange, out activeStateRevision))
                {
                    criteriaProfileKey = activeProfileKey;
                    mode = activeMode;
                    range = activeRange;
                }

                string? profileKey = mode == KumoriStarRatingFilterMode.PostMod && range.HasFilter ? criteriaProfileKey : null;

                evaluation = new CachedEvaluation(criteria, mode, range, profileKey, activeStateRevision);
                Volatile.Write(ref cachedEvaluation, evaluation);
                CachedCriteriaBuildCount++;

                if (Interlocked.Exchange(ref evaluationLogged, 1) == 0)
                {
                    Logger.Log($"Kumori star filter evaluating: mode={mode}, min={range.Minimum?.ToString(CultureInfo.InvariantCulture) ?? "none"}, "
                               + $"max={range.Maximum?.ToString(CultureInfo.InvariantCulture) ?? "none"}, bpmMod={bpm != null}, "
                               + $"activeState={criteriaProfileKey != null && KumoriStarFilterState.TryGet(out _, out _, out _)}, "
                               + $"profile={(profileKey == null ? "none" : profileKey[..8])}, loaded={profileKey != null && KumoriStarRatingIndex.IsLoaded(profileKey)}");
                }

                return evaluation;
            }
        }

        private sealed record CachedEvaluation(FilterCriteria Criteria, KumoriStarRatingFilterMode Mode, KumoriStarRatingRange Range, string? ProfileKey,
                                               long ActiveStateRevision);

        public bool TryParseCustomKeywordCriteria(string key, Operator op, string value) => false;

        public bool FilterMayChangeFromMods(FilterCriteria criteria, ValueChangedEvent<IReadOnlyList<Mod>> mods) =>
            containsEnabledFilter(mods.OldValue) || containsEnabledFilter(mods.NewValue);

        private static bool containsEnabledFilter(IEnumerable<Mod> mods) =>
            mods.OfType<KumoriModBPMAdjust>().Any(bpm => bpm.StarFilterMode.Value != KumoriStarRatingFilterMode.Disabled);
    }

    /// <summary>
    /// Bridges the active customisation control to song select. osu! may replace selected mod instances
    /// with copies, so filter-only settings cannot rely on the copy in <see cref="FilterCriteria.Mods"/>
    /// having received the latest text-box and dropdown values.
    /// </summary>
    internal static class KumoriStarFilterState
    {
        private static long nextRevision;
        private static ActiveState current = new ActiveState(0, null, KumoriStarRatingFilterMode.Disabled, default);

        public static long Revision => Volatile.Read(ref current).Revision;

        public static void Publish(IRulesetInfo rulesetInfo, IEnumerable<Mod> mods, KumoriStarRatingFilterMode mode, string minimum, string maximum)
        {
            Mod[] selectedMods = mods.ToArray();

            if (!selectedMods.OfType<KumoriModBPMAdjust>().Any())
            {
                Clear();
                return;
            }

            string profileKey = KumoriStarRatingIndex.CreateProfileKey(rulesetInfo, selectedMods);
            long revision = Interlocked.Increment(ref nextRevision);
            Volatile.Write(ref current, new ActiveState(revision, profileKey, mode, KumoriStarRatingRange.Parse(minimum, maximum)));
        }

        public static bool TryGet(out string profileKey, out KumoriStarRatingFilterMode mode, out KumoriStarRatingRange range)
            => TryGet(out profileKey, out mode, out range, out _);

        public static bool TryGet(out string profileKey, out KumoriStarRatingFilterMode mode, out KumoriStarRatingRange range, out long revision)
        {
            ActiveState state = Volatile.Read(ref current);
            revision = state.Revision;

            if (state.ProfileKey == null)
            {
                profileKey = string.Empty;
                mode = KumoriStarRatingFilterMode.Disabled;
                range = default;
                return false;
            }

            profileKey = state.ProfileKey;
            mode = state.Mode;
            range = state.Range;
            return true;
        }

        internal static void Clear()
        {
            long revision = Interlocked.Increment(ref nextRevision);
            Volatile.Write(ref current, new ActiveState(revision, null, KumoriStarRatingFilterMode.Disabled, default));
        }

        private sealed record ActiveState(long Revision, string? ProfileKey, KumoriStarRatingFilterMode Mode, KumoriStarRatingRange Range);
    }

    /// <summary>
    /// Persistent, profile-keyed exact star ratings used by <see cref="KumoriRulesetFilterCriteria"/>.
    /// </summary>
    public static class KumoriStarRatingIndex
    {
        private const int current_version = 2;
        private const string legacy_directory = "kumori-star-ratings";
        private const string obsolete_bulk_calculator_fingerprint = "2026.730.0.0";

        public const string DatabaseFilename = KumoriStarRatingDatabase.Filename;

        private static readonly ConcurrentDictionary<string, Profile> profiles = new ConcurrentDictionary<string, Profile>();

        public static string CreateProfileKey(IRulesetInfo rulesetInfo, IEnumerable<Mod> mods)
        {
            Mod[] selectedMods = mods.ToArray();
            string serialisedMods = serialiseDifficultyAffectingSettings(selectedMods, normaliseDifficultyAdjust: true);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{getCalculatorFingerprint()}|{getCalculationVersion(selectedMods)}|{rulesetInfo.ShortName}|{serialisedMods}"));
            return Convert.ToHexString(hash);
        }

        internal static string CreateObsoleteProfileKey(IRulesetInfo rulesetInfo, IEnumerable<Mod> mods)
        {
            Mod[] selectedMods = mods.ToArray();
            string serialisedMods = serialiseDifficultyAffectingSettings(selectedMods, normaliseDifficultyAdjust: true, includeHidden: true);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{obsolete_bulk_calculator_fingerprint}|{getCalculationVersion(selectedMods)}|{rulesetInfo.ShortName}|{serialisedMods}"));
            return Convert.ToHexString(hash);
        }

        internal static string CreateLegacyProfileKey(IRulesetInfo rulesetInfo, IEnumerable<Mod> mods)
        {
            Mod[] selectedMods = mods.ToArray();
            string serialisedMods = serialiseDifficultyAffectingSettings(selectedMods, normaliseDifficultyAdjust: false);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{getCalculationVersion(selectedMods)}|{rulesetInfo.ShortName}|{serialisedMods}"));
            return Convert.ToHexString(hash);
        }

        private static string createPreviousStableProfileKey(IRulesetInfo rulesetInfo, IEnumerable<Mod> mods)
        {
            Mod[] selectedMods = mods.ToArray();
            string serialisedMods = serialiseDifficultyAffectingSettings(selectedMods, normaliseDifficultyAdjust: true);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{getCalculationVersion(selectedMods)}|{rulesetInfo.ShortName}|{serialisedMods}"));
            return Convert.ToHexString(hash);
        }

        private static string serialiseDifficultyAffectingSettings(IEnumerable<Mod> mods, bool normaliseDifficultyAdjust, bool includeHidden = false) =>
            string.Join("|", mods.Where(mod => includeHidden || mod is not OsuModHidden).OrderBy(mod => mod.Acronym).Select(mod =>
            {
                if (mod is KumoriModBPMAdjust bpm)
                {
                    // Only BPM settings which affect difficulty belong in the star profile.
                    return JsonConvert.SerializeObject(new
                    {
                        acronym = mod.Acronym,
                        targetBpm = bpm.TargetBPM.Value,
                        scaleMapStatsWithBpm = bpm.ScaleMapStatsWithBPM.Value,
                    });
                }

                if (normaliseDifficultyAdjust && mod is OsuModDifficultyAdjust difficultyAdjust)
                {
                    // osu!standard star difficulty is affected by object radius (CS) and object
                    // pre-empt (AR). HP, OD and the Extended Limits UI toggle do not participate
                    // in the raw star calculation, so changing those must not invalidate a 97k-map
                    // exact-rating profile.
                    return JsonConvert.SerializeObject(new
                    {
                        acronym = mod.Acronym,
                        circleSize = difficultyAdjust.CircleSize.Value,
                        approachRate = difficultyAdjust.ApproachRate.Value,
                    });
                }

                return JsonConvert.SerializeObject(new APIMod(mod));
            }));

        private static string getCalculationVersion(IEnumerable<Mod> mods)
        {
            // Early Kumori builds applied BPM compensation alongside regular difficulty mods, so
            // caches involving DA/EZ/HR used the wrong ordering. Preserve the large, still-valid
            // BPM-only/visibility-mod caches while giving order-sensitive profiles a fresh key.
            return mods.SelectMany(mod => mod is MultiMod multi ? multi.Mods : [mod])
                       .Any(mod => mod is IApplicableToDifficulty)
                ? "v2"
                : "v1";
        }

        private static string getCalculatorFingerprint() =>
            KumoriDifficultyCalculator.OfficialDifficultyVersion.ToString(CultureInfo.InvariantCulture);

        public static string CreateBeatmapKey(BeatmapInfo beatmap) => CreateBeatmapKey(beatmap.ID, beatmap.Hash);

        internal static string CreateBeatmapKey(Guid id, string hash) => string.IsNullOrWhiteSpace(hash) ? $"local:{id:N}" : hash;

        public static bool TryGet(string profileKey, BeatmapInfo beatmap, out double rating, out bool unavailable)
        {
            rating = 0;
            unavailable = false;

            if (!profiles.TryGetValue(profileKey, out Profile? profile))
                return false;

            string beatmapKey = CreateBeatmapKey(beatmap);

            if (profile.RuntimeRatings.TryGetValue(beatmapKey, out double runtimeRating))
            {
                rating = runtimeRating;
                return true;
            }

            if (profile.RuntimeUnavailableBeatmaps.ContainsKey(beatmapKey))
            {
                unavailable = true;
                return true;
            }

            return false;
        }

        public static bool IsLoaded(string profileKey) => profiles.ContainsKey(profileKey);

        internal static KumoriStarRatingProfileSnapshot GetSnapshot(string profileKey)
        {
            if (!profiles.TryGetValue(profileKey, out Profile? profile))
                return new KumoriStarRatingProfileSnapshot(new Dictionary<string, double>(), new HashSet<string>(), false);

            lock (profile.Sync)
            {
                return new KumoriStarRatingProfileSnapshot(
                    new Dictionary<string, double>(profile.Ratings, StringComparer.Ordinal),
                    new HashSet<string>(profile.UnavailableBeatmaps, StringComparer.Ordinal),
                    profile.Complete);
            }
        }

        public static void Store(Storage storage, string profileKey, IReadOnlyDictionary<string, double> ratings, IReadOnlyCollection<string> unavailableBeatmaps,
                                 bool complete = true)
        {
            var normalisedRatings = new Dictionary<string, double>(StringComparer.Ordinal);

            foreach ((string key, double rating) in ratings)
                normalisedRatings[normaliseBeatmapKey(key)] = rating;

            var normalisedUnavailable = new HashSet<string>(unavailableBeatmaps.Select(normaliseBeatmapKey), StringComparer.Ordinal);
            normalisedUnavailable.ExceptWith(normalisedRatings.Keys);

            var profile = new Profile
            {
                Version = current_version,
                Ratings = normalisedRatings,
                UnavailableBeatmaps = normalisedUnavailable,
                Complete = complete,
            };
            profile.BuildRuntimeIndexes();

            profiles[profileKey] = profile;

            try
            {
                KumoriStarRatingDatabase.EnsureProfiles(storage, [profileKey]);
                var writes = new List<KumoriStarRatingDatabaseWrite>(profile.Ratings.Count + profile.UnavailableBeatmaps.Count);
                writes.AddRange(profile.Ratings.Select(pair => new KumoriStarRatingDatabaseWrite(profileKey, pair.Key, pair.Value, false)));
                writes.AddRange(profile.UnavailableBeatmaps.Select(key => new KumoriStarRatingDatabaseWrite(profileKey, key, null, true)));
                KumoriStarRatingDatabase.WriteBatch(storage, writes);
                KumoriStarRatingDatabase.SetProfilesComplete(storage, [profileKey], complete);
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to save Kumori star-rating index");
            }
        }

        public static bool Load(Storage storage, string profileKey)
        {
            if (profiles.ContainsKey(profileKey))
                return true;

            try
            {
                if (KumoriStarRatingDatabase.TryLoadProfile(storage, profileKey, out KumoriPersistedStarRatingProfile persisted))
                {
                    var databaseProfile = new Profile
                    {
                        Version = current_version,
                        Ratings = persisted.Ratings,
                        UnavailableBeatmaps = persisted.UnavailableBeatmaps,
                        Complete = persisted.Complete,
                    };
                    databaseProfile.BuildRuntimeIndexes();
                    profiles[profileKey] = databaseProfile;
                    return true;
                }

                return migrateLegacyJson(storage, profileKey);
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to load Kumori star-rating index");
                return false;
            }
        }

        public static bool Load(Storage storage, IRulesetInfo rulesetInfo, IEnumerable<Mod> mods)
        {
            Mod[] selectedMods = mods.ToArray();
            string profileKey = CreateProfileKey(rulesetInfo, selectedMods);

            if (Load(storage, profileKey))
                return true;

            // Adopt profiles written before calculator build fingerprinting and DA profile
            // normalisation, then persist under the current stable key once.
            string[] legacyProfileKeys =
            [
                createPreviousStableProfileKey(rulesetInfo, selectedMods),
                CreateLegacyProfileKey(rulesetInfo, selectedMods),
            ];

            foreach (string legacyProfileKey in legacyProfileKeys.Distinct(StringComparer.Ordinal))
            {
                if (legacyProfileKey == profileKey || !Load(storage, legacyProfileKey))
                    continue;

                KumoriStarRatingProfileSnapshot legacy = GetSnapshot(legacyProfileKey);
                Store(storage, profileKey, legacy.Ratings, legacy.UnavailableBeatmaps, legacy.Complete);
                Logger.Log($"Migrated Kumori star-rating profile {legacyProfileKey} to stable profile {profileKey}");
                return true;
            }

            return false;
        }

        public static void ClearMemory() => profiles.Clear();

        internal static void EnsureProfiles(Storage storage, IEnumerable<string> profileKeys) => KumoriStarRatingDatabase.EnsureProfiles(storage, profileKeys);

        internal static HashSet<string> GetFullyIndexedBeatmapKeys(Storage storage, IReadOnlyCollection<string> profileKeys) =>
            KumoriStarRatingDatabase.GetFullyIndexedBeatmapKeys(storage, profileKeys);

        internal static bool DeleteProfiles(Storage storage, IReadOnlyCollection<string> profileKeys, bool compact)
        {
            bool deleted = KumoriStarRatingDatabase.DeleteProfiles(storage, profileKeys, compact);

            foreach (string profileKey in profileKeys)
                profiles.TryRemove(profileKey, out _);

            return deleted;
        }

        internal static void StoreBatch(Storage storage, IReadOnlyCollection<KumoriStarRatingDatabaseWrite> writes, bool updateMemory, bool profilesAlreadyEnsured = false)
        {
            if (writes.Count == 0)
                return;

            if (!profilesAlreadyEnsured)
            {
                string[] profileKeys = writes.Select(write => write.ProfileKey).Distinct(StringComparer.Ordinal).ToArray();
                KumoriStarRatingDatabase.EnsureProfiles(storage, profileKeys);
            }
            KumoriStarRatingDatabase.WriteBatch(storage, writes);

            if (!updateMemory)
                return;

            foreach (IGrouping<string, KumoriStarRatingDatabaseWrite> group in writes.GroupBy(write => write.ProfileKey, StringComparer.Ordinal))
            {
                Profile profile = profiles.GetOrAdd(group.Key, _ => new Profile { Version = current_version });
                profile.ApplyWrites(group);
            }
        }

        internal static void SetProfilesComplete(Storage storage, IReadOnlyCollection<string> profileKeys, bool complete, bool updateMemory)
        {
            KumoriStarRatingDatabase.EnsureProfiles(storage, profileKeys);
            KumoriStarRatingDatabase.SetProfilesComplete(storage, profileKeys, complete);

            if (!updateMemory)
                return;

            foreach (string profileKey in profileKeys)
            {
                if (!profiles.TryGetValue(profileKey, out Profile? profile))
                    continue;

                lock (profile.Sync)
                    profile.Complete = complete;
            }
        }

        private static bool migrateLegacyJson(Storage storage, string profileKey)
        {
            string filename = getLegacyFilename(profileKey);

            if (!storage.Exists(filename))
                return false;

            try
            {
                using Stream? stream = storage.GetStream(filename);

                if (stream == null)
                    return false;

                using var reader = new StreamReader(stream);
                Profile? profile = JsonConvert.DeserializeObject<Profile>(reader.ReadToEnd());

                if (profile?.Version != current_version)
                    return false;

                profile.NormalisePortableKeys();
                Store(storage, profileKey, profile.Ratings, profile.UnavailableBeatmaps, profile.Complete);
                Logger.Log($"Migrated Kumori star-rating profile {profileKey} from JSON to SQLite");
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to migrate Kumori star-rating JSON index");
                return false;
            }
        }

        private static string getLegacyFilename(string profileKey) => $"{legacy_directory}/{profileKey}.json";

        private static string normaliseBeatmapKey(string key)
        {
            int separator = key.IndexOf(':');

            if (separator == 32 && Guid.TryParseExact(key.AsSpan(0, separator), "N", out _))
                return key[(separator + 1)..];

            return key;
        }

        private sealed class Profile
        {
            [JsonProperty("version")]
            public int Version { get; set; }

            [JsonProperty("ratings")]
            public Dictionary<string, double> Ratings { get; set; } = new Dictionary<string, double>();

            [JsonProperty("unavailable_beatmaps")]
            public HashSet<string> UnavailableBeatmaps { get; set; } = new HashSet<string>();

            [JsonProperty("complete")]
            public bool Complete { get; set; }

            [JsonIgnore]
            public object Sync { get; } = new object();

            [JsonIgnore]
            public ConcurrentDictionary<string, double> RuntimeRatings { get; private set; } = new ConcurrentDictionary<string, double>(StringComparer.Ordinal);

            [JsonIgnore]
            public ConcurrentDictionary<string, byte> RuntimeUnavailableBeatmaps { get; private set; } = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

            public void BuildRuntimeIndexes()
            {
                NormalisePortableKeys();
                RuntimeRatings = new ConcurrentDictionary<string, double>(Ratings, StringComparer.Ordinal);
                RuntimeUnavailableBeatmaps = new ConcurrentDictionary<string, byte>(
                    UnavailableBeatmaps.Select(key => new KeyValuePair<string, byte>(key, 0)), StringComparer.Ordinal);
            }

            public void ApplyWrites(IEnumerable<KumoriStarRatingDatabaseWrite> writes)
            {
                lock (Sync)
                {
                    foreach (KumoriStarRatingDatabaseWrite write in writes)
                    {
                        string key = normaliseBeatmapKey(write.BeatmapKey);

                        if (write.Unavailable || !write.Rating.HasValue)
                        {
                            Ratings.Remove(key);
                            UnavailableBeatmaps.Add(key);
                            RuntimeRatings.TryRemove(key, out _);
                            RuntimeUnavailableBeatmaps[key] = 0;
                        }
                        else
                        {
                            Ratings[key] = write.Rating.Value;
                            UnavailableBeatmaps.Remove(key);
                            RuntimeRatings[key] = write.Rating.Value;
                            RuntimeUnavailableBeatmaps.TryRemove(key, out _);
                        }
                    }
                }
            }

            public void NormalisePortableKeys()
            {
                Ratings = Ratings.ToDictionary(pair => normaliseBeatmapKey(pair.Key), pair => pair.Value, StringComparer.Ordinal);
                UnavailableBeatmaps = UnavailableBeatmaps.Select(normaliseBeatmapKey).ToHashSet(StringComparer.Ordinal);
                UnavailableBeatmaps.ExceptWith(Ratings.Keys);
            }
        }
    }

    internal readonly record struct KumoriStarRatingProfileSnapshot(Dictionary<string, double> Ratings, HashSet<string> UnavailableBeatmaps, bool Complete);
}

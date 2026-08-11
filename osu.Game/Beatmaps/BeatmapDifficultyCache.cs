// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using osu.Framework.Allocation;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics.Textures;
using osu.Framework.Lists;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Threading;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osu.Game.Skinning;
using osu.Game.Storyboards;

namespace osu.Game.Beatmaps
{
    /// <summary>
    /// A component which performs and acts as a central cache for difficulty calculations of beatmap/ruleset/mod combinations.
    /// Currently not persisted between game sessions.
    /// </summary>
    public partial class BeatmapDifficultyCache : MemoryCachingComponent<BeatmapDifficultyCache.DifficultyCacheLookup, StarDifficulty?>
    {
        private const int persisted_filter_cache_version = 2;

        // Too many simultaneous updates can lead to stutters. One thread seems to work fine for song select display purposes.
        private readonly ThreadedTaskScheduler updateScheduler = new ThreadedTaskScheduler(1, nameof(BeatmapDifficultyCache));

        // Keep library filtering away from the display-update queue, while limiting it to
        // one background worker so difficulty calculation does not make song select stutter.
        private readonly ThreadedTaskScheduler filterScheduler = new ThreadedTaskScheduler(1, $"{nameof(BeatmapDifficultyCache)} Filter");
        private readonly Dictionary<DifficultyCacheLookup, double> filterStarRatingCache = new Dictionary<DifficultyCacheLookup, double>();

        private const string persisted_filter_cache_directory = "cache/bpm-star-ratings";

        /// <summary>
        /// All bindables that should be updated along with the current ruleset + mods.
        /// </summary>
        private readonly WeakList<BindableStarDifficulty> trackedBindables = new WeakList<BindableStarDifficulty>();

        /// <summary>
        /// Cancellation sources used by tracked bindables.
        /// </summary>
        private readonly List<CancellationTokenSource> linkedCancellationSources = new List<CancellationTokenSource>();

        /// <summary>
        /// Lock to be held when operating on <see cref="trackedBindables"/> or <see cref="linkedCancellationSources"/>.
        /// </summary>
        private readonly object bindableUpdateLock = new object();

        private CancellationTokenSource trackedUpdateCancellationSource = new CancellationTokenSource();

        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        [Resolved]
        private Storage storage { get; set; } = null!;

        [Resolved]
        private Bindable<RulesetInfo> currentRuleset { get; set; } = null!;

        [Resolved]
        private Bindable<IReadOnlyList<Mod>> currentMods { get; set; } = null!;

        private ModSettingChangeTracker? modSettingChangeTracker;
        private ScheduledDelegate? debouncedModSettingsChange;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            currentRuleset.BindValueChanged(_ => Scheduler.AddOnce(updateTrackedBindables));

            currentMods.BindValueChanged(mods =>
            {
                // A change in bindable here doesn't guarantee that mods have actually changed.
                // However, we *do* want to make sure that the mod *references* are the same;
                // `SequenceEqual()` without a comparer would fall back to `IEquatable`.
                // Failing to ensure reference equality can cause setting change tracking to fail later.
                if (mods.OldValue.SequenceEqual(mods.NewValue, ReferenceEqualityComparer.Instance))
                    return;

                modSettingChangeTracker?.Dispose();

                Scheduler.AddOnce(updateTrackedBindables);

                modSettingChangeTracker = new ModSettingChangeTracker(mods.NewValue);
                modSettingChangeTracker.SettingChanged += _ =>
                {
                    lock (bindableUpdateLock)
                    {
                        debouncedModSettingsChange?.Cancel();
                        debouncedModSettingsChange = Scheduler.AddDelayed(updateTrackedBindables, 100);
                    }
                };
            }, true);
        }

        /// <summary>
        /// Notify this cache that a beatmap has been invalidated/updated.
        /// </summary>
        /// <param name="oldBeatmap">The old beatmap model.</param>
        /// <param name="newBeatmap">The updated beatmap model.</param>
        public void Invalidate(IBeatmapInfo oldBeatmap, IBeatmapInfo newBeatmap)
        {
            base.Invalidate(lookup => lookup.BeatmapInfo.Equals(oldBeatmap));

            lock (filterStarRatingCache)
            {
                foreach (var lookup in filterStarRatingCache.Keys.Where(lookup => lookup.BeatmapInfo.Equals(oldBeatmap)).ToArray())
                    filterStarRatingCache.Remove(lookup);
            }

            lock (bindableUpdateLock)
            {
                bool trackedBindablesRefreshRequired = false;

                foreach (var bsd in trackedBindables.Where(bsd => bsd.BeatmapInfo.Equals(oldBeatmap)))
                {
                    bsd.BeatmapInfo = newBeatmap;
                    trackedBindablesRefreshRequired = true;
                }

                if (trackedBindablesRefreshRequired)
                    Scheduler.AddOnce(updateTrackedBindables);
            }
        }

        /// <summary>
        /// Retrieves a bindable containing the star difficulty of a <see cref="BeatmapInfo"/> that follows the currently-selected ruleset and mods.
        /// </summary>
        /// <param name="beatmapInfo">The <see cref="BeatmapInfo"/> to get the difficulty of.</param>
        /// <param name="cancellationToken">An optional <see cref="CancellationToken"/> which stops updating the star difficulty for the given <see cref="BeatmapInfo"/>.</param>
        /// <param name="computationDelay">A delay in milliseconds before performing the </param>
        /// <returns>A bindable that is updated to contain the star difficulty when it becomes available. May be an approximation while in an initial calculating state.</returns>
        public IBindable<StarDifficulty> GetBindableDifficulty(IBeatmapInfo beatmapInfo, CancellationToken cancellationToken = default, int computationDelay = 0)
        {
            var bindable = new BindableStarDifficulty(beatmapInfo, cancellationToken)
            {
                // Start with an approximate known value instead of zero.
                Value = new StarDifficulty(beatmapInfo.StarRating, 0)
            };

            lock (bindableUpdateLock)
            {
                var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(trackedUpdateCancellationSource.Token, cancellationToken);
                linkedCancellationSources.Add(linkedSource);

                updateBindable(bindable, currentRuleset.Value, currentMods.Value, linkedSource, computationDelay);

                trackedBindables.Add(bindable);
            }

            return bindable;
        }

        /// <summary>
        /// Retrieves the difficulty of a <see cref="IBeatmapInfo"/>.
        /// </summary>
        /// <param name="beatmapInfo">The <see cref="IBeatmapInfo"/> to get the difficulty of.</param>
        /// <param name="rulesetInfo">The <see cref="IRulesetInfo"/> to get the difficulty with.</param>
        /// <param name="mods">The <see cref="Mod"/>s to get the difficulty with.</param>
        /// <param name="cancellationToken">An optional <see cref="CancellationToken"/> which stops computing the star difficulty.</param>
        /// <param name="computationDelay">In the case a cached lookup was not possible, a value in milliseconds of to wait until performing potentially intensive lookup.</param>
        /// <returns>
        /// The requested <see cref="StarDifficulty"/>, if non-<see langword="null"/>.
        /// A <see langword="null"/> return value indicates that the difficulty process failed or was interrupted early,
        /// and as such there is no usable star difficulty value to be returned.
        /// </returns>
        public virtual Task<StarDifficulty?> GetDifficultyAsync(IBeatmapInfo beatmapInfo, IRulesetInfo? rulesetInfo = null, IEnumerable<Mod>? mods = null,
                                                                CancellationToken cancellationToken = default, int computationDelay = 0)
        {
            // In the case that the user hasn't given us a ruleset, use the beatmap's default ruleset.
            rulesetInfo ??= beatmapInfo.Ruleset;

            var localBeatmapInfo = beatmapInfo as BeatmapInfo;
            var localRulesetInfo = rulesetInfo as RulesetInfo;

            // Difficulty can only be computed if the beatmap and ruleset are locally available.
            if (localBeatmapInfo == null || localRulesetInfo == null)
            {
                // If not, fall back to the existing star difficulty (e.g. from an online source).
                return Task.FromResult<StarDifficulty?>(new StarDifficulty(beatmapInfo.StarRating, (beatmapInfo as IBeatmapOnlineInfo)?.MaxCombo ?? 0));
            }

            return GetAsync(new DifficultyCacheLookup(localBeatmapInfo, localRulesetInfo, mods), cancellationToken, computationDelay);
        }

        /// <summary>
        /// Retrieves an already-computed difficulty without scheduling a new calculation.
        /// </summary>
        public bool TryGetDifficulty(IBeatmapInfo beatmapInfo, IRulesetInfo? rulesetInfo, IEnumerable<Mod>? mods, out StarDifficulty difficulty)
        {
            rulesetInfo ??= beatmapInfo.Ruleset;

            if (beatmapInfo is BeatmapInfo localBeatmapInfo
                && rulesetInfo is RulesetInfo localRulesetInfo
                && CheckExists(new DifficultyCacheLookup(localBeatmapInfo, localRulesetInfo, mods), out StarDifficulty? cached)
                && cached.HasValue)
            {
                difficulty = cached.Value;
                return true;
            }

            difficulty = default;
            return false;
        }

        /// <summary>
        /// Calculates exact star ratings for a complete manual song-select filter pass.
        /// Persisted results for the same ruleset and mod settings are reused between sessions.
        /// </summary>
        public Task<IReadOnlyDictionary<Guid, double>> CalculateStarRatingsForFilterAsync(IReadOnlyList<BeatmapInfo> beatmaps, IRulesetInfo rulesetInfo, IEnumerable<Mod>? mods,
                                                                                           Action<int, int>? reportProgress = null, CancellationToken cancellationToken = default,
                                                                                           bool retryUnavailableBeatmaps = false)
        {
            Mod[] orderedMods = mods?.OrderBy(mod => mod.Acronym).Select(mod => mod.DeepClone()).ToArray() ?? Array.Empty<Mod>();

            return Task.Factory.StartNew<IReadOnlyDictionary<Guid, double>>(() =>
            {
                Thread.CurrentThread.Priority = ThreadPriority.Highest;

                string profileKey = createFilterProfileKey(rulesetInfo, orderedMods);
                PersistedFilterStarRatings persisted = loadPersistedFilterStarRatingsForProfile(profileKey, rulesetInfo, orderedMods, false) ?? new PersistedFilterStarRatings();
                var results = new Dictionary<Guid, double>(beatmaps.Count);
                var pending = new List<(BeatmapInfo Beatmap, string PersistedKey)>();
                int completed = 0;

                foreach (BeatmapInfo beatmap in beatmaps)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string beatmapKey = createPersistedBeatmapKey(beatmap);

                    if (persisted.Ratings.TryGetValue(beatmapKey, out double persistedRating))
                    {
                        results[beatmap.ID] = persistedRating;
                        reportProgress?.Invoke(++completed, beatmaps.Count);
                        continue;
                    }

                    if (!retryUnavailableBeatmaps && persisted.UnavailableBeatmaps.Contains(beatmapKey))
                    {
                        reportProgress?.Invoke(++completed, beatmaps.Count);
                        continue;
                    }

                    pending.Add((beatmap, beatmapKey));
                }

                // Difficulty calculation is CPU-heavy and each map is independent. Use every
                // available core except one (kept free for UI/progress/cancellation handling).
                // Worker priority is restored afterwards because Parallel uses shared pool threads.
                var resultLock = new object();
                var parallelOptions = new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
                    TaskScheduler = TaskScheduler.Default,
                };

                Parallel.ForEach(pending, parallelOptions, item =>
                {
                    Thread currentThread = Thread.CurrentThread;
                    ThreadPriority originalPriority = currentThread.Priority;

                    try
                    {
                        currentThread.Priority = ThreadPriority.Highest;
                        cancellationToken.ThrowIfCancellationRequested();

                        var lookup = new DifficultyCacheLookup(item.Beatmap, rulesetInfo as RulesetInfo, orderedMods);
                        double? rating = null;

                        if (CheckExists(lookup, out StarDifficulty? exactDifficulty) && exactDifficulty.HasValue)
                            rating = exactDifficulty.Value.Stars;
                        else
                        {
                            lock (filterStarRatingCache)
                            {
                                if (filterStarRatingCache.TryGetValue(lookup, out double cached))
                                    rating = cached;
                            }
                        }

                        rating ??= computeStarRating(lookup, cancellationToken);

                        if (rating.HasValue)
                        {
                            lock (resultLock)
                            {
                                results[item.Beatmap.ID] = rating.Value;
                                persisted.Ratings[item.PersistedKey] = rating.Value;
                                persisted.UnavailableBeatmaps.Remove(item.PersistedKey);
                            }

                            lock (filterStarRatingCache)
                                filterStarRatingCache[lookup] = rating.Value;
                        }
                        else
                        {
                            lock (resultLock)
                                persisted.UnavailableBeatmaps.Add(item.PersistedKey);
                        }
                    }
                    finally
                    {
                        currentThread.Priority = originalPriority;
                    }

                    reportProgress?.Invoke(Interlocked.Increment(ref completed), beatmaps.Count);
                });

                cancellationToken.ThrowIfCancellationRequested();
                persisted.Version = persisted_filter_cache_version;
                savePersistedFilterStarRatings(profileKey, persisted);
                return results;
            }, cancellationToken, TaskCreationOptions.HideScheduler | TaskCreationOptions.RunContinuationsAsynchronously, filterScheduler);
        }

        /// <summary>
        /// Loads a complete persisted filter profile without recalculating beatmaps.
        /// Returns <see langword="null"/> if no profile exists or the current library contains unprocessed maps.
        /// </summary>
        public Task<FilterStarRatingProfile?> LoadStarRatingsForFilterAsync(IReadOnlyList<BeatmapInfo> beatmaps, IRulesetInfo rulesetInfo, IEnumerable<Mod>? mods,
                                                                            CancellationToken cancellationToken = default)
        {
            Mod[] orderedMods = mods?.OrderBy(mod => mod.Acronym).Select(mod => mod.DeepClone()).ToArray() ?? Array.Empty<Mod>();

            return Task.Factory.StartNew(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                string profileKey = createFilterProfileKey(rulesetInfo, orderedMods);
                PersistedFilterStarRatings? persisted = loadPersistedFilterStarRatingsForProfile(profileKey, rulesetInfo, orderedMods, true);

                if (persisted == null)
                    return null;

                bool migrated = persisted.Version < persisted_filter_cache_version;

                if (migrated)
                {
                    // Version 1 profiles were only written after a complete pass, but did not
                    // record maps for which difficulty calculation returned no rating. Treat all
                    // missing entries in that completed snapshot as unavailable during migration.
                    foreach (BeatmapInfo beatmap in beatmaps)
                    {
                        string beatmapKey = createPersistedBeatmapKey(beatmap);

                        if (!persisted.Ratings.ContainsKey(beatmapKey))
                            persisted.UnavailableBeatmaps.Add(beatmapKey);
                    }

                    persisted.Version = persisted_filter_cache_version;
                }

                if (migrated || !storage.Exists($"{persisted_filter_cache_directory}/{profileKey}.json"))
                    savePersistedFilterStarRatings(profileKey, persisted);

                var results = new Dictionary<Guid, double>(beatmaps.Count);

                foreach (BeatmapInfo beatmap in beatmaps)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string beatmapKey = createPersistedBeatmapKey(beatmap);

                    if (persisted.Ratings.TryGetValue(beatmapKey, out double rating))
                        results[beatmap.ID] = rating;
                    else if (!persisted.UnavailableBeatmaps.Contains(beatmapKey))
                        return null;
                }

                return new FilterStarRatingProfile(results, beatmaps.Count);
            }, cancellationToken, TaskCreationOptions.HideScheduler | TaskCreationOptions.RunContinuationsAsynchronously, filterScheduler);
        }

        private string createFilterProfileKey(IRulesetInfo rulesetInfo, IReadOnlyList<Mod> mods)
        {
            string serialisedMods = string.Join("|", mods.Select(mod =>
            {
                if (mod is ModBPMAdjust bpmAdjust)
                {
                    // Audio treatment, beat accents, custom pitch, target lifecycle, and the
                    // song-select filter stored with personal presets do not affect difficulty.
                    // Excluding them keeps one star profile stable across harmless UI changes.
                    return JsonConvert.SerializeObject(new
                    {
                        acronym = mod.Acronym,
                        targetBpm = bpmAdjust.TargetBPM.Value,
                        scaleMapStatsWithBpm = bpmAdjust.ScaleMapStatsWithBPM.Value,
                    });
                }

                return JsonConvert.SerializeObject(new APIMod(mod));
            }));

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"v2|{rulesetInfo.ShortName}|{serialisedMods}"));
            return Convert.ToHexString(hash);
        }

        private static string createLegacyFilterProfileKey(IRulesetInfo rulesetInfo, IReadOnlyList<Mod> mods)
        {
            string serialisedMods = JsonConvert.SerializeObject(mods.Select(mod => new APIMod(mod)));
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{rulesetInfo.ShortName}|{serialisedMods}"));
            return Convert.ToHexString(hash);
        }

        private PersistedFilterStarRatings? loadPersistedFilterStarRatingsForProfile(string profileKey, IRulesetInfo rulesetInfo, IReadOnlyList<Mod> mods,
                                                                                     bool allowSoleLegacyFallback)
        {
            PersistedFilterStarRatings? persisted = loadPersistedFilterStarRatings(profileKey);

            if (persisted != null)
                return persisted;

            string legacyProfileKey = createLegacyFilterProfileKey(rulesetInfo, mods);

            if (legacyProfileKey != profileKey && (persisted = loadPersistedFilterStarRatings(legacyProfileKey)) != null)
            {
                Logger.Log($"Migrating legacy BPM star-rating filter profile {legacyProfileKey} to {profileKey}.");
                return persisted;
            }

            if (!allowSoleLegacyFallback)
                return null;

            // The first cache format contained no profile metadata, so a key created with a
            // harmlessly different audio/UI setting cannot be identified from its contents.
            // If there is exactly one legacy profile, it is unambiguous enough to adopt.
            string[] legacyFiles = storage.GetFiles(persisted_filter_cache_directory, "*.json").ToArray();

            if (legacyFiles.Length != 1)
                return null;

            string soleLegacyProfileKey = Path.GetFileNameWithoutExtension(legacyFiles[0]);

            if (soleLegacyProfileKey == profileKey || soleLegacyProfileKey == legacyProfileKey)
                return null;

            persisted = loadPersistedFilterStarRatings(soleLegacyProfileKey);

            if (persisted != null)
                Logger.Log($"Adopting sole legacy BPM star-rating filter profile {soleLegacyProfileKey} as {profileKey}.");

            return persisted;
        }

        private PersistedFilterStarRatings? loadPersistedFilterStarRatings(string profileKey)
        {
            string path = $"{persisted_filter_cache_directory}/{profileKey}.json";

            if (!storage.Exists(path))
                return null;

            try
            {
                using Stream? stream = storage.GetStream(path);

                if (stream == null)
                    return null;

                using var reader = new StreamReader(stream);
                return JsonConvert.DeserializeObject<PersistedFilterStarRatings>(reader.ReadToEnd());
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to load persisted BPM star-rating filter cache");
                return null;
            }
        }

        private void savePersistedFilterStarRatings(string profileKey, PersistedFilterStarRatings ratings)
        {
            try
            {
                using Stream stream = storage.GetStream($"{persisted_filter_cache_directory}/{profileKey}.json", FileAccess.Write, FileMode.Create);
                using var writer = new StreamWriter(stream);
                writer.Write(JsonConvert.SerializeObject(ratings));
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to save persisted BPM star-rating filter cache");
            }
        }

        private static string createPersistedBeatmapKey(BeatmapInfo beatmap) => $"{beatmap.ID:N}:{beatmap.Hash}";

        protected override Task<StarDifficulty?> ComputeValueAsync(DifficultyCacheLookup lookup, CancellationToken cancellationToken = default)
        {
            return Task.Factory.StartNew(() =>
            {
                if (CheckExists(lookup, out var existing))
                    return existing;

                return computeDifficulty(lookup, cancellationToken);
            }, cancellationToken, TaskCreationOptions.HideScheduler | TaskCreationOptions.RunContinuationsAsynchronously, updateScheduler);
        }

        protected override bool CacheNullValues => false;

        public Task<List<TimedDifficultyAttributes>> GetTimedDifficultyAttributesAsync(IWorkingBeatmap beatmap, Ruleset ruleset, Mod[] mods, CancellationToken cancellationToken = default)
        {
            return Task.Factory.StartNew(() => ruleset.CreateDifficultyCalculator(beatmap).CalculateTimed(mods, cancellationToken),
                cancellationToken,
                TaskCreationOptions.HideScheduler | TaskCreationOptions.RunContinuationsAsynchronously,
                updateScheduler);
        }

        /// <summary>
        /// Updates all tracked <see cref="BindableStarDifficulty"/> using the current ruleset and mods.
        /// </summary>
        private void updateTrackedBindables()
        {
            lock (bindableUpdateLock)
            {
                cancelTrackedBindableUpdate();

                foreach (var b in trackedBindables)
                {
                    var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(trackedUpdateCancellationSource.Token, b.CancellationToken);
                    linkedCancellationSources.Add(linkedSource);

                    updateBindable(b, currentRuleset.Value, currentMods.Value, linkedSource);
                }
            }
        }

        /// <summary>
        /// Cancels the existing update of all tracked <see cref="BindableStarDifficulty"/> via <see cref="updateTrackedBindables"/>.
        /// </summary>
        private void cancelTrackedBindableUpdate()
        {
            lock (bindableUpdateLock)
            {
                debouncedModSettingsChange?.Cancel();
                debouncedModSettingsChange = null;

                trackedUpdateCancellationSource.Cancel();
                trackedUpdateCancellationSource = new CancellationTokenSource();

                foreach (var c in linkedCancellationSources)
                    c.Dispose();

                linkedCancellationSources.Clear();
            }
        }

        /// <summary>
        /// Updates the value of a <see cref="BindableStarDifficulty"/> with a given ruleset + mods.
        /// </summary>
        /// <param name="bindable">The <see cref="BindableStarDifficulty"/> to update.</param>
        /// <param name="rulesetInfo">The <see cref="IRulesetInfo"/> to update with.</param>
        /// <param name="mods">The <see cref="Mod"/>s to update with.</param>
        /// <param name="linkedCancellationTokenSource">
        /// A cancellation token source that may be used to cancel this update.
        /// This token will be cancelled in one of two scenarios:
        /// <list type="bullet">
        /// <item>The owner of the bindable has requested the cancellation.</item>
        /// <item>An <see cref="Invalidate"/> call has been issued, and as such ongoing calculations must be aborted to avoid stale values being potentially written to bindables.</item>
        /// </list>
        /// </param>
        /// <param name="computationDelay">In the case a cached lookup was not possible, a value in milliseconds of to wait until performing potentially intensive lookup.</param>
        private void updateBindable(BindableStarDifficulty bindable, IRulesetInfo? rulesetInfo, IEnumerable<Mod>? mods, CancellationTokenSource linkedCancellationTokenSource, int computationDelay = 0)
        {
            // GetDifficultyAsync will fall back to existing data from IBeatmapInfo if not locally available
            // (contrary to GetAsync)
            GetDifficultyAsync(bindable.BeatmapInfo, rulesetInfo, mods, linkedCancellationTokenSource.Token, computationDelay)
                .ContinueWith(task =>
                    {
                        // We're on a threadpool thread, but we should exit back to the update thread so consumers can safely handle value-changed events.
                        Schedule(() =>
                        {
                            if (!linkedCancellationTokenSource.IsCancellationRequested)
                            {
                                StarDifficulty? starDifficulty = task.GetResultSafely();

                                if (starDifficulty != null)
                                    bindable.Value = starDifficulty.Value;
                            }

                            // Once the linked cancellation token source is of no remaining use to anybody, clean it up.
                            lock (bindableUpdateLock)
                            {
                                linkedCancellationSources.Remove(linkedCancellationTokenSource);
                                linkedCancellationTokenSource.Dispose();
                            }
                        });
                    },
                    // This continuation MUST run even if the antecedent `GetDifficultyAsync()` call was canceled in order to clean up `linkedCancellationTokenSource`.
                    // Due to this, `ContinueWith()` CANNOT accept `linkedCancellationTokenSource.Token` here, because if it did, then in an event of a cancellation,
                    // the continuation would never be scheduled for execution.
                    CancellationToken.None);
        }

        /// <summary>
        /// Computes the difficulty defined by a <see cref="DifficultyCacheLookup"/> key, and stores it to the timed cache.
        /// </summary>
        /// <param name="key">The <see cref="DifficultyCacheLookup"/> that defines the computation parameters.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The <see cref="StarDifficulty"/>.</returns>
        private StarDifficulty? computeDifficulty(in DifficultyCacheLookup key, CancellationToken cancellationToken = default)
        {
            // In the case that the user hasn't given us a ruleset, use the beatmap's default ruleset.
            var beatmapInfo = key.BeatmapInfo;
            var rulesetInfo = key.Ruleset;

            try
            {
                var ruleset = rulesetInfo.CreateInstance();
                Debug.Assert(ruleset != null);

                // Beatmap loading may update state on beatmap-dependent mods. Never allow those updates
                // to mutate a dictionary key after it has been hashed and inserted into the cache.
                Mod[] calculationMods = key.OrderedMods.Select(mod => mod.DeepClone()).ToArray();

                PlayableCachedWorkingBeatmap workingBeatmap = new PlayableCachedWorkingBeatmap(beatmapManager.GetWorkingBeatmap(key.BeatmapInfo));
                IBeatmap playableBeatmap = workingBeatmap.GetPlayableBeatmap(ruleset.RulesetInfo, calculationMods, cancellationToken);

                var difficulty = ruleset.CreateDifficultyCalculator(workingBeatmap).Calculate(calculationMods, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                var performanceCalculator = ruleset.CreatePerformanceCalculator();
                if (performanceCalculator == null)
                    return new StarDifficulty(difficulty, new PerformanceAttributes());

                ScoreProcessor scoreProcessor = ruleset.CreateScoreProcessor();
                scoreProcessor.Mods.Value = calculationMods;
                scoreProcessor.ApplyBeatmap(playableBeatmap);
                cancellationToken.ThrowIfCancellationRequested();

                ScoreInfo perfectScore = new ScoreInfo(key.BeatmapInfo, ruleset.RulesetInfo)
                {
                    Passed = true,
                    Accuracy = 1,
                    Mods = calculationMods,
                    MaxCombo = scoreProcessor.MaximumCombo,
                    Combo = scoreProcessor.MaximumCombo,
                    TotalScore = scoreProcessor.MaximumTotalScore,
                    Statistics = scoreProcessor.MaximumStatistics,
                    MaximumStatistics = scoreProcessor.MaximumStatistics
                };

                var performance = performanceCalculator.Calculate(perfectScore, difficulty);
                cancellationToken.ThrowIfCancellationRequested();

                return new StarDifficulty(difficulty, performance);
            }
            catch (OperationCanceledException)
            {
                // no need to log, cancellations are expected as part of normal operation.
                return null;
            }
            catch (BeatmapInvalidForRulesetException invalidForRuleset)
            {
                if (rulesetInfo.Equals(beatmapInfo.Ruleset))
                    Logger.Error(invalidForRuleset, $"Failed to convert {beatmapInfo.OnlineID} to the beatmap's default ruleset ({beatmapInfo.Ruleset}).");

                return null;
            }
            catch (Exception unknownException)
            {
                Logger.Error(unknownException, "Failed to calculate beatmap difficulty");

                return null;
            }
        }

        private double? computeStarRating(in DifficultyCacheLookup key, CancellationToken cancellationToken)
        {
            var beatmapInfo = key.BeatmapInfo;
            var rulesetInfo = key.Ruleset;

            try
            {
                var ruleset = rulesetInfo.CreateInstance();
                Debug.Assert(ruleset != null);

                Mod[] calculationMods = key.OrderedMods.Select(mod => mod.DeepClone()).ToArray();

                var workingBeatmap = new PlayableCachedWorkingBeatmap(beatmapManager.GetWorkingBeatmap(beatmapInfo));
                workingBeatmap.GetPlayableBeatmap(ruleset.RulesetInfo, calculationMods, cancellationToken);

                var difficulty = ruleset.CreateDifficultyCalculator(workingBeatmap).Calculate(calculationMods, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                return double.IsFinite(difficulty.StarRating) ? difficulty.StarRating : 0;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (BeatmapInvalidForRulesetException invalidForRuleset)
            {
                if (rulesetInfo.Equals(beatmapInfo.Ruleset))
                    Logger.Error(invalidForRuleset, $"Failed to convert {beatmapInfo.OnlineID} to the beatmap's default ruleset ({beatmapInfo.Ruleset}).");

                return null;
            }
            catch (Exception unknownException)
            {
                Logger.Error(unknownException, "Failed to calculate beatmap difficulty for filtering");
                return null;
            }
        }

        public override void Clear()
        {
            base.Clear();

            lock (filterStarRatingCache)
                filterStarRatingCache.Clear();
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            modSettingChangeTracker?.Dispose();

            cancelTrackedBindableUpdate();
            updateScheduler.Dispose();
            filterScheduler.Dispose();
        }

        public readonly struct DifficultyCacheLookup : IEquatable<DifficultyCacheLookup>
        {
            public readonly BeatmapInfo BeatmapInfo;
            public readonly RulesetInfo Ruleset;
            public readonly Mod[] OrderedMods;

            public DifficultyCacheLookup(BeatmapInfo beatmapInfo, RulesetInfo? ruleset, IEnumerable<Mod>? mods)
            {
                BeatmapInfo = beatmapInfo;
                // In the case that the user hasn't given us a ruleset, use the beatmap's default ruleset.
                Ruleset = ruleset ?? BeatmapInfo.Ruleset;
                OrderedMods = mods?.OrderBy(m => m.Acronym).Select(mod => mod.DeepClone()).ToArray() ?? Array.Empty<Mod>();

                // A BPM Adjust instance follows the currently selected map. Rebind the cloned mod to this
                // lookup's map so cached difficulty always uses this map's source BPM rather than another selection's.
                OrderedMods.ApplyBeatmapInfo(BeatmapInfo);
            }

            public bool Equals(DifficultyCacheLookup other)
                => BeatmapInfo.Equals(other.BeatmapInfo)
                   && Ruleset.Equals(other.Ruleset)
                   && OrderedMods.SequenceEqual(other.OrderedMods);

            public override int GetHashCode()
            {
                var hashCode = new HashCode();

                hashCode.Add(BeatmapInfo.ID);
                hashCode.Add(Ruleset.ShortName);

                foreach (var mod in OrderedMods)
                    hashCode.Add(mod);

                return hashCode.ToHashCode();
            }
        }

        private sealed class PersistedFilterStarRatings
        {
            public int Version { get; set; }
            public Dictionary<string, double> Ratings { get; set; } = new Dictionary<string, double>();
            public HashSet<string> UnavailableBeatmaps { get; set; } = new HashSet<string>();
        }

        public sealed record FilterStarRatingProfile(IReadOnlyDictionary<Guid, double> Ratings, int TotalMaps);

        private class BindableStarDifficulty : Bindable<StarDifficulty>
        {
            public IBeatmapInfo BeatmapInfo;
            public readonly CancellationToken CancellationToken;

            public BindableStarDifficulty(IBeatmapInfo beatmapInfo, CancellationToken cancellationToken)
            {
                BeatmapInfo = beatmapInfo;
                CancellationToken = cancellationToken;
            }
        }

        /// <summary>
        /// A working beatmap that caches its playable representation.
        /// This is intended as single-use for when it is guaranteed that the playable beatmap can be reused.
        /// </summary>
        private class PlayableCachedWorkingBeatmap : IWorkingBeatmap
        {
            private readonly IWorkingBeatmap working;
            private IBeatmap? playable;

            public PlayableCachedWorkingBeatmap(IWorkingBeatmap working)
            {
                this.working = working;
            }

            public IBeatmap GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList<Mod> mods)
                => playable ??= working.GetPlayableBeatmap(ruleset, mods);

            public IBeatmap GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList<Mod> mods, CancellationToken cancellationToken)
                => playable ??= working.GetPlayableBeatmap(ruleset, mods, cancellationToken);

            IBeatmapInfo IWorkingBeatmap.BeatmapInfo => working.BeatmapInfo;
            bool IWorkingBeatmap.BeatmapLoaded => working.BeatmapLoaded;
            bool IWorkingBeatmap.TrackLoaded => working.TrackLoaded;
            IBeatmap IWorkingBeatmap.Beatmap => working.Beatmap;
            Texture IWorkingBeatmap.GetBackground() => working.GetBackground();
            Texture IWorkingBeatmap.GetPanelBackground() => working.GetPanelBackground();
            Waveform IWorkingBeatmap.Waveform => working.Waveform;
            Storyboard IWorkingBeatmap.Storyboard => working.Storyboard;
            ISkin IWorkingBeatmap.Skin => working.Skin;
            Track IWorkingBeatmap.Track => working.Track;
            Track IWorkingBeatmap.LoadTrack() => working.LoadTrack();
            Stream IWorkingBeatmap.GetStream(string storagePath) => working.GetStream(storagePath);
            void IWorkingBeatmap.BeginAsyncLoad() => working.BeginAsyncLoad();
            void IWorkingBeatmap.CancelAsyncLoad() => working.CancelAsyncLoad();
            void IWorkingBeatmap.PrepareTrackForPreview(bool looping, double? offsetFromPreviewPoint) => working.PrepareTrackForPreview(looping, offsetFromPreviewPoint);
        }
    }
}

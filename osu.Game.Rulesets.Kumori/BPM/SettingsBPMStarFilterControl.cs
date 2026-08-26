// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Select;
using osuTK;

namespace osu.Game.Rulesets.Kumori.BPM
{
    public partial class SettingsBPMStarFilterControl : SettingsItem<KumoriStarRatingFilterMode>
    {
        protected override Drawable CreateControl() => new StarFilterControl(() => SettingSourceObject as KumoriModBPMAdjust)
        {
            RelativeSizeAxes = Axes.X,
        };

        private sealed partial class StarFilterControl : CompositeDrawable, IHasCurrentValue<KumoriStarRatingFilterMode>
        {
            private const int checkpoint_interval = 10000;
            private const string preferences_file = "kumori-star-filter.json";
            private const string preferences_temporary_file = "kumori-star-filter.json.tmp";

            private readonly BindableWithCurrent<KumoriStarRatingFilterMode> current = new BindableWithCurrent<KumoriStarRatingFilterMode>();
            private readonly Func<KumoriModBPMAdjust?> getMod;

            private OsuEnumDropdown<KumoriStarRatingFilterMode> modeDropdown = null!;
            private OsuTextBox minimum = null!;
            private OsuTextBox maximum = null!;
            private RoundedButton calculateButton = null!;
            private RoundedButton cancelButton = null!;
            private OsuSpriteText status = null!;

            private Storage storage = null!;
            private RealmAccess realm = null!;
            private BeatmapManager beatmapManager = null!;

            [Resolved]
            private IBindable<RulesetInfo> selectedRuleset { get; set; } = null!;

            [Resolved]
            private IBindable<IReadOnlyList<Mod>> selectedMods { get; set; } = null!;

            [Resolved(canBeNull: true)]
            private ISongSelect? songSelect { get; set; }

            private ModSettingChangeTracker? settingChangeTracker;
            private string? trackedProfileKey;
            private CancellationTokenSource? calculationCancellation;
            private KumoriModBPMAdjust? mod;
            private bool loadingPreferences;
            private int latestProgress;
            private int progressUpdatePending;

            public Bindable<KumoriStarRatingFilterMode> Current
            {
                get => current.Current;
                set => current.Current = value;
            }

            public StarFilterControl(Func<KumoriModBPMAdjust?> getMod)
            {
                this.getMod = getMod;
                AutoSizeAxes = Axes.Y;

                InternalChild = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(5),
                    Children =
                    [
                        modeDropdown = new OsuEnumDropdown<KumoriStarRatingFilterMode>
                        {
                            RelativeSizeAxes = Axes.X,
                            Current = Current,
                        },
                        new GridContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 35,
                            ColumnDimensions =
                            [
                                new Dimension(),
                                new Dimension(GridSizeMode.Absolute, 24),
                                new Dimension(),
                            ],
                            Content = new[]
                            {
                                new Drawable[]
                                {
                                    minimum = new OsuTextBox
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        PlaceholderText = "Minimum stars",
                                        CommitOnFocusLost = true,
                                    },
                                    new OsuSpriteText
                                    {
                                        Text = "–",
                                        Anchor = Anchor.Centre,
                                        Origin = Anchor.Centre,
                                    },
                                    maximum = new OsuTextBox
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        PlaceholderText = "Maximum stars",
                                        CommitOnFocusLost = true,
                                    },
                                },
                            },
                        },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(5),
                            Children =
                            [
                                calculateButton = new RoundedButton
                                {
                                    Width = 130,
                                    Height = 30,
                                    Text = "Calculate maps",
                                    Action = beginCalculation,
                                },
                                cancelButton = new RoundedButton
                                {
                                    Width = 80,
                                    Height = 30,
                                    Text = "Cancel",
                                    Action = cancelCalculation,
                                },
                                status = new OsuSpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                },
                            ],
                        },
                    ],
                };

                Current.BindValueChanged(_ => filterSettingChanged());
            }

            [BackgroundDependencyLoader]
            private void load(Storage storage, RealmAccess realm, BeatmapManager beatmapManager)
            {
                this.storage = storage;
                this.realm = realm;
                this.beatmapManager = beatmapManager;
                mod = getMod();

                if (mod == null)
                    return;

                loadPreferences();
                minimum.Current.BindTo(mod.StarFilterMinimum);
                maximum.Current.BindTo(mod.StarFilterMaximum);
                minimum.Current.BindValueChanged(_ => filterSettingChanged());
                maximum.Current.BindValueChanged(_ => filterSettingChanged());

                selectedMods.BindValueChanged(modsChanged, true);
                publishFilterState();
                updateProfileState();
                updateControls();
            }

            private void modsChanged(ValueChangedEvent<IReadOnlyList<Mod>> mods)
            {
                settingChangeTracker?.Dispose();
                settingChangeTracker = new ModSettingChangeTracker(mods.NewValue);
                settingChangeTracker.SettingChanged += _ =>
                {
                    string profileKey = KumoriStarRatingIndex.CreateProfileKey(selectedRuleset.Value, selectedMods.Value);

                    // The highlighted map changes BPM Adjust's computed rate, but not the
                    // persistent target-BPM/mod profile. Ignore that synthetic menu-display pulse
                    // so selecting a song never refilters the entire library.
                    if (profileKey == trackedProfileKey)
                        return;

                    trackedProfileKey = profileKey;
                    publishFilterState();
                    updateProfileState();
                    triggerRefilter();
                };

                trackedProfileKey = KumoriStarRatingIndex.CreateProfileKey(selectedRuleset.Value, mods.NewValue);
                publishFilterState();
            }

            private void filterSettingChanged()
            {
                if (storage == null || mod == null)
                    return;

                if (!loadingPreferences)
                    savePreferences();

                publishFilterState();
                updateProfileState();
                updateControls();
                triggerRefilter();
            }

            private void publishFilterState()
            {
                if (mod == null)
                    return;

                KumoriStarFilterState.Publish(selectedRuleset.Value, selectedMods.Value, Current.Value, minimum.Current.Value, maximum.Current.Value);
            }

            private void updateProfileState()
            {
                if (storage == null || mod == null || Current.Value != KumoriStarRatingFilterMode.PostMod || calculationCancellation != null)
                    return;

                string profileKey = KumoriStarRatingIndex.CreateProfileKey(selectedRuleset.Value, selectedMods.Value);

                if (KumoriStarRatingIndex.Load(storage, selectedRuleset.Value, selectedMods.Value))
                    status.Text = KumoriStarRatingIndex.GetSnapshot(profileKey).Complete ? "Exact star index loaded" : "Partial index loaded — press Calculate maps to resume";
                else if (calculationCancellation == null)
                    status.Text = "Calculate maps once for this BPM/AR/CS setup";
            }

            private void beginCalculation()
            {
                if (calculationCancellation != null || mod == null)
                    return;

                var cancellation = calculationCancellation = new CancellationTokenSource();
                RulesetInfo rulesetInfo = selectedRuleset.Value;
                Mod[] mods = selectedMods.Value.Select(selectedMod => selectedMod.DeepClone()).ToArray();
                string profileKey = KumoriStarRatingIndex.CreateProfileKey(rulesetInfo, mods);
                KumoriStarRatingIndex.Load(storage, rulesetInfo, mods);
                KumoriStarRatingProfileSnapshot snapshot = KumoriStarRatingIndex.GetSnapshot(profileKey);

                updateControls();
                status.Text = "Loading maps…";
                latestProgress = 0;
                progressUpdatePending = 0;

                Task.Factory.StartNew(
                    () => calculate(rulesetInfo, mods, profileKey, snapshot, cancellation.Token),
                    cancellation.Token,
                    TaskCreationOptions.HideScheduler | TaskCreationOptions.RunContinuationsAsynchronously,
                     TaskScheduler.Default).ContinueWith(task => Schedule(() => finishCalculation(task, cancellation)), CancellationToken.None);
            }

            private CalculationResult calculate(RulesetInfo rulesetInfo, Mod[] mods, string profileKey, KumoriStarRatingProfileSnapshot snapshot,
                                                CancellationToken cancellationToken)
            {
                var ratings = snapshot.Ratings;
                var unavailable = snapshot.UnavailableBeatmaps;
                List<BeatmapIdentity> library = realm.Run(r =>
                    r.All<BeatmapInfo>()
                     .NotDeleted()
                     .Where(beatmap => !beatmap.Hidden)
                     .AsEnumerable()
                     .Where(beatmap => beatmap.AllowGameplayWithRuleset(rulesetInfo, true))
                     .Select(beatmap => new BeatmapIdentity(beatmap.ID, beatmap.Hash))
                     .ToList());

                var pendingIdentities = new List<BeatmapIdentity>();

                foreach (BeatmapIdentity beatmap in library)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string key = KumoriStarRatingIndex.CreateBeatmapKey(beatmap.ID, beatmap.Hash);

                    if (!ratings.ContainsKey(key) && (snapshot.Complete || !unavailable.Contains(key)))
                        pendingIdentities.Add(beatmap);
                }

                int completed = library.Count - pendingIdentities.Count;
                int reused = completed;
                reportProgress(completed, library.Count);

                List<(BeatmapInfo Beatmap, string Key)> pending;

                if (pendingIdentities.Count == 0)
                    pending = [];
                else
                {
                    reportStatus($"Preparing {pendingIdentities.Count:N0} maps…");
                    HashSet<Guid> pendingIds = pendingIdentities.Select(beatmap => beatmap.ID).ToHashSet();
                    List<BeatmapInfo> pendingBeatmaps = realm.Run(r =>
                        r.All<BeatmapInfo>()
                         .NotDeleted()
                         .Where(beatmap => !beatmap.Hidden)
                         .AsEnumerable()
                         .Where(beatmap => pendingIds.Contains(beatmap.ID))
                         .Detach());

                    pending = pendingBeatmaps.Select(beatmap => (beatmap, KumoriStarRatingIndex.CreateBeatmapKey(beatmap))).ToList();
                }

                var resultLock = new object();
                int nextCheckpoint = completed + checkpoint_interval;
                bool complete = false;

                try
                {
                    var parallelOptions = new ParallelOptions
                    {
                        CancellationToken = cancellationToken,
                        MaxDegreeOfParallelism = KumoriStarRatingCalculation.WorkerCount(Environment.ProcessorCount),
                        TaskScheduler = TaskScheduler.Default,
                    };

                    Parallel.ForEach(pending, parallelOptions,
                        () => new CalculationWorker(rulesetInfo.CreateInstance(), mods.Select(selectedMod => selectedMod.DeepClone()).ToArray()),
                        (item, _, worker) =>
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            double? rating = null;

                            try
                            {
                                foreach (KumoriModBPMAdjust bpm in worker.Mods.OfType<KumoriModBPMAdjust>())
                                    bpm.SetSourceBPM(BPMResolver.FromBeatmapInfo(item.Beatmap));

                                // Always calculate against this beatmap after rebinding BPM. The
                                // shared osu! cache does not include BPM Adjust's derived source BPM
                                // in its mod key and can therefore contain a rating for the wrong rate.
                                WorkingBeatmap workingBeatmap = beatmapManager.GetWorkingBeatmap(item.Beatmap);
                                rating = worker.Ruleset.CreateDifficultyCalculator(workingBeatmap).Calculate(worker.Mods, cancellationToken).StarRating;

                                if (!double.IsFinite(rating.Value))
                                    rating = null;
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception exception)
                            {
                                Logger.Error(exception, $"Failed to calculate Kumori star rating for beatmap {item.Beatmap.OnlineID}");
                            }

                            lock (resultLock)
                            {
                                if (rating.HasValue)
                                {
                                    ratings[item.Key] = rating.Value;
                                    unavailable.Remove(item.Key);
                                }
                                else
                                    unavailable.Add(item.Key);
                            }

                            int currentProgress = Interlocked.Increment(ref completed);
                            reportProgress(currentProgress, library.Count);

                            if (currentProgress >= Volatile.Read(ref nextCheckpoint))
                            {
                                lock (resultLock)
                                {
                                    if (currentProgress >= nextCheckpoint)
                                    {
                                        nextCheckpoint = currentProgress + checkpoint_interval;
                                        KumoriStarRatingIndex.Store(storage, profileKey, ratings, unavailable, false);
                                    }
                                }
                            }

                            return worker;
                        },
                        _ => { });

                    complete = true;
                }
                finally
                {
                    lock (resultLock)
                        KumoriStarRatingIndex.Store(storage, profileKey, ratings, unavailable, complete);
                }

                cancellationToken.ThrowIfCancellationRequested();
                int availableForCurrentLibrary = 0;
                int unavailableForCurrentLibrary = 0;

                foreach (BeatmapIdentity beatmap in library)
                {
                    string key = KumoriStarRatingIndex.CreateBeatmapKey(beatmap.ID, beatmap.Hash);

                    if (ratings.ContainsKey(key))
                        availableForCurrentLibrary++;
                    else if (unavailable.Contains(key))
                        unavailableForCurrentLibrary++;
                }

                return new CalculationResult(library.Count, availableForCurrentLibrary, unavailableForCurrentLibrary, reused);
            }

            private void reportStatus(string text) => Schedule(() => status.Text = text);

            private void reportProgress(int completed, int total)
            {
                Interlocked.Exchange(ref latestProgress, completed);

                if (Interlocked.CompareExchange(ref progressUpdatePending, 1, 0) != 0)
                    return;

                Schedule(() =>
                {
                    Interlocked.Exchange(ref progressUpdatePending, 0);
                    status.Text = $"{Interlocked.CompareExchange(ref latestProgress, 0, 0)} / {total}";
                });
            }

            private void finishCalculation(Task<CalculationResult> task, CancellationTokenSource cancellation)
            {
                if (!ReferenceEquals(calculationCancellation, cancellation))
                {
                    cancellation.Dispose();
                    return;
                }

                calculationCancellation = null;
                cancellation.Dispose();

                if (task.IsCanceled)
                    status.Text = "Calculation cancelled";
                else if (task.IsFaulted)
                {
                    Logger.Error(task.Exception!, "Kumori star-rating calculation failed");
                    status.Text = "Calculation failed — see logs";
                }
                else
                {
                    CalculationResult result = task.GetResultSafely();
                    status.Text = result.Unavailable == 0
                        ? $"Ready — {result.Ratings} maps ({result.Reused} reused)"
                        : $"Ready — {result.Ratings}/{result.Total} maps ({result.Reused} reused)";
                    triggerRefilter();
                }

                updateControls();
            }

            private void cancelCalculation() => calculationCancellation?.Cancel();

            private void updateControls()
            {
                bool postMod = Current.Value == KumoriStarRatingFilterMode.PostMod;
                bool calculating = calculationCancellation != null;

                calculateButton.Alpha = postMod ? 1 : 0;
                calculateButton.Enabled.Value = postMod && !calculating;
                cancelButton.Alpha = calculating ? 1 : 0;
                cancelButton.Enabled.Value = calculating;

                if (!postMod)
                    status.Text = Current.Value == KumoriStarRatingFilterMode.PreMod ? "Uses imported map stars immediately" : string.Empty;
            }

            private void triggerRefilter()
            {
                // The mod customisation panel and song select receive different bound copies of the
                // ruleset/mod bindables. TriggerChange() on this copy does not propagate, so request
                // a criteria rebuild from the active song-select control itself.
                PropertyInfo? filterControlProperty = songSelect?.GetType().GetProperty("FilterControl", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                FilterControl? filterControl = filterControlProperty?.GetValue(songSelect) as FilterControl;
                MethodInfo? updateCriteria = typeof(FilterControl).GetMethod("updateCriteria", BindingFlags.Instance | BindingFlags.NonPublic);

                if (filterControl != null && updateCriteria != null)
                {
                    try
                    {
                        updateCriteria.Invoke(filterControl, [false]);
                        Logger.Log("Kumori requested a direct song-list refilter");
                        return;
                    }
                    catch (Exception exception)
                    {
                        Logger.Error(exception, "Kumori could not directly refresh the song list");
                    }
                }

                // Retain a compatibility fallback for client versions where FilterControl changes.
                Logger.Log("Kumori could not find the active song-list filter control; trying bindable fallback", LoggingTarget.Runtime, LogLevel.Error);

                if (selectedRuleset is Bindable<RulesetInfo> mutableRuleset)
                {
                    mutableRuleset.TriggerChange();
                    return;
                }

                MethodInfo? triggerChange = selectedRuleset.GetType().GetMethod(nameof(Bindable<RulesetInfo>.TriggerChange), BindingFlags.Instance | BindingFlags.Public);

                if (triggerChange != null)
                    triggerChange.Invoke(selectedRuleset, null);
                else
                    Logger.Log($"Kumori could not pulse wrapped ruleset bindable {selectedRuleset.GetType().FullName}", LoggingTarget.Runtime, LogLevel.Error);
            }

            private void loadPreferences()
            {
                if (mod == null || !storage.Exists(preferences_file))
                    return;

                try
                {
                    using Stream? stream = storage.GetStream(preferences_file);

                    if (stream == null)
                        return;

                    using var reader = new StreamReader(stream);
                    Preferences? preferences = JsonConvert.DeserializeObject<Preferences>(reader.ReadToEnd());

                    if (preferences == null)
                        return;

                    loadingPreferences = true;
                    mod.StarFilterMode.Value = preferences.Mode;
                    mod.StarFilterMinimum.Value = preferences.Minimum ?? string.Empty;
                    mod.StarFilterMaximum.Value = preferences.Maximum ?? string.Empty;
                }
                catch (Exception exception)
                {
                    Logger.Error(exception, "Failed to load Kumori star-filter preferences");
                }
                finally
                {
                    loadingPreferences = false;
                }
            }

            private void savePreferences()
            {
                if (mod == null)
                    return;

                try
                {
                    string serialised = JsonConvert.SerializeObject(new Preferences
                    {
                        Mode = mod.StarFilterMode.Value,
                        Minimum = mod.StarFilterMinimum.Value,
                        Maximum = mod.StarFilterMaximum.Value,
                    });

                    using (Stream stream = storage.GetStream(preferences_temporary_file, FileAccess.Write, FileMode.Create))
                    using (var writer = new StreamWriter(stream))
                        writer.Write(serialised);

                    storage.Move(preferences_temporary_file, preferences_file);
                }
                catch (Exception exception)
                {
                    Logger.Error(exception, "Failed to save Kumori star-filter preferences");
                }
            }

            protected override void Dispose(bool isDisposing)
            {
                calculationCancellation?.Cancel();
                calculationCancellation?.Dispose();
                settingChangeTracker?.Dispose();

                if (trackedProfileKey != null)
                    KumoriStarFilterState.ClearIfCurrentProfileMatches(trackedProfileKey);

                base.Dispose(isDisposing);
            }

            private readonly record struct CalculationResult(int Total, int Ratings, int Unavailable, int Reused);

            private readonly record struct BeatmapIdentity(Guid ID, string Hash);

            private sealed record CalculationWorker(Ruleset Ruleset, Mod[] Mods);

            private sealed class Preferences
            {
                [JsonProperty("mode")]
                public KumoriStarRatingFilterMode Mode { get; set; }

                [JsonProperty("minimum")]
                public string? Minimum { get; set; }

                [JsonProperty("maximum")]
                public string? Maximum { get; set; }
            }
        }
    }

    internal static class KumoriStarRatingCalculation
    {
        public static int WorkerCount(int processorCount) => Math.Max(1, processorCount - 1);

        public static KumoriStarRatingWorkPlan CreateWorkPlan(IReadOnlyList<BeatmapInfo> beatmaps, KumoriStarRatingProfileSnapshot snapshot,
                                                               CancellationToken cancellationToken = default)
        {
            var pending = new List<(BeatmapInfo Beatmap, string Key)>();
            int reused = 0;

            foreach (BeatmapInfo beatmap in beatmaps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string key = KumoriStarRatingIndex.CreateBeatmapKey(beatmap);

                if (snapshot.Ratings.ContainsKey(key) || (!snapshot.Complete && snapshot.UnavailableBeatmaps.Contains(key)))
                    reused++;
                else
                    pending.Add((beatmap, key));
            }

            return new KumoriStarRatingWorkPlan(pending, reused);
        }
    }

    internal readonly record struct KumoriStarRatingWorkPlan(List<(BeatmapInfo Beatmap, string Key)> Pending, int Reused);

    /// <summary>
    /// Reads only already-computed entries from osu!'s central difficulty cache. Reflection keeps
    /// compatibility with official client versions where the cache lookup helper is non-public.
    /// </summary>
    internal sealed class KumoriDifficultyCacheReader
    {
        private readonly BeatmapDifficultyCache cache;
        private readonly ConstructorInfo? lookupConstructor;
        private readonly MethodInfo? checkExists;
        private int disabled;

        public KumoriDifficultyCacheReader(BeatmapDifficultyCache cache)
        {
            this.cache = cache;
            Type cacheType = cache.GetType();
            Type? lookupType = cacheType.GetNestedType("DifficultyCacheLookup", BindingFlags.Public | BindingFlags.NonPublic);
            lookupConstructor = lookupType?.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(constructor => constructor.GetParameters().Length == 3);

            for (Type? type = cacheType; type != null && checkExists == null; type = type.BaseType)
            {
                checkExists = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                  .FirstOrDefault(method => method.Name == "CheckExists" && method.GetParameters().Length == 2);
            }
        }

        public bool TryGet(BeatmapInfo beatmap, RulesetInfo ruleset, Mod[] mods, out double rating)
        {
            rating = 0;

            if (lookupConstructor == null || checkExists == null || Volatile.Read(ref disabled) != 0)
                return false;

            try
            {
                object lookup = lookupConstructor.Invoke([beatmap, ruleset, mods]);
                object?[] arguments = [lookup, null];

                if (checkExists.Invoke(cache, arguments) is not true || arguments[1] is not StarDifficulty difficulty)
                    return false;

                rating = difficulty.Stars;
                return double.IsFinite(rating);
            }
            catch
            {
                // Cache internals may change between official client releases. A miss is safe and
                // falls back to the ruleset's own parallel difficulty calculation.
                Interlocked.Exchange(ref disabled, 1);
                return false;
            }
        }
    }
}

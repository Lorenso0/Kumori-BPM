// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Configuration;
using osu.Game.Online.API;
using osu.Game.Rulesets.Kumori.BPM;
using osu.Game.Rulesets.Kumori.Updates;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Beatmaps;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Screens.Select;
using osu.Game.Tests.Beatmaps;
using osu.Game.Utils;
using osuTK;
using BPMAdjustAudioMode = osu.Game.Rulesets.Kumori.BPM.BPMAdjustAudioMode;
using BPMAdjustBeatAccentMode = osu.Game.Rulesets.Kumori.BPM.BPMAdjustBeatAccentMode;
using BPMAdjustHitsoundMode = osu.Game.Rulesets.Kumori.BPM.BPMAdjustHitsoundMode;

namespace osu.Game.Rulesets.Kumori.Tests
{
    [TestFixture]
    public class KumoriRulesetTest
    {
        [Test]
        public void TestIdentityDoesNotCollideWithOfficialRuleset()
        {
            var ruleset = new KumoriRuleset();

            Assert.Multiple(() =>
            {
                Assert.That(ruleset.RulesetInfo.ShortName, Is.EqualTo("kumori"));
                Assert.That(ruleset.RulesetInfo.OnlineID, Is.EqualTo(-1));
                Assert.That(ruleset, Is.Not.InstanceOf<ILegacyRuleset>());
                Assert.That(ruleset.RulesetInfo.InstantiationInfo, Does.Contain(nameof(KumoriRuleset)));
                Assert.That(typeof(KumoriRuleset).Assembly.GetTypes().Count(t => t.IsPublic && t.IsSubclassOf(typeof(Ruleset))), Is.EqualTo(1));
            });
        }

        [Test]
        public void TestRulesetApiVersionMatchesHostGeneration()
        {
            Assert.Multiple(() =>
            {
                Assert.That(KumoriRuleset.ResolveRulesetApiVersion(false), Is.EqualTo(Ruleset.CURRENT_RULESET_API_VERSION));
                Assert.That(KumoriRuleset.ResolveRulesetApiVersion(true), Is.EqualTo("2026.818.0"));
                Assert.That(new KumoriRuleset().RulesetAPIVersionSupported, Is.EqualTo(Ruleset.CURRENT_RULESET_API_VERSION));
            });
        }

        [Test]
        public void TestOfficialOsuModsArePreservedAndBPMIsAdded()
        {
            var kumori = new KumoriRuleset();
            var official = new OsuRuleset();

            foreach (ModType type in System.Enum.GetValues<ModType>())
            {
                int expected = official.GetModsFor(type).Where(m => m.Acronym != "BPM").SelectMany(m => (m as MultiMod)?.Mods ?? [m]).Count();
                int actual = kumori.GetModsFor(type).SelectMany(m => (m as MultiMod)?.Mods ?? [m]).Count();

                Assert.That(actual, Is.EqualTo(expected + (type == ModType.DifficultyIncrease ? 1 : 0)), type.ToString());
            }

            Assert.That(kumori.CreateAllMods().OfType<KumoriModBPMAdjust>().Count(), Is.EqualTo(1));
        }

        [Test]
        public void TestBPMAdjustIsImmediatelyAfterHidden()
        {
            Mod[] difficultyMods = new KumoriRuleset().GetModsFor(ModType.DifficultyIncrease).ToArray();
            int hiddenIndex = System.Array.FindIndex(difficultyMods, mod => mod is ModHidden
                                                                            || mod is MultiMod multiMod && multiMod.Mods.Any(nestedMod => nestedMod is ModHidden));
            int bpmIndex = System.Array.FindIndex(difficultyMods, mod => mod is KumoriModBPMAdjust);

            Assert.Multiple(() =>
            {
                Assert.That(new KumoriModBPMAdjust().Type, Is.EqualTo(ModType.DifficultyIncrease));
                Assert.That(hiddenIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(bpmIndex, Is.EqualTo(hiddenIndex + 1));
                Assert.That(new KumoriRuleset().GetModsFor(ModType.Fun).Any(mod => mod.Acronym == "BPM"), Is.False);
            });
        }

        [TestCase(120, 180, 1.5)]
        [TestCase(200, 150, 0.75)]
        [TestCase(128, 174.5, 1.36328125)]
        public void TestRateCalculation(double sourceBPM, double targetBPM, double expectedRate)
        {
            var mod = new KumoriModBPMAdjust();
            mod.SetSourceBPM(sourceBPM);
            mod.TargetBPM.Value = targetBPM;

            Assert.Multiple(() =>
            {
                Assert.That(mod.SourceBPM, Is.EqualTo(sourceBPM));
                Assert.That(mod.SpeedChange.Value, Is.EqualTo(expectedRate).Within(1e-12));
                Assert.That(mod.ApplyToRate(0, 1), Is.EqualTo(expectedRate).Within(1e-12));
            });
        }

        [Test]
        public void TestMaterialisedBeatmapResolvesSourceBPMBeforeDifficultyMods()
        {
            var beatmap = new Beatmap<OsuHitObject>();
            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            beatmap.HitObjects.Add(new HitCircle { StartTime = 1000 });

            var mod = new KumoriModBPMAdjust { TargetBPM = { Value = 180 } };
            ((IApplicableAfterBeatmapConversion)mod).ApplyToBeatmap(beatmap);

            Assert.Multiple(() =>
            {
                Assert.That(mod.SourceBPM, Is.EqualTo(120));
                Assert.That(mod.SpeedChange.Value, Is.EqualTo(1.5));
            });
        }

        [Test]
        public void TestRateChangingModsAreIncompatible()
        {
            var bpm = new KumoriModBPMAdjust();

            Assert.Multiple(() =>
            {
                Assert.That(ModUtils.CheckCompatibleSet([bpm, new OsuModDoubleTime()]), Is.False);
                Assert.That(ModUtils.CheckCompatibleSet([bpm, new OsuModHalfTime()]), Is.False);
                Assert.That(bpm.Ranked, Is.False);
                Assert.That(bpm.ValidForMultiplayer, Is.False);
            });
        }

        [Test]
        public void TestDisplayDifficultyUsesSelectedBeatmapBPM()
        {
            var beatmap = new BeatmapInfo
            {
                BPM = 120,
                Difficulty = new BeatmapDifficulty
                {
                    ApproachRate = 9,
                    OverallDifficulty = 8,
                },
            };
            var mod = new KumoriModBPMAdjust { TargetBPM = { Value = 180 } };

            _ = new KumoriRuleset().GetAdjustedDisplayDifficulty(beatmap, [mod]);

            Assert.Multiple(() =>
            {
                Assert.That(mod.SourceBPM, Is.EqualTo(120));
                Assert.That(mod.SpeedChange.Value, Is.EqualTo(1.5));
            });
        }

        [Test]
        public void TestBPMCompensationRunsAfterDifficultyAdjust()
        {
            var beatmap = new BeatmapInfo
            {
                BPM = 120,
                Difficulty = new BeatmapDifficulty
                {
                    ApproachRate = 5,
                    OverallDifficulty = 5,
                },
            };
            var bpm = new KumoriModBPMAdjust
            {
                TargetBPM = { Value = 180 },
                ScaleMapStatsWithBPM = { Value = false },
            };
            var difficultyAdjust = new OsuModDifficultyAdjust
            {
                ApproachRate = { Value = 10 },
                OverallDifficulty = { Value = 9 },
            };

            BeatmapDifficulty displayed = new KumoriRuleset().GetAdjustedDisplayDifficulty(beatmap, [bpm, difficultyAdjust]);
            var playableBeatmap = new Beatmap<OsuHitObject>
            {
                Difficulty = new BeatmapDifficulty(beatmap.Difficulty),
            };
            ((IApplicableToDifficulty)difficultyAdjust).ApplyToDifficulty(playableBeatmap.Difficulty);
            ((IApplicableToBeatmapProcessor)bpm).ApplyToBeatmapProcessor(new OsuBeatmapProcessor(playableBeatmap));
            double effectivePreempt = IBeatmapDifficultyInfo.DifficultyRange(playableBeatmap.Difficulty.ApproachRate, OsuHitObject.PREEMPT_RANGE) / bpm.SpeedChange.Value;
            double effectiveGreatWindow = IBeatmapDifficultyInfo.DifficultyRange(playableBeatmap.Difficulty.OverallDifficulty, OsuHitWindows.GREAT_WINDOW_RANGE) / bpm.SpeedChange.Value;

            Assert.Multiple(() =>
            {
                Assert.That(bpm, Is.Not.InstanceOf<IApplicableToDifficulty>());
                Assert.That(bpm, Is.InstanceOf<IApplicableToBeatmapProcessor>());
                Assert.That(displayed.ApproachRate, Is.EqualTo(10).Within(0.01));
                Assert.That(displayed.OverallDifficulty, Is.EqualTo(9).Within(0.01));
                Assert.That(effectivePreempt, Is.EqualTo(IBeatmapDifficultyInfo.DifficultyRange(10, OsuHitObject.PREEMPT_RANGE)).Within(0.01));
                Assert.That(effectiveGreatWindow, Is.EqualTo(IBeatmapDifficultyInfo.DifficultyRange(9, OsuHitWindows.GREAT_WINDOW_RANGE)).Within(0.01));
            });
        }

        [Test]
        public void TestCloneAndApiSerialisationPreserveSettings()
        {
            var original = new KumoriModBPMAdjust
            {
                TargetBPM = { Value = 174.5 },
                AudioMode = { Value = BPMAdjustAudioMode.CustomPitch },
                CustomPitchSemitones = { Value = 7 },
                BeatAccents = { Value = BPMAdjustBeatAccentMode.Metronome },
                HitsoundMode = { Value = BPMAdjustHitsoundMode.FollowMusicPitch },
                ScaleMapStatsWithBPM = { Value = false },
                StarFilterMode = { Value = KumoriStarRatingFilterMode.PostMod },
            };
            original.SetSourceBPM(128);

            var clone = (KumoriModBPMAdjust)original.DeepClone();
            var restored = (KumoriModBPMAdjust)new APIMod(original).ToMod(new KumoriRuleset());

            Assert.Multiple(() =>
            {
                Assert.That(clone.SourceBPM, Is.EqualTo(128));
                Assert.That(clone.SpeedChange.Value, Is.EqualTo(174.5 / 128).Within(1e-12));
                Assert.That(restored.TargetBPM.Value, Is.EqualTo(174.5));
                Assert.That(restored.AudioMode.Value, Is.EqualTo(BPMAdjustAudioMode.CustomPitch));
                Assert.That(restored.CustomPitchSemitones.Value, Is.EqualTo(7));
                Assert.That(restored.BeatAccents.Value, Is.EqualTo(BPMAdjustBeatAccentMode.Metronome));
                Assert.That(restored.HitsoundMode.Value, Is.EqualTo(BPMAdjustHitsoundMode.FollowMusicPitch));
                Assert.That(restored.ScaleMapStatsWithBPM.Value, Is.False);
                Assert.That(restored.StarFilterMode.Value, Is.EqualTo(KumoriStarRatingFilterMode.PostMod));
            });
        }

        [Test]
        public void TestGlobalPreModStarRangeUsesDisplayedRatingPrecision()
        {
            var mod = new KumoriModBPMAdjust
            {
                StarFilterMode = { Value = KumoriStarRatingFilterMode.PreMod },
                StarFilterMinimum = { Value = "4.99" },
                StarFilterMaximum = { Value = "5.99" },
            };
            var criteria = new FilterCriteria { Mods = [mod] };
            var filter = new KumoriRuleset().CreateRulesetFilterCriteria();

            Assert.Multiple(() =>
            {
                Assert.That(filter.Matches(new BeatmapInfo { StarRating = 4.989 }, criteria), Is.False);
                Assert.That(filter.Matches(new BeatmapInfo { StarRating = 4.999 }, criteria), Is.True);
                Assert.That(filter.Matches(new BeatmapInfo { StarRating = 5.999 }, criteria), Is.True);
                Assert.That(filter.Matches(new BeatmapInfo { StarRating = 6 }, criteria), Is.False);
            });
        }

        [Test]
        public void TestPostModStarRangePreservesMapsUntilExactIndexExists()
        {
            KumoriStarRatingIndex.ClearMemory();

            var mod = new KumoriModBPMAdjust
            {
                TargetBPM = { Value = 173.25 },
                StarFilterMode = { Value = KumoriStarRatingFilterMode.PostMod },
                StarFilterMinimum = { Value = "4" },
                StarFilterMaximum = { Value = "5" },
            };
            var ruleset = new KumoriRuleset();
            var criteria = new FilterCriteria
            {
                Ruleset = ruleset.RulesetInfo,
                Mods = [mod],
            };

            Assert.That(ruleset.CreateRulesetFilterCriteria().Matches(new BeatmapInfo { StarRating = 20 }, criteria), Is.True);
        }

        [Test]
        public void TestPostModStarRangeRejectsUnknownMapsOnceIndexIsLoaded()
        {
            using var storage = new TemporaryNativeStorage("kumori-star-filter-unknown-map-test");
            KumoriStarRatingIndex.ClearMemory();
            KumoriStarFilterState.Clear();

            try
            {
                var mod = new KumoriModBPMAdjust
                {
                    TargetBPM = { Value = 180 },
                    StarFilterMode = { Value = KumoriStarRatingFilterMode.PostMod },
                    StarFilterMinimum = { Value = "6" },
                    StarFilterMaximum = { Value = "7" },
                };
                var ruleset = new KumoriRuleset();
                var indexed = new BeatmapInfo { Hash = "indexed" };
                var unknown = new BeatmapInfo { Hash = "unknown", StarRating = 12 };
                string profileKey = KumoriStarRatingIndex.CreateProfileKey(ruleset.RulesetInfo, [mod]);

                KumoriStarRatingIndex.Store(storage, profileKey, new System.Collections.Generic.Dictionary<string, double>
                {
                    [KumoriStarRatingIndex.CreateBeatmapKey(indexed)] = 6.5,
                }, [], complete: false);

                var criteria = new FilterCriteria { Ruleset = ruleset.RulesetInfo, Mods = [mod] };
                var filter = new KumoriRulesetFilterCriteria();

                Assert.Multiple(() =>
                {
                    Assert.That(filter.Matches(indexed, criteria), Is.True);
                    Assert.That(filter.Matches(unknown, criteria), Is.False);
                });
            }
            finally
            {
                KumoriStarFilterState.Clear();
                KumoriStarRatingIndex.ClearMemory();
            }
        }

        [Test]
        public void TestActiveStarFilterStateOverridesStaleSelectedModCopy()
        {
            using var storage = new TemporaryNativeStorage("kumori-active-star-filter-test");
            KumoriStarRatingIndex.ClearMemory();
            KumoriStarFilterState.Clear();

            try
            {
                var staleSelectedMod = new KumoriModBPMAdjust { TargetBPM = { Value = 180 } };
                var ruleset = new KumoriRuleset();
                var matching = new BeatmapInfo { Hash = "matching" };
                var excluded = new BeatmapInfo { Hash = "excluded" };
                Mod[] mods = [staleSelectedMod];
                string profileKey = KumoriStarRatingIndex.CreateProfileKey(ruleset.RulesetInfo, mods);

                KumoriStarRatingIndex.Store(storage, profileKey, new System.Collections.Generic.Dictionary<string, double>
                {
                    [KumoriStarRatingIndex.CreateBeatmapKey(matching)] = 6.5,
                    [KumoriStarRatingIndex.CreateBeatmapKey(excluded)] = 5.5,
                }, []);
                KumoriStarFilterState.Publish(ruleset.RulesetInfo, mods, KumoriStarRatingFilterMode.PostMod, "6", "7");

                var staleCriteriaMod = new KumoriModBPMAdjust { TargetBPM = { Value = 200 } };
                var criteria = new FilterCriteria { Ruleset = ruleset.RulesetInfo, Mods = [staleCriteriaMod] };
                var filter = new KumoriRulesetFilterCriteria();

                Assert.Multiple(() =>
                {
                    Assert.That(staleSelectedMod.StarFilterMode.Value, Is.EqualTo(KumoriStarRatingFilterMode.Disabled));
                    Assert.That(KumoriStarRatingIndex.CreateProfileKey(ruleset.RulesetInfo, criteria.Mods!), Is.Not.EqualTo(profileKey));
                    Assert.That(filter.Matches(matching, criteria), Is.True);
                    Assert.That(filter.Matches(excluded, criteria), Is.False);
                });
            }
            finally
            {
                KumoriStarFilterState.Clear();
                KumoriStarRatingIndex.ClearMemory();
            }
        }

        [Test]
        public void TestStarFilterRefreshesActiveProfileForSameCriteriaInstance()
        {
            using var storage = new TemporaryNativeStorage("kumori-active-star-filter-refresh-test");
            KumoriStarRatingIndex.ClearMemory();
            KumoriStarFilterState.Clear();

            try
            {
                var ruleset = new KumoriRuleset();
                var beatmap = new BeatmapInfo { Hash = "profile-refresh" };
                var firstMod = new KumoriModBPMAdjust { TargetBPM = { Value = 180 } };
                var secondMod = new KumoriModBPMAdjust { TargetBPM = { Value = 240 } };
                string firstProfile = KumoriStarRatingIndex.CreateProfileKey(ruleset.RulesetInfo, [firstMod]);
                string secondProfile = KumoriStarRatingIndex.CreateProfileKey(ruleset.RulesetInfo, [secondMod]);

                KumoriStarRatingIndex.Store(storage, firstProfile, new System.Collections.Generic.Dictionary<string, double>
                {
                    [KumoriStarRatingIndex.CreateBeatmapKey(beatmap)] = 6.5,
                }, []);
                KumoriStarRatingIndex.Store(storage, secondProfile, new System.Collections.Generic.Dictionary<string, double>
                {
                    [KumoriStarRatingIndex.CreateBeatmapKey(beatmap)] = 5.5,
                }, []);

                var criteria = new FilterCriteria { Ruleset = ruleset.RulesetInfo, Mods = [firstMod] };
                var filter = new KumoriRulesetFilterCriteria();

                KumoriStarFilterState.Publish(ruleset.RulesetInfo, [firstMod], KumoriStarRatingFilterMode.PostMod, "6", "7");
                Assert.That(filter.Matches(beatmap, criteria), Is.True);

                // osu! reuses the FilterCriteria instance during a refilter. The active profile must
                // therefore invalidate the filter's cached evaluation independently of that instance.
                KumoriStarFilterState.Publish(ruleset.RulesetInfo, [secondMod], KumoriStarRatingFilterMode.PostMod, "6", "7");
                Assert.That(filter.Matches(beatmap, criteria), Is.False);
            }
            finally
            {
                KumoriStarFilterState.Clear();
                KumoriStarRatingIndex.ClearMemory();
            }
        }

        [Test]
        public void TestStarFilterBuildsProfileLookupOncePerCarouselPass()
        {
            var mod = new KumoriModBPMAdjust
            {
                StarFilterMode = { Value = KumoriStarRatingFilterMode.PostMod },
                StarFilterMinimum = { Value = "4" },
                StarFilterMaximum = { Value = "5" },
            };
            var criteria = new FilterCriteria
            {
                Ruleset = new KumoriRuleset().RulesetInfo,
                Mods = [mod],
            };
            var filter = new KumoriRulesetFilterCriteria();

            filter.Matches(new BeatmapInfo(), criteria);
            filter.Matches(new BeatmapInfo(), criteria);

            Assert.That(filter.CachedCriteriaBuildCount, Is.EqualTo(1));
        }

        [Test]
        public void TestStaleFilterControlCannotClearNewActiveProfile()
        {
            KumoriStarFilterState.Clear();
            var ruleset = new KumoriRuleset();
            var mod = new KumoriModBPMAdjust { TargetBPM = { Value = 180 } };
            string profileKey = KumoriStarRatingIndex.CreateProfileKey(ruleset.RulesetInfo, [mod]);

            try
            {
                KumoriStarFilterState.Publish(ruleset.RulesetInfo, [mod], KumoriStarRatingFilterMode.PostMod, "6", "7");

                Assert.Multiple(() =>
                {
                    Assert.That(KumoriStarFilterState.ClearIfCurrentProfileMatches("stale-profile"), Is.False);
                    Assert.That(KumoriStarFilterState.TryGet(out string activeProfile, out _, out _), Is.True);
                    Assert.That(activeProfile, Is.EqualTo(profileKey));
                    Assert.That(KumoriStarFilterState.ClearIfCurrentProfileMatches(profileKey), Is.True);
                    Assert.That(KumoriStarFilterState.TryGet(out _, out _, out _), Is.False);
                });
            }
            finally
            {
                KumoriStarFilterState.Clear();
            }
        }

        [Test]
        public void TestPostModWorkPlanReusesCompletedMapsAndResumesPartialProfiles()
        {
            var completeMap = new BeatmapInfo { Hash = "complete" };
            var unavailableMap = new BeatmapInfo { Hash = "unavailable" };
            var newMap = new BeatmapInfo { Hash = "new" };
            string completeKey = KumoriStarRatingIndex.CreateBeatmapKey(completeMap);
            string unavailableKey = KumoriStarRatingIndex.CreateBeatmapKey(unavailableMap);
            var partial = new KumoriStarRatingProfileSnapshot(
                new System.Collections.Generic.Dictionary<string, double> { [completeKey] = 4.5 },
                new System.Collections.Generic.HashSet<string> { unavailableKey },
                false);

            KumoriStarRatingWorkPlan resume = KumoriStarRatingCalculation.CreateWorkPlan([completeMap, unavailableMap, newMap], partial);
            KumoriStarRatingWorkPlan retry = KumoriStarRatingCalculation.CreateWorkPlan([completeMap, unavailableMap, newMap], partial with { Complete = true });

            Assert.Multiple(() =>
            {
                Assert.That(resume.Reused, Is.EqualTo(2));
                Assert.That(resume.Pending.Select(item => item.Beatmap), Is.EqualTo(new[] { newMap }));
                Assert.That(retry.Reused, Is.EqualTo(1));
                Assert.That(retry.Pending.Select(item => item.Beatmap), Is.EqualTo(new[] { unavailableMap, newMap }));
            });
        }

        [Test]
        public void TestPartialStarProfilePersistsForCancellationResume()
        {
            using var storage = new TemporaryNativeStorage("kumori-star-filter-test");
            const string profileKey = "partial-profile";
            var ratings = new System.Collections.Generic.Dictionary<string, double> { ["map"] = 5.25 };
            var unavailable = new System.Collections.Generic.HashSet<string> { "failed" };

            KumoriStarRatingIndex.Store(storage, profileKey, ratings, unavailable, false);
            KumoriStarRatingIndex.ClearMemory();

            Assert.That(KumoriStarRatingIndex.Load(storage, profileKey), Is.True);
            KumoriStarRatingProfileSnapshot snapshot = KumoriStarRatingIndex.GetSnapshot(profileKey);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.Complete, Is.False);
                Assert.That(snapshot.Ratings["map"], Is.EqualTo(5.25));
                Assert.That(snapshot.UnavailableBeatmaps, Does.Contain("failed"));
                Assert.That(KumoriStarRatingCalculation.WorkerCount(1), Is.EqualTo(1));
                Assert.That(KumoriStarRatingCalculation.WorkerCount(8), Is.EqualTo(7));
            });
        }

        [Test]
        public void TestSongSelectSynchroniserUpdatesSelectedModRate()
        {
            var controlMod = new KumoriModBPMAdjust { TargetBPM = { Value = 180 } };
            var selectedMod = (KumoriModBPMAdjust)controlMod.DeepClone();

            Assert.That(BPMSongSelectSynchroniser.Apply([controlMod, selectedMod], 120, 180), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(BPMSongSelectSynchroniser.ContainsBPMAdjust([selectedMod]), Is.True);
                Assert.That(BPMSongSelectSynchroniser.ContainsBPMAdjust([new OsuModHidden()]), Is.False);
                Assert.That(controlMod.SourceBPM, Is.EqualTo(120));
                Assert.That(selectedMod.SourceBPM, Is.EqualTo(120));
                Assert.That(controlMod.SpeedChange.Value, Is.EqualTo(1.5));
                Assert.That(selectedMod.SpeedChange.Value, Is.EqualTo(1.5));
                Assert.That(BPMSongSelectSynchroniser.Apply([controlMod, selectedMod], 120, 180), Is.False);
            });
        }

        [Test]
        public void TestComputedRateDoesNotChangePresetIdentity()
        {
            var savedPresetMod = new KumoriModBPMAdjust { TargetBPM = { Value = 180 } };
            savedPresetMod.SetSourceBPM(180);
            var liveMod = (KumoriModBPMAdjust)savedPresetMod.DeepClone();
            liveMod.SetSourceBPM(120);

            Assert.Multiple(() =>
            {
                Assert.That(liveMod.SpeedChange.Value, Is.EqualTo(1.5));
                Assert.That(liveMod, Is.EqualTo(savedPresetMod));
                Assert.That(liveMod.GetSettingsSourceProperties().Select(setting => setting.Item2.Name), Does.Not.Contain(nameof(KumoriModBPMAdjust.SpeedChange)));
            });
        }

        [Test]
        public void TestBPMRateCannotBeReplacedByOfficialSpeedHotkeys()
        {
            var mod = new KumoriModBPMAdjust { TargetBPM = { Value = 240 } };
            mod.SetSourceBPM(160);

            Assert.Multiple(() =>
            {
                Assert.That(mod, Is.InstanceOf<IApplicableToRate>());
                Assert.That(mod, Is.Not.InstanceOf<ModRateAdjust>());
                Assert.That(mod.SpeedChange.Value, Is.EqualTo(1.5));
                Assert.That(mod.ApplyToRate(0, 1), Is.EqualTo(1.5));
                Assert.That(ModUtils.CalculateRateWithMods([mod]), Is.EqualTo(1.5));
                Assert.That(ModUtils.CheckCompatibleSet([mod, new OsuModDoubleTime()]), Is.False);
            });
        }

        [Test]
        public void TestDifficultyCalculationRebindsRateForEachBeatmapPanel()
        {
            var mod = new KumoriModBPMAdjust { TargetBPM = { Value = 180 } };
            mod.SetSourceBPM(90); // Simulate another currently highlighted map.

            BPMPreparedWorkingBeatmap.PrepareMods(new BeatmapInfo { BPM = 120 }, [mod]);

            Assert.Multiple(() =>
            {
                Assert.That(mod.SourceBPM, Is.EqualTo(120));
                Assert.That(mod.SpeedChange.Value, Is.EqualTo(1.5));
            });
        }

        [Test]
        public void TestNormalSongSelectShowsCompactNativeAndOriginalMapStatistics()
        {
            var beatmap = new BeatmapInfo
            {
                StarRating = 4.45,
                BPM = 150,
            };
            var bpm = new KumoriModBPMAdjust { TargetBPM = { Value = 195 } };
            RulesetBeatmapAttribute[] attributes = new KumoriRuleset().GetBeatmapAttributesForDisplay(beatmap, [bpm]).ToArray();
            RulesetBeatmapAttribute originalStars = attributes.Single(attribute => attribute.Label.ToString() == "OG SR");
            RulesetBeatmapAttribute originalBPM = attributes.Single(attribute => attribute.Label.ToString() == "OG BPM");
            RulesetBeatmapAttribute playRate = attributes.Single(attribute => attribute.Label.ToString() == "Rate ×");

            Assert.Multiple(() =>
            {
                Assert.That(attributes, Has.Length.EqualTo(7));
                Assert.That(attributes.Select(attribute => attribute.Label.ToString()), Is.EqualTo(new[] { "CS", "AR", "OD", "HP", "OG SR", "OG BPM", "Rate ×" }));
                Assert.That(originalStars.AdjustedValue, Is.EqualTo(4.45).Within(0.001));
                Assert.That(originalBPM.AdjustedValue, Is.EqualTo(150));
                Assert.That(playRate.AdjustedValue, Is.EqualTo(1.3).Within(0.001));
            });
        }

        [Test]
        public void TestStarProfileOnlyInvalidatesForDifficultyAdjustSettingsThatAffectStars()
        {
            var ruleset = new KumoriRuleset();
            var bpm = new KumoriModBPMAdjust { TargetBPM = { Value = 240 } };
            var first = new OsuModDifficultyAdjust
            {
                CircleSize = { Value = 4 },
                ApproachRate = { Value = 10 },
                OverallDifficulty = { Value = 9 },
                DrainRate = { Value = 0 },
            };
            var second = new OsuModDifficultyAdjust
            {
                CircleSize = { Value = 4 },
                ApproachRate = { Value = 10 },
                OverallDifficulty = { Value = 3 },
                DrainRate = { Value = 8 },
                ExtendedLimits = { Value = true },
            };

            string firstKey = KumoriStarRatingIndex.CreateProfileKey(ruleset.RulesetInfo, [bpm, first]);
            string secondKey = KumoriStarRatingIndex.CreateProfileKey(ruleset.RulesetInfo, [bpm, second]);

            Assert.That(secondKey, Is.EqualTo(firstKey), "HP, OD and Extended Limits do not change raw osu!standard stars");

            second.ApproachRate.Value = 9.9f;
            Assert.That(KumoriStarRatingIndex.CreateProfileKey(ruleset.RulesetInfo, [bpm, second]), Is.Not.EqualTo(firstKey));

            second.ApproachRate.Value = 10;
            second.CircleSize.Value = 4.1f;
            Assert.That(KumoriStarRatingIndex.CreateProfileKey(ruleset.RulesetInfo, [bpm, second]), Is.Not.EqualTo(firstKey));
        }

        [Test]
        public void TestHiddenDoesNotCreateASeparateStarProfile()
        {
            var ruleset = new KumoriRuleset();
            var bpm = new KumoriModBPMAdjust { TargetBPM = { Value = 240 } };
            var difficultyAdjust = new OsuModDifficultyAdjust
            {
                CircleSize = { Value = 4 },
                ApproachRate = { Value = 10 },
                DrainRate = { Value = 0 },
            };

            string noHidden = KumoriStarRatingIndex.CreateProfileKey(ruleset.RulesetInfo, [bpm, difficultyAdjust]);
            string hidden = KumoriStarRatingIndex.CreateProfileKey(ruleset.RulesetInfo, [bpm, new OsuModHidden(), difficultyAdjust]);

            Assert.That(hidden, Is.EqualTo(noHidden));
        }

        [Test]
        public void TestCompletedLegacyDifficultyAdjustProfileMigratesWithoutRecalculation()
        {
            using var storage = new TemporaryNativeStorage("kumori-star-profile-migration-test");
            var ruleset = new KumoriRuleset();
            var bpm = new KumoriModBPMAdjust { TargetBPM = { Value = 240 } };
            var difficultyAdjust = new OsuModDifficultyAdjust
            {
                CircleSize = { Value = 4 },
                ApproachRate = { Value = 10 },
                OverallDifficulty = { Value = 9 },
                DrainRate = { Value = 0 },
            };
            Mod[] mods = [bpm, difficultyAdjust];
            string stableKey = KumoriStarRatingIndex.CreateProfileKey(ruleset.RulesetInfo, mods);
            string legacyKey = KumoriStarRatingIndex.CreateLegacyProfileKey(ruleset.RulesetInfo, mods);
            var ratings = new System.Collections.Generic.Dictionary<string, double> { ["map"] = 6.25 };

            Assert.That(legacyKey, Is.Not.EqualTo(stableKey));
            KumoriStarRatingIndex.Store(storage, legacyKey, ratings, [], true);
            KumoriStarRatingIndex.ClearMemory();

            Assert.That(KumoriStarRatingIndex.Load(storage, ruleset.RulesetInfo, mods), Is.True);
            Assert.That(KumoriStarRatingIndex.GetSnapshot(stableKey).Ratings["map"], Is.EqualTo(6.25));

            KumoriStarRatingIndex.ClearMemory();
            Assert.That(KumoriStarRatingIndex.Load(storage, stableKey), Is.True, "the migrated stable profile must persist to disk");
        }

        [Test]
        public void TestRuntimeStarIndexUsesPortableHashAndInvalidatesChangedHash()
        {
            using var storage = new TemporaryNativeStorage("kumori-star-runtime-index-test");
            const string profileKey = "runtime-index";
            var beatmap = new BeatmapInfo { ID = System.Guid.NewGuid(), Hash = "original" };
            string key = KumoriStarRatingIndex.CreateBeatmapKey(beatmap);

            KumoriStarRatingIndex.Store(storage, profileKey, new System.Collections.Generic.Dictionary<string, double> { [key] = 6.25 }, []);

            Assert.That(KumoriStarRatingIndex.TryGet(profileKey, beatmap, out double rating, out _), Is.True);
            Assert.That(rating, Is.EqualTo(6.25));

            beatmap.Hash = "changed";
            Assert.That(KumoriStarRatingIndex.TryGet(profileKey, beatmap, out _, out _), Is.False);
        }

        [Test]
        public void TestStarRatingDatabaseUsesPortableBeatmapHashAcrossInstallations()
        {
            using var storage = new TemporaryNativeStorage("kumori-portable-star-index-test");
            const string profileKey = "portable-profile";
            var sourceBeatmap = new BeatmapInfo { ID = System.Guid.NewGuid(), Hash = "shared-hash" };
            var importedBeatmap = new BeatmapInfo { ID = System.Guid.NewGuid(), Hash = "shared-hash" };

            KumoriStarRatingIndex.Store(storage, profileKey, new System.Collections.Generic.Dictionary<string, double>
            {
                [KumoriStarRatingIndex.CreateBeatmapKey(sourceBeatmap)] = 7.25,
            }, []);
            KumoriStarRatingIndex.ClearMemory();

            Assert.Multiple(() =>
            {
                Assert.That(storage.Exists(KumoriStarRatingIndex.DatabaseFilename), Is.True);
                Assert.That(KumoriStarRatingIndex.Load(storage, profileKey), Is.True);
                Assert.That(KumoriStarRatingIndex.TryGet(profileKey, importedBeatmap, out double rating, out _), Is.True);
                Assert.That(rating, Is.EqualTo(7.25));
            });
        }

        [Test]
        public void TestBulkStarPlanCovers220To270Once()
        {
            var ruleset = new KumoriRuleset();
            System.Collections.Generic.IReadOnlyList<KumoriBulkStarRatingProfile> profiles = KumoriBulkStarRatingPlan.Create(ruleset.RulesetInfo);
            System.Collections.Generic.IReadOnlyList<string> obsoleteProfiles = KumoriBulkStarRatingPlan.CreateObsoleteBulkProfileKeys(ruleset.RulesetInfo);

            Assert.Multiple(() =>
            {
                Assert.That(profiles, Has.Count.EqualTo(51));
                Assert.That(profiles.Select(profile => profile.ProfileKey), Is.Unique);
                Assert.That(obsoleteProfiles, Has.Count.EqualTo(102));
                Assert.That(obsoleteProfiles, Is.Unique);
                Assert.That(obsoleteProfiles, Has.None.Matches<string>(key => profiles.Any(profile => profile.ProfileKey == key)));
                Assert.That(profiles.Min(profile => profile.TargetBPM), Is.EqualTo(220));
                Assert.That(profiles.Max(profile => profile.TargetBPM), Is.EqualTo(270));
            });

            foreach (KumoriBulkStarRatingProfile profile in profiles)
            {
                var bpm = profile.TemplateMods.OfType<KumoriModBPMAdjust>().Single();
                var difficultyAdjust = profile.TemplateMods.OfType<OsuModDifficultyAdjust>().Single();

                Assert.Multiple(() =>
                {
                    Assert.That(bpm.TargetBPM.Value, Is.EqualTo(profile.TargetBPM));
                    Assert.That(bpm.ScaleMapStatsWithBPM.Value, Is.False);
                    Assert.That(difficultyAdjust.ApproachRate.Value, Is.EqualTo(10));
                    Assert.That(difficultyAdjust.DrainRate.Value, Is.EqualTo(0));
                    Assert.That(profile.TemplateMods.OfType<OsuModHidden>(), Is.Empty);
                });
            }
        }

        [Test]
        public void TestObsoleteBulkProfilesAreDeletedAndCompacted()
        {
            using var storage = new TemporaryNativeStorage("kumori-bulk-profile-pruning-test");
            var ruleset = new KumoriRuleset();
            string obsoleteProfile = KumoriBulkStarRatingPlan.CreateObsoleteBulkProfileKeys(ruleset.RulesetInfo).First();

            KumoriStarRatingIndex.Store(storage, obsoleteProfile, new System.Collections.Generic.Dictionary<string, double>
            {
                ["map"] = 6.25,
            }, [], true);
            KumoriStarRatingIndex.ClearMemory();

            Assert.That(KumoriStarRatingIndex.Load(storage, obsoleteProfile), Is.True);
            Assert.That(KumoriStarRatingIndex.DeleteProfiles(storage, [obsoleteProfile], compact: true), Is.True);
            Assert.That(KumoriStarRatingIndex.Load(storage, obsoleteProfile), Is.False);
            Assert.That(KumoriStarRatingIndex.DeleteProfiles(storage, [obsoleteProfile], compact: true), Is.False);
        }

        [Test]
        public void TestBulkResumeOnlySkipsMapsStoredForEveryProfile()
        {
            using var storage = new TemporaryNativeStorage("kumori-bulk-resume-test");
            string[] profileKeys = ["first", "second"];
            KumoriStarRatingIndex.EnsureProfiles(storage, profileKeys);
            KumoriStarRatingIndex.StoreBatch(storage,
            [
                new KumoriStarRatingDatabaseWrite("first", "complete-map", 5.1, false),
                new KumoriStarRatingDatabaseWrite("second", "complete-map", 5.2, false),
                new KumoriStarRatingDatabaseWrite("first", "partial-map", 6.1, false),
            ], updateMemory: false);

            System.Collections.Generic.HashSet<string> completed = KumoriStarRatingIndex.GetFullyIndexedBeatmapKeys(storage, profileKeys);

            Assert.Multiple(() =>
            {
                Assert.That(completed, Does.Contain("complete-map"));
                Assert.That(completed, Does.Not.Contain("partial-map"));
            });
        }

        [Test]
        public void TestStarOnlyBatchCalculatorMatchesEveryBulkProfile()
        {
            var beatmap = new Beatmap<OsuHitObject>
            {
                BeatmapInfo = new BeatmapInfo { BPM = 120 },
                Difficulty = new BeatmapDifficulty
                {
                    ApproachRate = 9,
                    CircleSize = 4,
                    OverallDifficulty = 8,
                },
            };
            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });

            for (int i = 0; i < 100; i++)
            {
                beatmap.HitObjects.Add(new HitCircle
                {
                    StartTime = 1000 + i * 250,
                    Position = i % 2 == 0 ? new Vector2(64, 64) : new Vector2(448, 320),
                });
            }

            var ruleset = new KumoriRuleset();
            DifficultyCalculator calculator = ruleset.CreateDifficultyCalculator(new TestWorkingBeatmap(beatmap));
            KumoriStarOnlyBatchCalculator batchCalculator = ruleset.CreateStarOnlyBatchCalculator(new TestWorkingBeatmap(beatmap));
            IReadOnlyList<KumoriBulkStarRatingProfile> profiles = KumoriBulkStarRatingPlan.Create(ruleset.RulesetInfo);

            foreach (KumoriBulkStarRatingProfile profile in profiles)
            {
                Mod[] mods = profile.CreateWorkerMods();

                foreach (KumoriModBPMAdjust bpm in mods.OfType<KumoriModBPMAdjust>())
                    bpm.SetSourceBPM(120);

                double expected = calculator.Calculate(mods).StarRating;
                double actual = batchCalculator.CalculateStarRating(mods);

                Assert.That(actual, Is.EqualTo(expected).Within(1e-12), $"At {profile.TargetBPM} BPM");
            }
        }

        [Test]
        public void TestHiddenDoesNotAffectLiveKumoriStarRating()
        {
            var beatmap = new Beatmap<OsuHitObject>
            {
                BeatmapInfo = new BeatmapInfo { BPM = 120 },
                Difficulty = new BeatmapDifficulty
                {
                    ApproachRate = 9,
                    CircleSize = 4,
                    OverallDifficulty = 8,
                },
            };
            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });

            for (int i = 0; i < 100; i++)
            {
                beatmap.HitObjects.Add(new HitCircle
                {
                    StartTime = 1000 + i * 250,
                    Position = i % 2 == 0 ? new Vector2(64, 64) : new Vector2(448, 320),
                });
            }

            var ruleset = new KumoriRuleset();
            var hidden = new OsuModHidden();
            DifficultyCalculator kumori = ruleset.CreateDifficultyCalculator(new TestWorkingBeatmap(beatmap));
            var official = new OsuDifficultyCalculator(ruleset.RulesetInfo, new TestWorkingBeatmap(beatmap));

            double noHiddenRating = kumori.Calculate().StarRating;
            DifficultyAttributes hiddenAttributes = kumori.Calculate([hidden]);
            double flashlightRating = kumori.Calculate([new OsuModFlashlight()]).StarRating;
            double hiddenFlashlightRating = kumori.Calculate([hidden, new OsuModFlashlight()]).StarRating;
            double officialNoHiddenRating = official.Calculate().StarRating;
            double officialHiddenRating = official.Calculate([hidden]).StarRating;

            Assert.Multiple(() =>
            {
                Assert.That(hiddenAttributes.StarRating, Is.EqualTo(noHiddenRating).Within(1e-12));
                Assert.That(hiddenFlashlightRating, Is.EqualTo(flashlightRating).Within(1e-12), "Hidden must also remain neutral when combined with Flashlight");
                Assert.That(hiddenAttributes.Mods.OfType<OsuModHidden>(), Is.Not.Empty, "Hidden must remain in the difficulty attributes");
                Assert.That(officialHiddenRating, Is.Not.EqualTo(officialNoHiddenRating).Within(1e-12), "The fixture must exercise Hidden's official Reading bonus");
                Assert.That(official.Version, Is.EqualTo(KumoriDifficultyCalculator.OfficialDifficultyVersion),
                    "Update Kumori's cache fingerprint when osu!'s difficulty algorithm changes");
            });
        }

        [Test]
        public void TestLegacyJsonStarProfileMigratesToSqliteAndPortableHashes()
        {
            using var storage = new TemporaryNativeStorage("kumori-json-star-migration-test");
            const string profileKey = "json-profile";
            var beatmap = new BeatmapInfo { ID = System.Guid.NewGuid(), Hash = "portable-json-hash" };
            string legacyKey = $"{beatmap.ID:N}:{beatmap.Hash}";
            string duplicateLegacyKey = $"{System.Guid.NewGuid():N}:{beatmap.Hash}";
            string json = JsonConvert.SerializeObject(new
            {
                version = 2,
                ratings = new System.Collections.Generic.Dictionary<string, double>
                {
                    [legacyKey] = 6.75,
                    [duplicateLegacyKey] = 6.75,
                },
                unavailable_beatmaps = new[] { legacyKey, duplicateLegacyKey },
                complete = true,
            });

            using (Stream stream = storage.GetStream($"kumori-star-ratings/{profileKey}.json", FileAccess.Write, FileMode.Create))
            using (var writer = new StreamWriter(stream))
                writer.Write(json);

            KumoriStarRatingIndex.ClearMemory();

            Assert.Multiple(() =>
            {
                Assert.That(KumoriStarRatingIndex.Load(storage, profileKey), Is.True);
                Assert.That(storage.Exists(KumoriStarRatingIndex.DatabaseFilename), Is.True);
                Assert.That(KumoriStarRatingIndex.TryGet(profileKey, new BeatmapInfo { ID = System.Guid.NewGuid(), Hash = beatmap.Hash }, out double rating, out _), Is.True);
                Assert.That(rating, Is.EqualTo(6.75));
            });
        }

        [Test]
        public void TestTargetBPMPersistsAcrossFreshModSelections()
        {
            using var storage = new TemporaryNativeStorage("kumori-target-bpm-test");

            Assert.That(BPMTargetStore.Load(storage), Is.Null);
            BPMTargetStore.Save(storage, 239.999);
            Assert.That(BPMTargetStore.Load(storage), Is.EqualTo(240));

            BPMTargetStore.Save(storage, null);
            Assert.That(BPMTargetStore.Load(storage), Is.Null);
        }

        [Test]
        public void TestOfficialDifficultyCacheReaderSafelyHandlesCacheMiss()
        {
            var reader = new KumoriDifficultyCacheReader(new BeatmapDifficultyCache());

            Assert.That(reader.TryGet(new BeatmapInfo(), new KumoriRuleset().RulesetInfo, [], out _), Is.False);
        }

        [TestCase(120, 180, 1.5)]
        [TestCase(200, 150, 0.75)]
        public void TestTosuCompatibilityPreservesBPMIdentityAndRate(double sourceBPM, double targetBPM, double rate)
        {
            var mod = new KumoriModBPMAdjust { TargetBPM = { Value = targetBPM } };
            mod.SetSourceBPM(sourceBPM);

            string json = TosuCompatibilityIdentity.CreateTosuModsJson([mod]);
            APIMod apiMod = JsonConvert.DeserializeObject<APIMod[]>(json)!.Single();

            Assert.Multiple(() =>
            {
                Assert.That(apiMod.Acronym, Is.EqualTo("BPM"));
                Assert.That(apiMod.Settings["target_bpm"], Is.EqualTo(targetBPM));
                Assert.That(apiMod.Settings["speed_change"], Is.EqualTo(rate).Within(1e-12));
                Assert.That(mod.Acronym, Is.EqualTo("BPM"), "The real score/mod identity must remain unchanged");
            });
        }

        [Test]
        public void TestTosuCompatibilityDoesNotMutateRulesetIdentity()
        {
            var ruleset = new KumoriRuleset();
            Assert.That(ruleset.RulesetInfo.OnlineID, Is.EqualTo(-1));
        }

        [Test]
        public void TestDrawableUsesOfficialOsuIdentityForHudAndSkinLayouts()
        {
            var beatmap = new Beatmap<OsuHitObject>();
            beatmap.HitObjects.Add(new HitCircle { StartTime = 1000 });

            var ruleset = new KumoriRuleset();
            using var drawable = ruleset.CreateDrawableRulesetWith(beatmap);

            Assert.Multiple(() =>
            {
                Assert.That(drawable.Ruleset, Is.TypeOf<OsuRuleset>(), "the delegated renderer should use osu!standard's HUD and skin layout key");
                Assert.That(drawable.Ruleset.RulesetInfo.ShortName, Is.EqualTo("osu"));
                Assert.That(ruleset.RulesetInfo.ShortName, Is.EqualTo("kumori"), "Kumori must remain the selected and scoring ruleset");
                Assert.That(ruleset.RulesetInfo.OnlineID, Is.EqualTo(-1));
            });
        }

        [Test]
        public void TestTosuCompatibilityTemporarilyPresentsActiveScoreAsOsuStandard()
        {
            var beatmap = new Beatmap<OsuHitObject>();
            beatmap.HitObjects.Add(new HitCircle { StartTime = 1000 });

            var ruleset = new KumoriRuleset();
            var bpm = new KumoriModBPMAdjust { TargetBPM = { Value = 180 } };
            bpm.SetSourceBPM(120);
            var state = new GameplayState(beatmap, ruleset, [bpm]);
            var originalRuleset = state.Score.ScoreInfo.Ruleset;
            string originalModsJson = state.Score.ScoreInfo.ModsJson;
            var compatibility = new TosuCompatibilityIdentity();

            compatibility.Attach(state);

            Assert.Multiple(() =>
            {
                Assert.That(state.Score.ScoreInfo.Ruleset, Is.Not.SameAs(originalRuleset));
                Assert.That(state.Score.ScoreInfo.Ruleset.OnlineID, Is.EqualTo(0));
                Assert.That(state.Score.ScoreInfo.Ruleset.ShortName, Is.EqualTo("kumori"));
                Assert.That(ruleset.RulesetInfo.OnlineID, Is.EqualTo(-1), "the selected custom ruleset identity must remain untouched");
                Assert.That(state.Score.ScoreInfo.ModsJson, Does.Contain("\"BPM\""));
                Assert.That(state.Score.ScoreInfo.ModsJson, Does.Contain("\"target_bpm\":180.0"));
                Assert.That(state.Score.ScoreInfo.ModsJson, Does.Contain("\"speed_change\":1.5"));
            });

            compatibility.Restore();

            Assert.Multiple(() =>
            {
                Assert.That(state.Score.ScoreInfo.Ruleset, Is.SameAs(originalRuleset));
                Assert.That(state.Score.ScoreInfo.ModsJson, Is.EqualTo(originalModsJson));
            });
        }

        [Test]
        public void TestSpectatorTransportRestoresKumoriLocally()
        {
            var bpm = new KumoriModBPMAdjust { TargetBPM = { Value = 180 } };
            APIMod apiMod = new APIMod(bpm);
            var state = new osu.Game.Online.Spectator.SpectatorState
            {
                RulesetID = 0,
                Mods = [apiMod],
            };

            Assert.Multiple(() =>
            {
                Assert.That(apiMod.Settings[KumoriModBPMAdjust.SPECTATOR_MARKER_SETTING], Is.EqualTo(true));
                Assert.That(KumoriRulesetIcon.TryRestoreKumoriSpectatorState(state), Is.True);
                Assert.That(state.RulesetID, Is.EqualTo(-1));
            });
        }

        [Test]
        public void TestSpectatorTransportDoesNotClaimOrdinaryOsuBPMMod()
        {
            var state = new osu.Game.Online.Spectator.SpectatorState
            {
                RulesetID = 0,
                Mods = [new APIMod { Acronym = "BPM", Settings = { ["target_bpm"] = 180d } }],
            };

            Assert.Multiple(() =>
            {
                Assert.That(KumoriRulesetIcon.TryRestoreKumoriSpectatorState(state), Is.False);
                Assert.That(state.RulesetID, Is.EqualTo(0));
            });
        }

        [Test]
        public void TestClearedTargetRemainsNeutralWhenBeatmapChanges()
        {
            var beatmap = new Beatmap<OsuHitObject>();
            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });

            var mod = new KumoriModBPMAdjust();
            mod.SetSourceBPM(180);
            mod.TargetBPM.Value = null;
            ((IApplicableAfterBeatmapConversion)mod).ApplyToBeatmap(beatmap);

            Assert.Multiple(() =>
            {
                Assert.That(mod.TargetBPM.Value, Is.Null);
                Assert.That(mod.SpeedChange.Value, Is.EqualTo(1));
            });
        }

        [Test]
        public void TestModSettingsControlsCanBeCreatedByOfficialUi()
        {
            var mod = new KumoriModBPMAdjust();
            var controls = mod.CreateSettingsControls().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(controls.Count(control => control.IsPresent), Is.EqualTo(7));
                Assert.That(controls[0], Is.TypeOf<SettingsBPMStarFilterControl>());
                Assert.That(controls, Has.Some.TypeOf<SettingsBPMControl>());
                Assert.That(controls, Has.Some.TypeOf<SettingsBPMAudioMode>());
                Assert.That(controls, Has.Some.TypeOf<SettingsBPMStarFilterControl>());
            });
        }

        [Test]
        public void TestRulesetBrandingResourcesAreAvailable()
        {
            var ruleset = new KumoriRuleset();
            using var resources = ruleset.CreateResourceStore();
            byte[]? selectionSample = resources.Get("Samples/UI/ruleset-select-kumori.wav");

            Assert.Multiple(() =>
            {
                Assert.That(ruleset.CreateIcon(), Is.TypeOf<KumoriRulesetIcon>());
                Assert.That(selectionSample, Is.Not.Null.And.Length.GreaterThan(1000));
            });
        }

        [Test]
        public void TestAutoUpdaterRecognisesOnlyInstalledRulesetPath()
        {
            Assert.Multiple(() =>
            {
                Assert.That(KumoriAutoUpdater.IsInstalledRulesetPath(@"D:\Games\osu!lazer\rulesets\osu.Game.Rulesets.Kumori.dll"), Is.True);
                Assert.That(KumoriAutoUpdater.IsInstalledRulesetPath(@"D:\build\bin\osu.Game.Rulesets.Kumori.dll"), Is.False);
                Assert.That(KumoriAutoUpdater.IsInstalledRulesetPath(@"D:\Games\osu!lazer\rulesets\osu.Game.Rulesets.Kumori.dll.pending"), Is.False);
            });
        }

        [Test]
        public void TestAutoUpdaterParsesReleaseVersionsAndChecksums()
        {
            Assert.Multiple(() =>
            {
                Assert.That(KumoriAutoUpdater.TryParseReleaseVersion("ruleset-v1.2.3", out System.Version version), Is.True);
                Assert.That(version, Is.EqualTo(new System.Version(1, 2, 3)));
                Assert.That(KumoriAutoUpdater.TryParseReleaseVersion("ruleset-v2.0.1+build.5", out _), Is.True);
                Assert.That(KumoriAutoUpdater.TryParseReleaseVersion("v1.2.3", out _), Is.False);
                Assert.That(KumoriAutoUpdater.TryReadChecksum(new string('a', 64) + " *osu.Game.Rulesets.Kumori.dll", out string checksum), Is.True);
                Assert.That(checksum, Is.EqualTo(new string('a', 64)));
                Assert.That(KumoriAutoUpdater.TryReadChecksum("not-a-checksum", out _), Is.False);
            });
        }

        [Test]
        public void TestAutoUpdaterAcceptsOnlyNewVerifiedGitHubReleaseShape()
        {
            const string release = """
                {
                  "tag_name": "ruleset-v1.2.4",
                  "draft": false,
                  "prerelease": false,
                  "assets": [
                    {
                      "name": "osu.Game.Rulesets.Kumori.dll",
                      "browser_download_url": "https://github.com/Lorenso0/Kumori-BPM/releases/download/ruleset-v1.2.4/osu.Game.Rulesets.Kumori.dll",
                      "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                    },
                    {
                      "name": "osu.Game.Rulesets.Kumori.dll.sha256",
                      "browser_download_url": "https://github.com/Lorenso0/Kumori-BPM/releases/download/ruleset-v1.2.4/osu.Game.Rulesets.Kumori.dll.sha256"
                    }
                  ]
                }
                """;

            KumoriUpdateCandidate? candidate = KumoriAutoUpdater.ParseCandidate(release, new System.Version(1, 2, 3));

            Assert.Multiple(() =>
            {
                Assert.That(candidate, Is.Not.Null);
                Assert.That(candidate!.Version, Is.EqualTo(new System.Version(1, 2, 4)));
                Assert.That(candidate.RulesetUri.Host, Is.EqualTo("github.com"));
                Assert.That(candidate.GitHubDigest, Is.EqualTo(new string('a', 64)));
                Assert.That(KumoriAutoUpdater.ParseCandidate(release, new System.Version(1, 2, 4)), Is.Null);
                Assert.That(KumoriAutoUpdater.ParseCandidate(release.Replace("github.com", "example.com"), new System.Version(1, 2, 3)), Is.Null);
            });
        }

        [Test]
        public void TestUpdateNotificationsQueueUntilUiAttaches()
        {
            KumoriUpdateNotificationBus.ResetForTesting();
            var firstMessages = new System.Collections.Generic.List<string>();
            var secondMessages = new System.Collections.Generic.List<string>();

            try
            {
                KumoriUpdateNotificationBus.Post("ready");

                using (KumoriUpdateNotificationBus.Attach(firstMessages.Add))
                {
                    KumoriUpdateNotificationBus.Post("installed");
                    Assert.That(firstMessages, Is.EqualTo(new[] { "ready", "installed" }));
                }

                KumoriUpdateNotificationBus.Post("queued again");

                using (KumoriUpdateNotificationBus.Attach(secondMessages.Add))
                    Assert.That(secondMessages, Is.EqualTo(new[] { "queued again" }));
            }
            finally
            {
                KumoriUpdateNotificationBus.ResetForTesting();
            }
        }

        [Test]
        public void TestAutoUpdaterHelperWaitsThenReplacesWithBackup()
        {
            if (!System.OperatingSystem.IsWindows())
                Assert.Ignore("The installed updater helper is Windows-specific.");

            string directory = Path.Combine(Path.GetTempPath(), $"kumori-updater-test-{System.Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            try
            {
                string target = Path.Combine(directory, "osu.Game.Rulesets.Kumori.dll");
                string staged = Path.Combine(directory, ".kumori-update-test.dll.pending");
                string script = Path.Combine(directory, "updater.ps1");
                byte[] original = System.Text.Encoding.UTF8.GetBytes("original ruleset");
                byte[] updated = System.Text.Encoding.UTF8.GetBytes("updated ruleset");
                string expectedHash = System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(updated));
                File.WriteAllBytes(target, original);
                File.WriteAllBytes(staged, updated);
                File.WriteAllText(script, KumoriAutoUpdater.ReplacementScriptForTesting);

                string powershell = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
                using var waitedProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = powershell,
                    Arguments = "-NoProfile -Command \"Start-Sleep -Seconds 1\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                })!;

                var helperInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = powershell,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                helperInfo.ArgumentList.Add("-NoProfile");
                helperInfo.ArgumentList.Add("-ExecutionPolicy");
                helperInfo.ArgumentList.Add("Bypass");
                helperInfo.ArgumentList.Add("-File");
                helperInfo.ArgumentList.Add(script);
                helperInfo.ArgumentList.Add("-ProcessId");
                helperInfo.ArgumentList.Add(waitedProcess.Id.ToString());
                helperInfo.ArgumentList.Add("-StagedDll");
                helperInfo.ArgumentList.Add(staged);
                helperInfo.ArgumentList.Add("-TargetDll");
                helperInfo.ArgumentList.Add(target);
                helperInfo.ArgumentList.Add("-ExpectedHash");
                helperInfo.ArgumentList.Add(expectedHash);
                helperInfo.ArgumentList.Add("-Version");
                helperInfo.ArgumentList.Add("1.2.4");

                using var helper = System.Diagnostics.Process.Start(helperInfo)!;
                Assert.That(helper.WaitForExit(10000), Is.True, "the updater helper did not finish after the waited process exited");
                string logPath = Path.Combine(directory, "kumori-updater.log");
                string markerPath = Path.Combine(directory, ".kumori-update-installed");

                Assert.Multiple(() =>
                {
                    Assert.That(File.ReadAllBytes(target), Is.EqualTo(updated));
                    Assert.That(File.ReadAllBytes(target + ".bak"), Is.EqualTo(original));
                    Assert.That(File.Exists(staged), Is.False);
                    Assert.That(File.ReadAllText(logPath), Does.Contain("Installed Kumori 1.2.4"));
                    Assert.That(File.ReadAllText(markerPath), Is.EqualTo("1.2.4"));
                });

                Assert.That(KumoriAutoUpdater.ConsumeInstalledVersion(directory), Is.EqualTo("1.2.4"));
                Assert.That(File.Exists(markerPath), Is.False, "the success toast marker must only be consumed once");
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }
    }
}

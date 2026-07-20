// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using osu.Framework.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Configuration;
using osu.Game.Online.API;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Catch.Mods;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Taiko;
using osu.Game.Rulesets.Taiko.Mods;
using osu.Game.Utils;
using osu.Game.Scoring;

namespace osu.Game.Tests.Mods
{
    [TestFixture]
    public class ModBPMAdjustTest
    {
        [TestCase(120, 180, 1.5)]
        [TestCase(200, 150, 0.75)]
        [TestCase(128, 174.5, 1.36328125)]
        public void TestRateCalculation(double sourceBPM, double targetBPM, double expectedRate)
        {
            var mod = new OsuModBPMAdjust();
            mod.ApplyToBeatmapInfo(new BeatmapInfo { BPM = sourceBPM });
            mod.TargetBPM.Value = targetBPM;

            Assert.Multiple(() =>
            {
                Assert.That(mod.SourceBPM, Is.EqualTo(sourceBPM));
                Assert.That(mod.SpeedChange.Value, Is.EqualTo(expectedRate).Within(1e-12));
                Assert.That(mod.ApplyToRate(0, 1), Is.EqualTo(expectedRate).Within(1e-12));
            });
        }

        [Test]
        public void TestInitialTargetAndMapSwitchPersistence()
        {
            var mod = new OsuModBPMAdjust();

            mod.ApplyToBeatmapInfo(new BeatmapInfo { BPM = 120 });

            Assert.Multiple(() =>
            {
                Assert.That(mod.TargetBPM.Value, Is.EqualTo(120));
                Assert.That(mod.SpeedChange.Value, Is.EqualTo(1));
            });

            mod.TargetBPM.Value = 180;
            mod.ApplyToBeatmapInfo(new BeatmapInfo { BPM = 200 });

            Assert.Multiple(() =>
            {
                Assert.That(mod.TargetBPM.Value, Is.EqualTo(180));
                Assert.That(mod.SourceBPM, Is.EqualTo(200));
                Assert.That(mod.SpeedChange.Value, Is.EqualTo(0.9).Within(1e-12));
            });

            mod.TargetBPM.Value = null;
            mod.ApplyToBeatmapInfo(new BeatmapInfo { BPM = 150 });

            Assert.Multiple(() =>
            {
                Assert.That(mod.TargetBPM.Value, Is.Null);
                Assert.That(mod.SpeedChange.Value, Is.EqualTo(1));
            });
        }

        [Test]
        public void TestMaterialisedVariableBPMBeatmapUsesMostCommonBPM()
        {
            var beatmap = new Beatmap<OsuHitObject>();
            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            beatmap.ControlPointInfo.Add(15000, new TimingControlPoint { BeatLength = 400 });
            beatmap.HitObjects.Add(new HitCircle { StartTime = 1000 });
            beatmap.HitObjects.Add(new HitCircle { StartTime = 20000 });

            var mod = new OsuModBPMAdjust
            {
                TargetBPM = { Value = 180 }
            };

            mod.ApplyToBeatmapBeforeConversion(beatmap);

            Assert.Multiple(() =>
            {
                Assert.That(mod.SourceBPM, Is.EqualTo(120));
                Assert.That(mod.SpeedChange.Value, Is.EqualTo(1.5));
            });
        }

        [Test]
        public void TestMetadataAndMaterialisedSourceUseSameResolver()
        {
            var beatmap = new Beatmap<OsuHitObject>();
            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            beatmap.ControlPointInfo.Add(10000, new TimingControlPoint { BeatLength = 400 });
            beatmap.HitObjects.Add(new HitCircle { StartTime = 1000 });
            beatmap.HitObjects.Add(new HitCircle { StartTime = 20000 });
            beatmap.BeatmapInfo.UpdateStatisticsFromBeatmap(beatmap);

            Assert.That(BPMResolver.FromBeatmapInfo(beatmap.BeatmapInfo), Is.EqualTo(BPMResolver.FromBeatmap(beatmap)));
        }

        [Test]
        public void TestAudioModes()
        {
            var mod = new OsuModBPMAdjust();
            mod.ApplyToBeatmapInfo(new BeatmapInfo { BPM = 120 });
            mod.TargetBPM.Value = 240;

            var adjustments = new AudioAdjustments();
            mod.ApplyToTrack(adjustments);

            assertMode(BPMAdjustAudioMode.PreservePitch, 1, 2);
            assertMode(BPMAdjustAudioMode.AdjustPitch, 2, 1);
            assertMode(BPMAdjustAudioMode.Nightcore, 1.5, 4.0 / 3);
            assertMode(BPMAdjustAudioMode.Daycore, 0.75, 8.0 / 3);
            assertMode(BPMAdjustAudioMode.Balanced, Math.Sqrt(2), Math.Sqrt(2));

            mod.CustomPitchSemitones.Value = 7;
            double customFrequency = Math.Pow(2, 7.0 / 12);
            assertMode(BPMAdjustAudioMode.CustomPitch, customFrequency, 2 / customFrequency);

            assertMode(BPMAdjustAudioMode.Chipmunk, 2, 1);
            assertMode(BPMAdjustAudioMode.Deep, 0.5, 4);
            assertMode(BPMAdjustAudioMode.NightcorePitchOnly, 1.5, 4.0 / 3);
            assertMode(BPMAdjustAudioMode.PreservePitchWithAccents, 1, 2);

            mod.AudioMode.Value = BPMAdjustAudioMode.PreservePitch;
            mod.TargetBPM.Value = 1;

            Assert.Multiple(() =>
            {
                Assert.That(adjustments.AggregateTempo.Value, Is.EqualTo(0.05));
                Assert.That(adjustments.AggregateFrequency.Value * adjustments.AggregateTempo.Value, Is.EqualTo(1.0 / 120).Within(1e-12));
                Assert.That(mod.AudioFallbackActive, Is.True);
            });

            mod.AudioMode.Value = BPMAdjustAudioMode.Nightcore;

            Assert.Multiple(() =>
            {
                Assert.That(adjustments.AggregateTempo.Value, Is.EqualTo(0.05));
                Assert.That(adjustments.AggregateFrequency.Value * adjustments.AggregateTempo.Value, Is.EqualTo(1.0 / 120).Within(1e-12));
                Assert.That(mod.AudioFallbackActive, Is.True);
            });

            void assertMode(BPMAdjustAudioMode mode, double expectedFrequency, double expectedTempo)
            {
                mod.AudioMode.Value = mode;

                Assert.Multiple(() =>
                {
                    Assert.That(adjustments.AggregateFrequency.Value, Is.EqualTo(expectedFrequency).Within(1e-12), mode.ToString());
                    Assert.That(adjustments.AggregateTempo.Value, Is.EqualTo(expectedTempo).Within(1e-12), mode.ToString());
                    Assert.That(adjustments.AggregateFrequency.Value * adjustments.AggregateTempo.Value, Is.EqualTo(2).Within(1e-12), mode.ToString());
                    Assert.That(mod.AudioFallbackActive, Is.False, mode.ToString());
                });
            }
        }

        [Test]
        public void TestUnrepresentableRateUsesNearestPositiveValueRatherThanOne()
        {
            var mod = new OsuModBPMAdjust();
            mod.ApplyToBeatmapInfo(new BeatmapInfo { BPM = double.MaxValue });
            mod.TargetBPM.Value = double.Epsilon;

            Assert.Multiple(() =>
            {
                Assert.That(mod.SpeedChange.Value, Is.EqualTo(double.Epsilon));
                Assert.That(mod.SpeedChange.Value, Is.Not.EqualTo(1));
                Assert.That(mod.RateWasLimited, Is.True);
            });
        }

        [Test]
        public void TestScaleMapStatsToggleOsu()
        {
            var beatmapInfo = createBeatmapInfo(approachRate: 9, overallDifficulty: 8);
            var mod = createRateMod(new OsuModBPMAdjust());
            var ruleset = new OsuRuleset();

            BeatmapDifficulty scaled = ruleset.GetAdjustedDisplayDifficulty(beatmapInfo, [mod]);

            Assert.Multiple(() =>
            {
                Assert.That(scaled.ApproachRate, Is.GreaterThan(beatmapInfo.Difficulty.ApproachRate));
                Assert.That(scaled.OverallDifficulty, Is.GreaterThan(beatmapInfo.Difficulty.OverallDifficulty));
            });

            mod.ScaleMapStatsWithBPM.Value = false;
            BeatmapDifficulty unscaled = ruleset.GetAdjustedDisplayDifficulty(beatmapInfo, [mod]);

            Assert.Multiple(() =>
            {
                Assert.That(unscaled.ApproachRate, Is.EqualTo(beatmapInfo.Difficulty.ApproachRate).Within(1e-5));
                Assert.That(unscaled.OverallDifficulty, Is.EqualTo(beatmapInfo.Difficulty.OverallDifficulty).Within(1e-5));
            });
        }

        [Test]
        public void TestUnscaledStatsApplyAfterOtherDifficultyModsRegardlessOfOrder()
        {
            var beatmapInfo = createBeatmapInfo(approachRate: 7, overallDifficulty: 6);
            var bpm = createRateMod(new OsuModBPMAdjust());
            bpm.ScaleMapStatsWithBPM.Value = false;
            var hardRock = new OsuModHardRock();
            var ruleset = new OsuRuleset();

            BeatmapDifficulty expected = ruleset.GetAdjustedDisplayDifficulty(beatmapInfo, [hardRock]);
            BeatmapDifficulty bpmFirst = ruleset.GetAdjustedDisplayDifficulty(beatmapInfo, [bpm, hardRock]);
            BeatmapDifficulty bpmLast = ruleset.GetAdjustedDisplayDifficulty(beatmapInfo, [hardRock, bpm]);

            Assert.Multiple(() =>
            {
                Assert.That(bpmFirst.ApproachRate, Is.EqualTo(expected.ApproachRate).Within(1e-5));
                Assert.That(bpmFirst.OverallDifficulty, Is.EqualTo(expected.OverallDifficulty).Within(1e-5));
                Assert.That(bpmLast.ApproachRate, Is.EqualTo(expected.ApproachRate).Within(1e-5));
                Assert.That(bpmLast.OverallDifficulty, Is.EqualTo(expected.OverallDifficulty).Within(1e-5));
            });
        }

        [Test]
        public void TestScaleMapStatsToggleCatchAndTaiko()
        {
            var beatmapInfo = createBeatmapInfo(approachRate: 9, overallDifficulty: 8);
            var catchMod = createRateMod(new CatchModBPMAdjust());
            var taikoMod = createRateMod(new TaikoModBPMAdjust());

            Assert.That(new CatchRuleset().GetAdjustedDisplayDifficulty(beatmapInfo, [catchMod]).ApproachRate,
                Is.GreaterThan(beatmapInfo.Difficulty.ApproachRate));
            Assert.That(new TaikoRuleset().GetAdjustedDisplayDifficulty(beatmapInfo, [taikoMod]).OverallDifficulty,
                Is.GreaterThan(beatmapInfo.Difficulty.OverallDifficulty));

            catchMod.ScaleMapStatsWithBPM.Value = false;
            taikoMod.ScaleMapStatsWithBPM.Value = false;

            Assert.Multiple(() =>
            {
                Assert.That(new CatchRuleset().GetAdjustedDisplayDifficulty(beatmapInfo, [catchMod]).ApproachRate,
                    Is.EqualTo(beatmapInfo.Difficulty.ApproachRate).Within(1e-5));
                Assert.That(new TaikoRuleset().GetAdjustedDisplayDifficulty(beatmapInfo, [taikoMod]).OverallDifficulty,
                    Is.EqualTo(beatmapInfo.Difficulty.OverallDifficulty).Within(1e-5));
            });
        }

        [Test]
        public void TestManiaOverallDifficultyIsAlreadyRateIndependent()
        {
            var beatmapInfo = createBeatmapInfo(approachRate: 9, overallDifficulty: 8);
            var mod = createRateMod(new ManiaModBPMAdjust());
            var ruleset = new ManiaRuleset();

            Assert.That(ruleset.GetAdjustedDisplayDifficulty(beatmapInfo, [mod]).OverallDifficulty,
                Is.EqualTo(beatmapInfo.Difficulty.OverallDifficulty).Within(1e-5));

            mod.ScaleMapStatsWithBPM.Value = false;

            Assert.That(ruleset.GetAdjustedDisplayDifficulty(beatmapInfo, [mod]).OverallDifficulty,
                Is.EqualTo(beatmapInfo.Difficulty.OverallDifficulty).Within(1e-5));
        }

        [Test]
        public void TestCloneResetAndSerialisation()
        {
            var original = new OsuModBPMAdjust
            {
                TargetBPM = { Value = 174.5 },
                AudioMode = { Value = BPMAdjustAudioMode.Nightcore },
                CustomPitchSemitones = { Value = 3.5 },
                BeatAccents = { Value = BPMAdjustBeatAccentMode.Metronome },
                ScaleMapStatsWithBPM = { Value = false }
            };
            original.ApplyToBeatmapInfo(new BeatmapInfo { BPM = 128 });

            var clone = (OsuModBPMAdjust)original.DeepClone();
            var apiMod = JsonConvert.DeserializeObject<APIMod>(JsonConvert.SerializeObject(new APIMod(original)))!;
            var deserialised = (OsuModBPMAdjust)apiMod.ToMod(new OsuRuleset());
            var preset = new ModPreset
            {
                Ruleset = new OsuRuleset().RulesetInfo,
                Mods = [original]
            };
            var presetMod = (OsuModBPMAdjust)preset.Mods.Single();

            Assert.Multiple(() =>
            {
                Assert.That(clone.TargetBPM.Value, Is.EqualTo(174.5));
                Assert.That(clone.AudioMode.Value, Is.EqualTo(BPMAdjustAudioMode.Nightcore));
                Assert.That(clone.CustomPitchSemitones.Value, Is.EqualTo(3.5));
                Assert.That(clone.BeatAccents.Value, Is.EqualTo(BPMAdjustBeatAccentMode.Metronome));
                Assert.That(clone.ScaleMapStatsWithBPM.Value, Is.False);
                Assert.That(deserialised.TargetBPM.Value, Is.EqualTo(174.5));
                Assert.That(deserialised.AudioMode.Value, Is.EqualTo(BPMAdjustAudioMode.Nightcore));
                Assert.That(deserialised.CustomPitchSemitones.Value, Is.EqualTo(3.5));
                Assert.That(deserialised.BeatAccents.Value, Is.EqualTo(BPMAdjustBeatAccentMode.Metronome));
                Assert.That(deserialised.ScaleMapStatsWithBPM.Value, Is.False);
                Assert.That(deserialised.Acronym, Is.EqualTo("BPM"));
                Assert.That(deserialised.Icon, Is.Null);
                Assert.That(deserialised.ExtendedIconInformation.ToString(), Is.EqualTo("174.5"));
                Assert.That(presetMod.TargetBPM.Value, Is.EqualTo(174.5));
                Assert.That(presetMod.AudioMode.Value, Is.EqualTo(BPMAdjustAudioMode.Nightcore));
                Assert.That(presetMod.CustomPitchSemitones.Value, Is.EqualTo(3.5));
                Assert.That(presetMod.BeatAccents.Value, Is.EqualTo(BPMAdjustBeatAccentMode.Metronome));
                Assert.That(presetMod.ScaleMapStatsWithBPM.Value, Is.False);
            });

            original.ResetSettingsToDefaults();

            Assert.Multiple(() =>
            {
                Assert.That(original.TargetBPM.Value, Is.Null);
                Assert.That(original.AudioMode.Value, Is.EqualTo(BPMAdjustAudioMode.PreservePitch));
                Assert.That(original.CustomPitchSemitones.Value, Is.Zero);
                Assert.That(original.BeatAccents.Value, Is.EqualTo(BPMAdjustBeatAccentMode.Automatic));
                Assert.That(original.ScaleMapStatsWithBPM.Value, Is.True);
                Assert.That(original.SpeedChange.Value, Is.EqualTo(1));
            });
        }

        [Test]
        public void TestExplicitNeutralTargetSurvivesClonePresetScoreAndMapChange()
        {
            var original = new OsuModBPMAdjust();
            original.ApplyToBeatmapInfo(new BeatmapInfo { BPM = 120 });
            original.TargetBPM.Value = null;

            var clone = (OsuModBPMAdjust)original.DeepClone();
            var preset = new ModPreset
            {
                Ruleset = new OsuRuleset().RulesetInfo,
                Mods = [original],
            };
            var score = new ScoreInfo
            {
                Ruleset = new OsuRuleset().RulesetInfo,
                Mods = [original],
            };

            var presetMod = (OsuModBPMAdjust)preset.Mods.Single();
            var scoreMod = (OsuModBPMAdjust)score.DeepClone().Mods.Single();

            clone.ApplyToBeatmapInfo(new BeatmapInfo { BPM = 150 });
            presetMod.ApplyToBeatmapInfo(new BeatmapInfo { BPM = 160 });
            scoreMod.ApplyToBeatmapInfo(new BeatmapInfo { BPM = 170 });

            Assert.Multiple(() =>
            {
                Assert.That(original.TargetInitialised.Value, Is.True);
                Assert.That(clone.TargetBPM.Value, Is.Null);
                Assert.That(presetMod.TargetBPM.Value, Is.Null);
                Assert.That(scoreMod.TargetBPM.Value, Is.Null);
                Assert.That(score.ModsJson, Does.Contain("target_initialised"));
            });
        }

        [Test]
        public void TestPresetStoreMigratesAndSerialisesVersionedData()
        {
            var migrated = BPMPresetStore.Deserialise("200;120;174.5;174.5;invalid");
            string serialised = BPMPresetStore.Serialise(migrated);
            var reloaded = BPMPresetStore.Deserialise(serialised);

            Assert.Multiple(() =>
            {
                Assert.That(migrated.Select(p => p.BPM), Is.EqualTo(new[] { 120, 174.5, 200 }));
                Assert.That(serialised, Does.StartWith("{\"version\":1"));
                Assert.That(reloaded.Select(p => p.Name), Is.EqualTo(new[] { "120 BPM", "174.5 BPM", "200 BPM" }));
            });
        }

        [Test]
        public void TestAudioFallbackIsReported()
        {
            var mod = new OsuModBPMAdjust();
            mod.ApplyToBeatmapInfo(new BeatmapInfo { BPM = 120 });
            mod.TargetBPM.Value = 1;

            Assert.Multiple(() =>
            {
                Assert.That(mod.AudioFallbackActive, Is.True);
                Assert.That(mod.SettingDescription.Any(entry => entry.setting.ToString() == "Audio warning"), Is.True);
            });
        }

        [TestCase(typeof(OsuRuleset), typeof(OsuModBPMAdjust))]
        [TestCase(typeof(TaikoRuleset), typeof(TaikoModBPMAdjust))]
        [TestCase(typeof(CatchRuleset), typeof(CatchModBPMAdjust))]
        [TestCase(typeof(ManiaRuleset), typeof(ManiaModBPMAdjust))]
        public void TestRegisteredInEveryRuleset(Type rulesetType, Type expectedModType)
        {
            var ruleset = (Ruleset)Activator.CreateInstance(rulesetType)!;
            Assert.That(ruleset.GetModsFor(ModType.Fun).Any(mod => mod.GetType() == expectedModType), Is.True);
        }

        [Test]
        public void TestUnrankedLocalOnlyAndRateModIncompatibility()
        {
            var mod = new OsuModBPMAdjust();

            Assert.Multiple(() =>
            {
                Assert.That(mod.Ranked, Is.False);
                Assert.That(mod.ValidForMultiplayer, Is.False);
                Assert.That(ModUtils.CheckCompatibleSet([mod, new OsuModDoubleTime()]), Is.False);
                Assert.That(ModUtils.CheckCompatibleSet([mod, new OsuModHalfTime()]), Is.False);
                Assert.That(ModUtils.CheckCompatibleSet([mod, new OsuModNightcore()]), Is.False);
                Assert.That(ModUtils.CheckCompatibleSet([mod, new OsuModDaycore()]), Is.False);
                Assert.That(ModUtils.CheckCompatibleSet([mod, new ModAdaptiveSpeed()]), Is.False);
                Assert.That(ModUtils.CheckCompatibleSet([mod, new ModWindUp()]), Is.False);
            });
        }

        private static T createRateMod<T>(T mod)
            where T : ModBPMAdjust
        {
            mod.ApplyToBeatmapInfo(new BeatmapInfo { BPM = 120 });
            mod.TargetBPM.Value = 180;
            return mod;
        }

        private static BeatmapInfo createBeatmapInfo(float approachRate, float overallDifficulty) => new BeatmapInfo
        {
            Difficulty = new BeatmapDifficulty
            {
                ApproachRate = approachRate,
                OverallDifficulty = overallDifficulty,
            }
        };
    }
}

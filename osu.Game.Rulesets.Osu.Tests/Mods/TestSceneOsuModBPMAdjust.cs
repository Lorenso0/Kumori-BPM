// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Framework.Utils;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Tests.Mods
{
    public partial class TestSceneOsuModBPMAdjust : OsuModTestScene
    {
        [Test]
        public void TestGameplayUsesTargetBPMRate()
        {
            var mod = new OsuModBPMAdjust
            {
                TargetBPM = { Value = 180 }
            };

            CreateModTest(new ModTestData
            {
                Mod = mod,
                PassCondition = () =>
                {
                    var appliedMod = Player.GameplayState.Mods.OfType<OsuModBPMAdjust>().Single();
                    return Player.ScoreProcessor.JudgedHits >= 2
                           && appliedMod.SourceBPM > 0
                           && Precision.AlmostEquals(Player.GameplayClockContainer.Rate, 180 / appliedMod.SourceBPM);
                }
            });
        }

        [TestCase(BPMAdjustAudioMode.PreservePitch, false)]
        [TestCase(BPMAdjustAudioMode.AdjustPitch, false)]
        [TestCase(BPMAdjustAudioMode.Nightcore, true)]
        public void TestNightcoreBeatContainerOnlyAddedForNightcore(BPMAdjustAudioMode audioMode, bool shouldAddBeatContainer)
        {
            var mod = new OsuModBPMAdjust
            {
                AudioMode = { Value = audioMode }
            };

            CreateModTest(new ModTestData
            {
                Mod = mod,
                PassCondition = () => Player.ScoreProcessor.JudgedHits >= 2
                                      && this.ChildrenOfType<ModNightcore<OsuHitObject>.NightcoreBeatContainer>().Any() == shouldAddBeatContainer
            });
        }
    }
}

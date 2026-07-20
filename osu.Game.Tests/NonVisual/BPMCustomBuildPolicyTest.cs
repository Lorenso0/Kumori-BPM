// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Game.Customisation;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Catch.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;

namespace osu.Game.Tests.NonVisual
{
    [TestFixture]
    public class BPMCustomBuildPolicyTest
    {
        [Test]
        public void TestSocialOnlineFeaturesRemainEnabledWhileSubmissionIsBlocked()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BPMCustomBuildPolicy.OnlineChatEnabled, Is.True);
                Assert.That(BPMCustomBuildPolicy.OnlineBeatmapAccessEnabled, Is.True);
                Assert.That(BPMCustomBuildPolicy.CanSubmitScore(System.Array.Empty<Mod>()), Is.False);
            });
        }

        [Test]
        public void TestUpdaterTargetsOnlyKumoriReleases()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BPMCustomBuildPolicy.SelfUpdatesEnabled, Is.True);
                Assert.That(BPMCustomBuildPolicy.UPDATE_REPOSITORY_URL, Is.EqualTo("https://github.com/Lorenso0/Kumori-BPM"));
                Assert.That(BPMCustomBuildPolicy.UPDATE_REPOSITORY_URL, Does.Not.Contain("ppy/osu"));
                Assert.That(BPMCustomBuildPolicy.UPDATE_RELEASES_URL, Is.EqualTo("https://github.com/Lorenso0/Kumori-BPM/releases/latest"));
                Assert.That(BPMCustomBuildPolicy.VELOPACK_APP_ID, Is.EqualTo("KumoriBPM"));
            });
        }

        [Test]
        public void TestOnlyPinnedVersionRejectionIsDemoted()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BPMCustomBuildPolicy.IsExpectedPinnedVersionRejection(
                    "Realtime online functionality is not supported on this version of the game. Please upgrade."), Is.True);
                Assert.That(BPMCustomBuildPolicy.IsExpectedPinnedVersionRejection("Connection lost."), Is.False);
                Assert.That(BPMCustomBuildPolicy.IsExpectedPinnedVersionRejection(null), Is.False);
            });
        }

        [Test]
        public void TestGimmickModsHiddenButStillResolvable()
        {
            Mod[] hiddenMods =
            [
                new OsuModTransform(),
                new OsuModWiggle(),
                new OsuModSpinIn(),
                new OsuModGrow(),
                new OsuModDeflate(),
                new OsuModBarrelRoll(),
                new OsuModApproachDifferent(),
                new OsuModNoScope(),
                new OsuModMagnetised(),
                new OsuModRepel(),
                new OsuModFreezeFrame(),
                new OsuModBubbles(),
                new OsuModSynesthesia(),
                new OsuModDepth(),
                new OsuModBloom(),
                new CatchModFloatingFruits(),
                new CatchModNoScope(),
                new CatchModMovingFast(),
                new CatchModSynesthesia(),
            ];

            Assert.Multiple(() =>
            {
                Assert.That(hiddenMods.All(mod => !BPMCustomBuildPolicy.ShouldShowModInSelector(mod)), Is.True);
                Assert.That(BPMCustomBuildPolicy.ShouldShowModInSelector(new OsuModBPMAdjust()), Is.True);
                Assert.That(BPMCustomBuildPolicy.ShouldShowModInSelector(new OsuModMuted()), Is.True);
                Assert.That(BPMCustomBuildPolicy.ShouldShowModInSelector(new OsuModDoubleTime()), Is.True);

                var osuRuleset = new OsuRuleset();
                var catchRuleset = new CatchRuleset();

                foreach (var mod in hiddenMods.Take(15))
                    Assert.That(osuRuleset.CreateModFromAcronym(mod.Acronym), Is.Not.Null);

                foreach (var mod in hiddenMods.Skip(15))
                    Assert.That(catchRuleset.CreateModFromAcronym(mod.Acronym), Is.Not.Null);
            });
        }
    }
}

// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Customisation;
using osu.Game.Rulesets.Mods;

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
    }
}

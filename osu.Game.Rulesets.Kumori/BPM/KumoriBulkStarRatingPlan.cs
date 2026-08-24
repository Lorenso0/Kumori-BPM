// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;

namespace osu.Game.Rulesets.Kumori.BPM
{
    internal static class KumoriBulkStarRatingPlan
    {
        public const int MinimumBPM = 220;
        public const int MaximumBPM = 270;
        public const int ProfileCount = (MaximumBPM - MinimumBPM + 1) * 2;

        public static IReadOnlyList<KumoriBulkStarRatingProfile> Create(IRulesetInfo rulesetInfo)
        {
            var profiles = new List<KumoriBulkStarRatingProfile>(ProfileCount);

            for (int targetBPM = MinimumBPM; targetBPM <= MaximumBPM; targetBPM++)
            {
                profiles.Add(createProfile(rulesetInfo, targetBPM, hidden: false));
                profiles.Add(createProfile(rulesetInfo, targetBPM, hidden: true));
            }

            return profiles;
        }

        private static KumoriBulkStarRatingProfile createProfile(IRulesetInfo rulesetInfo, int targetBPM, bool hidden)
        {
            Mod[] mods = createMods(targetBPM, hidden);
            return new KumoriBulkStarRatingProfile(targetBPM, hidden, KumoriStarRatingIndex.CreateProfileKey(rulesetInfo, mods), mods);
        }

        private static Mod[] createMods(int targetBPM, bool hidden)
        {
            var bpm = new KumoriModBPMAdjust
            {
                TargetBPM = { Value = targetBPM },
                ScaleMapStatsWithBPM = { Value = true },
            };
            var difficultyAdjust = new OsuModDifficultyAdjust
            {
                ApproachRate = { Value = 10 },
                DrainRate = { Value = 0 },
            };

            return hidden
                ? [bpm, new OsuModHidden(), difficultyAdjust]
                : [bpm, difficultyAdjust];
        }
    }

    internal sealed record KumoriBulkStarRatingProfile(int TargetBPM, bool Hidden, string ProfileKey, Mod[] TemplateMods)
    {
        public Mod[] CreateWorkerMods() => TemplateMods.Select(mod => mod.DeepClone()).ToArray();
    }
}

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
        public const int ProfileCount = MaximumBPM - MinimumBPM + 1;

        public static IReadOnlyList<KumoriBulkStarRatingProfile> Create(IRulesetInfo rulesetInfo)
        {
            var profiles = new List<KumoriBulkStarRatingProfile>(ProfileCount);

            for (int targetBPM = MinimumBPM; targetBPM <= MaximumBPM; targetBPM++)
                profiles.Add(createProfile(rulesetInfo, targetBPM));

            return profiles;
        }

        public static IReadOnlyList<string> CreateObsoleteBulkProfileKeys(IRulesetInfo rulesetInfo)
        {
            var profileKeys = new List<string>(ProfileCount * 2);

            for (int targetBPM = MinimumBPM; targetBPM <= MaximumBPM; targetBPM++)
            {
                Mod[] mods = createObsoleteMods(targetBPM);
                profileKeys.Add(KumoriStarRatingIndex.CreateObsoleteProfileKey(rulesetInfo, mods));
                profileKeys.Add(KumoriStarRatingIndex.CreateObsoleteProfileKey(rulesetInfo, [.. mods, new OsuModHidden()]));
            }

            return profileKeys;
        }

        private static KumoriBulkStarRatingProfile createProfile(IRulesetInfo rulesetInfo, int targetBPM)
        {
            Mod[] mods = createMods(targetBPM);
            return new KumoriBulkStarRatingProfile(targetBPM, KumoriStarRatingIndex.CreateProfileKey(rulesetInfo, mods), mods);
        }

        private static Mod[] createMods(int targetBPM)
        {
            var bpm = new KumoriModBPMAdjust
            {
                TargetBPM = { Value = targetBPM },
                ScaleMapStatsWithBPM = { Value = false },
            };
            var difficultyAdjust = new OsuModDifficultyAdjust
            {
                ApproachRate = { Value = 10 },
                DrainRate = { Value = 0 },
            };

            return [bpm, difficultyAdjust];
        }

        private static Mod[] createObsoleteMods(int targetBPM)
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

            return [bpm, difficultyAdjust];
        }
    }

    internal sealed record KumoriBulkStarRatingProfile(int TargetBPM, string ProfileKey, Mod[] TemplateMods)
    {
        public Mod[] CreateWorkerMods() => TemplateMods.Select(mod => mod.DeepClone()).ToArray();
    }
}

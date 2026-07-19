// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;

namespace osu.Game.Rulesets.Mods
{
    public static class BeatmapDependentModExtensions
    {
        /// <summary>
        /// Prepares all mods which depend on beatmap metadata before preview attributes
        /// and audio rate are calculated.
        /// </summary>
        public static void ApplyBeatmapInfo(this IEnumerable<Mod> mods, IBeatmapInfo beatmapInfo)
        {
            foreach (var mod in mods.OfType<IApplicableToBeatmapInfo>())
                mod.ApplyToBeatmapInfo(beatmapInfo);
        }
    }
}

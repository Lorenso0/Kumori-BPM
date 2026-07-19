// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Beatmaps;

namespace osu.Game.Rulesets.Mods
{
    /// <summary>
    /// Canonical source-BPM resolution used by song select, gameplay, and replay playback.
    /// </summary>
    public static class BPMResolver
    {
        public static double FromBeatmapInfo(IBeatmapInfo beatmapInfo) =>
            normalise(beatmapInfo.BPM);

        public static double FromBeatmap(IBeatmap beatmap)
        {
            double beatLength = beatmap.GetMostCommonBeatLength();
            return beatLength > 0 ? normalise(60000 / beatLength) : 0;
        }

        public static bool IsValid(double bpm) => bpm > 0 && double.IsFinite(bpm);

        private static double normalise(double bpm) => IsValid(bpm) ? bpm : 0;
    }
}

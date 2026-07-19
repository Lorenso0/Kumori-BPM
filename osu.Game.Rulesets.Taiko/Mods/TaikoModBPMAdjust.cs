// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Taiko.Objects;
using osu.Game.Rulesets.Taiko.Scoring;

namespace osu.Game.Rulesets.Taiko.Mods
{
    public class TaikoModBPMAdjust : ModBPMAdjust<TaikoHitObject>, IApplicableToDifficultyAfterMods
    {
        public void ApplyToDifficultyAfterMods(BeatmapDifficulty difficulty) =>
            CompensateOverallDifficulty(difficulty, TaikoHitWindows.GREAT_WINDOW_RANGE);
    }
}

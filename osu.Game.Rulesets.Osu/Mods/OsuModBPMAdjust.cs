// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Scoring;

namespace osu.Game.Rulesets.Osu.Mods
{
    public class OsuModBPMAdjust : ModBPMAdjust<OsuHitObject>, IApplicableToDifficultyAfterMods
    {
        public void ApplyToDifficultyAfterMods(BeatmapDifficulty difficulty)
        {
            CompensateApproachRate(difficulty, OsuHitObject.PREEMPT_RANGE);
            CompensateOverallDifficulty(difficulty, OsuHitWindows.GREAT_WINDOW_RANGE);
        }
    }
}

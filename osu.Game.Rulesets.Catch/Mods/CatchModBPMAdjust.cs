// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Beatmaps;
using osu.Game.Rulesets.Catch.Objects;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Catch.Mods
{
    public class CatchModBPMAdjust : ModBPMAdjust<CatchHitObject>, IApplicableToDifficultyAfterMods
    {
        public void ApplyToDifficultyAfterMods(BeatmapDifficulty difficulty) =>
            CompensateApproachRate(difficulty, CatchHitObject.PREEMPT_RANGE);
    }
}

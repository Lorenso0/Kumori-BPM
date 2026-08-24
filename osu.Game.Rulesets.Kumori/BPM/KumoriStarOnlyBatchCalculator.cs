// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Osu.Difficulty.Skills;

namespace osu.Game.Rulesets.Kumori.BPM
{
    /// <summary>
    /// Calculates only the exact star values needed by the shared index. A regular difficulty
    /// calculation builds a playable beatmap, difficulty objects, four skills, score simulation,
    /// and every public attribute for each mod combination. The bulk matrix only needs star rating.
    /// </summary>
    internal sealed class KumoriStarOnlyBatchCalculator : OsuDifficultyCalculator
    {
        private readonly IRulesetInfo ruleset;
        private readonly IWorkingBeatmap workingBeatmap;

        public KumoriStarOnlyBatchCalculator(IRulesetInfo ruleset, IWorkingBeatmap workingBeatmap)
            : base(ruleset, workingBeatmap)
        {
            this.ruleset = ruleset;
            this.workingBeatmap = workingBeatmap;
        }

        public double CalculateStarRating(IEnumerable<Mod> mods, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Mod[] selectedMods = mods.Select(mod => mod.DeepClone()).ToArray();
            IBeatmap beatmap = workingBeatmap.GetPlayableBeatmap(ruleset, selectedMods, cancellationToken);

            if (beatmap.HitObjects.Count == 0)
                return 0;

            var aim = new Aim(selectedMods, includeSliders: true);
            var speed = new Speed(selectedMods);
            var reading = new Reading(KumoriDifficultyCalculator.WithoutHidden(selectedMods));
            Skill[] skills = [aim, speed, reading];
            List<DifficultyHitObject> difficultyObjects = SortObjects(CreateDifficultyHitObjects(beatmap, selectedMods)).ToList();

            foreach (DifficultyHitObject hitObject in difficultyObjects)
            {
                foreach (Skill skill in skills)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    skill.Process(hitObject);
                }
            }

            double aimRating = DiffUtils.Pow(aim.DifficultyValue(), 0.63) * 0.02275;
            double speedRating = Math.Sqrt(speed.DifficultyValue()) * 0.0675;
            double readingRating = Math.Sqrt(reading.DifficultyValue()) * 0.0675;
            double baseAimPerformance = OsuPerformanceCalculator.DifficultyToPerformance(aimRating);
            double baseSpeedPerformance = HarmonicSkill.DifficultyToPerformance(speedRating);
            double baseReadingPerformance = HarmonicSkill.DifficultyToPerformance(readingRating);
            double basePerformance = DiffUtils.Norm(OsuPerformanceCalculator.PERFORMANCE_NORM_EXPONENT,
                baseAimPerformance, baseSpeedPerformance, baseReadingPerformance);
            return Math.Cbrt(basePerformance * OsuPerformanceCalculator.PERFORMANCE_BASE_MULTIPLIER);
        }
    }
}

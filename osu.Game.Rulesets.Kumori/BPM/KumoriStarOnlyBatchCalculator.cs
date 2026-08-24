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
    /// and every public attribute for each mod combination. The bulk matrix only needs star rating,
    /// and its NM/HD pair differs solely in Reading, so Aim, Speed, and all preprocessing are shared.
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

        public KumoriStarRatingPair CalculatePair(IEnumerable<Mod> noHiddenMods, IEnumerable<Mod> hiddenMods, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Mod[] noHidden = noHiddenMods.Select(mod => mod.DeepClone()).ToArray();
            Mod[] hidden = hiddenMods.Select(mod => mod.DeepClone()).ToArray();

            // Hidden only changes object fade timing. Build the Hidden playable map once: Aim,
            // Speed, and non-Hidden Reading do not consume the altered fade-out value, while
            // Hidden Reading does. This therefore exactly represents both profiles.
            IBeatmap beatmap = workingBeatmap.GetPlayableBeatmap(ruleset, hidden, cancellationToken);

            if (beatmap.HitObjects.Count == 0)
                return new KumoriStarRatingPair(0, 0);

            var aim = new Aim(noHidden, includeSliders: true);
            var speed = new Speed(noHidden);
            var reading = new Reading(noHidden);
            var hiddenReading = new Reading(hidden);
            Skill[] skills = [aim, speed, reading, hiddenReading];
            List<DifficultyHitObject> difficultyObjects = SortObjects(CreateDifficultyHitObjects(beatmap, hidden)).ToList();

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
            double hiddenReadingRating = Math.Sqrt(hiddenReading.DifficultyValue()) * 0.0675;
            double baseAimPerformance = OsuPerformanceCalculator.DifficultyToPerformance(aimRating);
            double baseSpeedPerformance = HarmonicSkill.DifficultyToPerformance(speedRating);

            double calculateStarRating(double readingDifficulty)
            {
                double baseReadingPerformance = HarmonicSkill.DifficultyToPerformance(readingDifficulty);
                double basePerformance = DiffUtils.Norm(OsuPerformanceCalculator.PERFORMANCE_NORM_EXPONENT,
                    baseAimPerformance, baseSpeedPerformance, baseReadingPerformance);
                return Math.Cbrt(basePerformance * OsuPerformanceCalculator.PERFORMANCE_BASE_MULTIPLIER);
            }

            return new KumoriStarRatingPair(calculateStarRating(readingRating), calculateStarRating(hiddenReadingRating));
        }
    }

    internal readonly record struct KumoriStarRatingPair(double NoHidden, double Hidden);
}

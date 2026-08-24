// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Osu.Difficulty.Skills;
using osu.Game.Rulesets.Osu.Mods;

namespace osu.Game.Rulesets.Kumori.BPM
{
    /// <summary>
    /// Keeps Hidden's gameplay and mod identity while excluding its visibility bonus from star difficulty.
    /// </summary>
    internal sealed class KumoriDifficultyCalculator : OsuDifficultyCalculator
    {
        internal const int OfficialDifficultyVersion = 20260706;

        // Keep osu!'s calculator version in the identity while ensuring its cached result cannot be
        // confused with the official Hidden-aware implementation.
        public override int Version => OfficialDifficultyVersion * 10 + 1;

        public KumoriDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
            : base(ruleset, beatmap)
        {
        }

        protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods)
        {
            Mod[] starMods = WithoutHidden(mods);
            var skills = new List<Skill>
            {
                new Aim(starMods, true),
                new Aim(starMods, false),
                new Speed(starMods),
                new Reading(starMods)
            };

            if (mods.Any(mod => mod is OsuModFlashlight))
                skills.Add(new Flashlight(starMods, beatmap.HitObjects.Count));

            return skills.ToArray();
        }

        internal static Mod[] WithoutHidden(IEnumerable<Mod> mods) => mods.Where(mod => mod is not OsuModHidden).ToArray();
    }
}

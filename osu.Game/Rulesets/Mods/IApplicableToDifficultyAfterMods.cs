// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Beatmaps;

namespace osu.Game.Rulesets.Mods
{
    /// <summary>
    /// Applies a difficulty adjustment after all regular <see cref="IApplicableToDifficulty"/>
    /// mods. This is intended for adjustments which must consume the final difficulty
    /// produced by mods such as Easy, Hard Rock, and Difficulty Adjust.
    /// </summary>
    public interface IApplicableToDifficultyAfterMods : IApplicableMod
    {
        void ApplyToDifficultyAfterMods(BeatmapDifficulty difficulty);
    }
}

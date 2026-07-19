// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Beatmaps;

namespace osu.Game.Rulesets.Mods
{
    /// <summary>
    /// Prepares a mod from the fully decoded source beatmap before conversion and
    /// difficulty adjustments. This is suitable for beatmap-derived mod settings
    /// which later mod stages depend on.
    /// </summary>
    public interface IApplicableToBeatmapBeforeConversion : IApplicableMod
    {
        void ApplyToBeatmapBeforeConversion(IBeatmap beatmap);
    }
}

// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Beatmaps;

namespace osu.Game.Rulesets.Mods
{
    /// <summary>
    /// Interface for a <see cref="Mod"/> which needs selected beatmap metadata before gameplay begins.
    /// </summary>
    public interface IApplicableToBeatmapInfo : IApplicableMod
    {
        /// <summary>
        /// Applies metadata from the currently selected beatmap to this mod.
        /// </summary>
        /// <param name="beatmapInfo">The selected beatmap metadata.</param>
        void ApplyToBeatmapInfo(IBeatmapInfo beatmapInfo);
    }
}

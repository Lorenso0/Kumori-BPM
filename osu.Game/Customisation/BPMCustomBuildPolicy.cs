// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Customisation
{
    /// <summary>
    /// Central policy for functionality which differs from an official osu! build.
    /// Social/API functionality intentionally remains enabled; only actions which
    /// could submit custom gameplay to official services are blocked.
    /// </summary>
    public static class BPMCustomBuildPolicy
    {
        public const string ISOLATED_PROFILE_ARGUMENT = "--bpm-isolated";
        public const string ISOLATED_GAME_NAME = "osu-bpm";

        public static bool OnlineChatEnabled => true;
        public static bool OnlineBeatmapAccessEnabled => true;
        public static bool ScoreSubmissionEnabled => false;

        public static bool CanSubmitScore(IReadOnlyList<Mod> mods) => ScoreSubmissionEnabled;

        /// <summary>
        /// The realtime server currently communicates an outdated-client rejection
        /// as human-readable text rather than a typed error code. Keep the matching
        /// in one location until the protocol exposes a machine-readable reason.
        /// </summary>
        public static bool IsExpectedPinnedVersionRejection(string? message) =>
            message?.StartsWith("Realtime online functionality is not supported on this version of the game.", StringComparison.OrdinalIgnoreCase) == true;
    }
}

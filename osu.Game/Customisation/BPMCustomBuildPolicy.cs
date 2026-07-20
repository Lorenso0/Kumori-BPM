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
        private static readonly HashSet<string> hidden_gimmick_mod_types = new HashSet<string>
        {
            "osu.Game.Rulesets.Osu.Mods.OsuModTransform",
            "osu.Game.Rulesets.Osu.Mods.OsuModWiggle",
            "osu.Game.Rulesets.Osu.Mods.OsuModSpinIn",
            "osu.Game.Rulesets.Osu.Mods.OsuModGrow",
            "osu.Game.Rulesets.Osu.Mods.OsuModDeflate",
            "osu.Game.Rulesets.Osu.Mods.OsuModBarrelRoll",
            "osu.Game.Rulesets.Osu.Mods.OsuModApproachDifferent",
            "osu.Game.Rulesets.Osu.Mods.OsuModNoScope",
            "osu.Game.Rulesets.Osu.Mods.OsuModMagnetised",
            "osu.Game.Rulesets.Osu.Mods.OsuModRepel",
            "osu.Game.Rulesets.Osu.Mods.OsuModFreezeFrame",
            "osu.Game.Rulesets.Osu.Mods.OsuModBubbles",
            "osu.Game.Rulesets.Osu.Mods.OsuModSynesthesia",
            "osu.Game.Rulesets.Osu.Mods.OsuModDepth",
            "osu.Game.Rulesets.Osu.Mods.OsuModBloom",
            "osu.Game.Rulesets.Catch.Mods.CatchModFloatingFruits",
            "osu.Game.Rulesets.Catch.Mods.CatchModNoScope",
            "osu.Game.Rulesets.Catch.Mods.CatchModMovingFast",
            "osu.Game.Rulesets.Catch.Mods.CatchModSynesthesia",
        };

        public const string ISOLATED_PROFILE_ARGUMENT = "--bpm-isolated";
        public const string ISOLATED_GAME_NAME = "osu-bpm";
        public const string UPDATE_REPOSITORY_URL = "https://github.com/Lorenso0/Kumori-BPM";
        public const string UPDATE_RELEASES_URL = UPDATE_REPOSITORY_URL + "/releases/latest";
        public const string VELOPACK_APP_ID = "KumoriBPM";

        public static bool SelfUpdatesEnabled => true;
        public static bool OnlineChatEnabled => true;
        public static bool OnlineBeatmapAccessEnabled => true;
        public static bool ScoreSubmissionEnabled => false;

        public static bool CanSubmitScore(IReadOnlyList<Mod> mods) => ScoreSubmissionEnabled;

        public static bool ShouldShowModInSelector(Mod mod) =>
            !hidden_gimmick_mod_types.Contains(mod.GetType().FullName ?? string.Empty);

        /// <summary>
        /// The realtime server currently communicates an outdated-client rejection
        /// as human-readable text rather than a typed error code. Keep the matching
        /// in one location until the protocol exposes a machine-readable reason.
        /// </summary>
        public static bool IsExpectedPinnedVersionRejection(string? message) =>
            message?.StartsWith("Realtime online functionality is not supported on this version of the game.", StringComparison.OrdinalIgnoreCase) == true;
    }
}

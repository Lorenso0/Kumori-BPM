// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Online.API;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Kumori.BPM;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Scoring;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.Kumori
{
    /// <summary>
    /// Gives memory readers an osu!standard-shaped mod list during an active Kumori play without
    /// mutating the live ruleset identity. The real score data is restored before osu! imports it.
    /// </summary>
    internal sealed class TosuCompatibilityIdentity
    {
        private GameplayState? gameplayState;
        private string? originalModsJson;
        private RulesetInfo? originalRuleset;

        public void Attach(GameplayState state)
        {
            Restore();
            gameplayState = state;
            originalModsJson = state.Score.ScoreInfo.ModsJson;
            originalRuleset = state.Score.ScoreInfo.Ruleset;

            // tosu treats the score's online ruleset ID as the active game mode and only
            // exposes IDs 0-3. Keep Kumori's real RulesetInfo untouched, but present a
            // detached, osu!standard-compatible copy on the in-flight score. This avoids
            // colliding with the built-in ruleset in song select while allowing memory
            // readers to recognise the play as osu!standard.
            RulesetInfo compatibleRuleset = originalRuleset.Clone();
            compatibleRuleset.OnlineID = 0;
            state.Score.ScoreInfo.Ruleset = compatibleRuleset;
            state.Score.ScoreInfo.ModsJson = CreateTosuModsJson(state.Mods);
        }

        public void Restore()
        {
            if (gameplayState != null)
            {
                if (originalRuleset != null)
                    gameplayState.Score.ScoreInfo.Ruleset = originalRuleset;

                if (originalModsJson != null)
                    gameplayState.Score.ScoreInfo.ModsJson = originalModsJson;
            }

            gameplayState = null;
            originalModsJson = null;
            originalRuleset = null;
        }

        internal static string CreateTosuModsJson(IReadOnlyList<Mod> mods)
        {
            APIMod[] apiMods = mods.Select(mod =>
            {
                var apiMod = new APIMod(mod);

                if (mod is KumoriModBPMAdjust bpm)
                {
                    // tosu accepts custom acronyms from ScoreInfo.ModsJson and uses
                    // speed_change independently to calculate the live clock rate.
                    // Preserve BPM (and APIMod's target/audio/stat settings) so Kumori
                    // can distinguish the custom mod from ordinary DT/HT gameplay.
                    apiMod.Settings["speed_change"] = bpm.SpeedChange.Value;
                }

                return apiMod;
            }).ToArray();

            return apiMods.Length == 0 ? string.Empty : JsonConvert.SerializeObject(apiMods);
        }
    }

    internal partial class TosuCompatibleDrawableOsuRuleset : DrawableOsuRuleset
    {
        private readonly TosuCompatibilityIdentity identity;

        public TosuCompatibleDrawableOsuRuleset(OsuRuleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod>? mods, TosuCompatibilityIdentity identity)
            : base(ruleset, beatmap, mods)
        {
            this.identity = identity;
        }

        [BackgroundDependencyLoader]
        private void load(GameplayState gameplayState)
        {
            identity.Attach(gameplayState);
        }

        protected override void Dispose(bool isDisposing)
        {
            identity.Restore();
            base.Dispose(isDisposing);
        }
    }

    internal partial class TosuCompatibleOsuScoreProcessor : OsuScoreProcessor
    {
        public TosuCompatibleOsuScoreProcessor(TosuCompatibilityIdentity identity)
        {
            HasCompleted.BindValueChanged(completed =>
            {
                if (completed.NewValue)
                    identity.Restore();
            });
        }
    }

    internal partial class TosuCompatibleOsuHealthProcessor : OsuHealthProcessor
    {
        public TosuCompatibleOsuHealthProcessor(double drainStartTime, TosuCompatibilityIdentity identity)
            : base(drainStartTime)
        {
            Failed += () =>
            {
                identity.Restore();
                return false;
            };
        }
    }
}

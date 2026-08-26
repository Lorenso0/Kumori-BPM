// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.Spectator;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.Kumori.BPM;
using osu.Game.Rulesets.Kumori.Updates;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Kumori
{
    /// <summary>
    /// A compact version of Kumori's ringed wordmark, designed to remain legible in osu!'s
    /// smallest ruleset-icon placements.
    /// </summary>
    public partial class KumoriRulesetIcon : CompositeDrawable
    {
        private IDisposable? updateNotificationSubscription;
        private readonly IBindableDictionary<int, SpectatorState> spectatorStates = new BindableDictionary<int, SpectatorState>();

        public KumoriRulesetIcon()
        {
            Size = new Vector2(32);

            InternalChildren =
            [
                new CircularContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(30),
                    Masking = true,
                    BorderThickness = 2.5f,
                    BorderColour = Color4.White,
                    Child = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Alpha = 0,
                        AlwaysPresent = true,
                    },
                },
                new OsuSpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = "K",
                    Y = -1,
                    Font = OsuFont.TorusAlternate.With(size: 22, weight: FontWeight.Bold),
                },
            ];
        }

        [BackgroundDependencyLoader]
        private void load(INotificationOverlay notifications, SpectatorClient spectatorClient)
        {
            updateNotificationSubscription = KumoriUpdateNotificationBus.Attach(message =>
                Schedule(() => notifications.Post(new SimpleNotification { Text = message })));

            // The toolbar creates and keeps one icon for every installed ruleset, even while osu!
            // is selected. Bind before a spectator screen is opened so marked incoming states are
            // rewritten to Kumori before that screen resolves the numeric ruleset ID.
            spectatorStates.BindTo(spectatorClient.WatchedUserStates);
            spectatorStates.BindCollectionChanged(onSpectatorStatesChanged, true);
        }

        private void onSpectatorStatesChanged(object? sender, NotifyDictionaryChangedEventArgs<int, SpectatorState> change)
        {
            if (change.NewItems == null)
                return;

            foreach ((_, SpectatorState state) in change.NewItems)
                TryRestoreKumoriSpectatorState(state);
        }

        internal static bool TryRestoreKumoriSpectatorState(SpectatorState state)
        {
            if (state.RulesetID != 0)
                return false;

            bool isKumori = state.Mods.Any(mod =>
                mod.Acronym == "BPM"
                && mod.Settings.TryGetValue(KumoriModBPMAdjust.SPECTATOR_MARKER_SETTING, out object? marker)
                && marker is true);

            if (!isKumori)
                return false;

            state.RulesetID = -1;
            return true;
        }

        protected override void Dispose(bool isDisposing)
        {
            updateNotificationSubscription?.Dispose();
            base.Dispose(isDisposing);
        }
    }
}

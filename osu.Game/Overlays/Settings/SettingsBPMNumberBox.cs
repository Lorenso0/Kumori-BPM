// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osuTK;
using CommonStrings = osu.Game.Resources.Localisation.Web.CommonStrings;

namespace osu.Game.Overlays.Settings
{
    /// <summary>
    /// A positive decimal number box used for BPM mod settings.
    /// </summary>
    public partial class SettingsBPMNumberBox : SettingsItem<double?>
    {
        protected override Drawable CreateControl() => new BPMNumberControl
        {
            RelativeSizeAxes = Axes.X,
        };

        private sealed partial class BPMNumberControl : CompositeDrawable, IHasCurrentValue<double?>
        {
            private readonly BindableWithCurrent<double?> current = new BindableWithCurrent<double?>();
            private readonly List<BPMPreset> presets = new List<BPMPreset>();

            public Bindable<double?> Current
            {
                get => current.Current;
                set => current.Current = value;
            }

            private BPMTextBox numberBox = null!;
            private FillFlowContainer presetsFlow = null!;
            private RoundedButton? addPresetButton;
            private Bindable<string>? presetStorage;
            private bool updatingFromText;

            public BPMNumberControl()
            {
                AutoSizeAxes = Axes.Y;

                InternalChild = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(5),
                    Children = new Drawable[]
                    {
                        numberBox = new BPMTextBox
                        {
                            RelativeSizeAxes = Axes.X,
                            CommitOnFocusLost = true
                        },
                        presetsFlow = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Full,
                            Spacing = new Vector2(5),
                        }
                    }
                };
                numberBox.LostFocusAction = () =>
                {
                    numberBox.Current.TriggerChange();
                    numberBox.Current.Value = Current.Value?.ToString("0.##", CultureInfo.CurrentCulture) ?? string.Empty;
                };

                numberBox.Current.BindValueChanged(e =>
                {
                    updatingFromText = true;

                    try
                    {
                        if (string.IsNullOrWhiteSpace(e.NewValue))
                        {
                            Current.Value = null;
                            return;
                        }

                        if (tryParseFinite(e.NewValue, out double value))
                        {
                            double rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
                            Current.Value = rounded > 0 ? rounded : null;
                        }
                        else if (!numberBox.HasFocus)
                        {
                            numberBox.NotifyInputError();
                            numberBox.Current.Value = Current.Value?.ToString("0.##", CultureInfo.CurrentCulture) ?? string.Empty;
                        }
                        else
                        {
                            return;
                        }
                    }
                    finally
                    {
                        updatingFromText = false;
                    }
                });

                Current.BindValueChanged(e =>
                {
                    // Preserve transient input such as a trailing decimal separator while editing.
                    if (!updatingFromText && !numberBox.HasFocus)
                        numberBox.Current.Value = e.NewValue?.ToString("0.##", CultureInfo.CurrentCulture) ?? string.Empty;

                    updateAddPresetButton();
                });
            }

            [BackgroundDependencyLoader]
            private void load(OsuConfigManager config)
            {
                presetStorage = config.GetBindable<string>(OsuSetting.BPMAdjustPresets);
                presetStorage.BindValueChanged(e => loadPresets(e.NewValue), true);
            }

            private void loadPresets(string serialised)
            {
                presets.Clear();
                presets.AddRange(BPMPresetStore.Deserialise(serialised));

                updatePresetButtons();
            }

            private void updatePresetButtons()
            {
                presetsFlow.Clear();

                foreach (BPMPreset preset in presets)
                {
                    presetsFlow.Add(new BPMPresetButton(preset)
                    {
                        Action = () => Current.Value = preset.BPM,
                        OnDelete = deletePreset,
                    });
                }

                presetsFlow.Add(addPresetButton = new RoundedButton
                {
                    Width = 85,
                    Height = 25,
                    Text = "+ SAVE",
                    TooltipText = "Save the current BPM as a reusable preset",
                    Action = addCurrentPreset,
                });

                updateAddPresetButton();
            }

            private void addCurrentPreset()
            {
                if (Current.Value is not double value || value <= 0 || !double.IsFinite(value))
                    return;

                double rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);

                if (presets.All(p => p.BPM != rounded))
                    presets.Add(BPMPreset.Create(rounded));

                savePresets();
            }

            private void deletePreset(double value)
            {
                presets.RemoveAll(p => p.BPM == value);
                savePresets();
            }

            private void savePresets()
            {
                if (presetStorage == null)
                    return;

                presetStorage.Value = BPMPresetStore.Serialise(presets);
            }

            private void updateAddPresetButton()
            {
                if (addPresetButton == null)
                    return;

                addPresetButton.Enabled.Value = Current.Value is double value
                                                && value > 0
                                                && double.IsFinite(value)
                                                && presets.All(p => p.BPM != Math.Round(value, 2, MidpointRounding.AwayFromZero));
            }

            private static bool tryParseFinite(string text, out double value)
            {
                // The control accepts both decimal separators, but never treats either as
                // a thousands separator (for example, "174.5" must not become 1745 in nl-NL).
                bool parsed = double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

                return parsed && double.IsFinite(value);
            }
        }

        private partial class BPMTextBox : OutlinedTextBox
        {
            public Action? LostFocusAction;

            public BPMTextBox()
            {
                // TextInputType.Decimal strips "." on cultures which use ",". Character
                // filtering below still keeps this numeric while accepting both separators.
                InputProperties = new TextInputProperties(TextInputType.Text, false);
            }

            protected override bool CanAddCharacter(char character) =>
                char.IsAsciiDigit(character) || character == '.' || character == ',';

            public new void NotifyInputError() => base.NotifyInputError();

            protected override void OnFocusLost(FocusLostEvent e)
            {
                base.OnFocusLost(e);
                LostFocusAction?.Invoke();
            }
        }

        private partial class BPMPresetButton : RoundedButton, IHasContextMenu
        {
            public double Value { get; }
            public Action<double>? OnDelete { get; init; }

            public BPMPresetButton(BPMPreset preset)
            {
                Value = preset.BPM;
                Width = 100;
                Height = 25;
                Text = preset.Name;
                TooltipText = FormattableString.Invariant($"Set target to {preset.BPM:0.##} BPM; right-click to delete");
            }

            public MenuItem[] ContextMenuItems =>
            [
                new OsuMenuItem(CommonStrings.ButtonsDelete, MenuItemType.Destructive, () => OnDelete?.Invoke(Value))
            ];
        }
    }
}

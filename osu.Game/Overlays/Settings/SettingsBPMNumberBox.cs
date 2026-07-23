// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Screens.Select;
using osuTK;
using CommonStrings = osu.Game.Resources.Localisation.Web.CommonStrings;

namespace osu.Game.Overlays.Settings
{
    /// <summary>
    /// A positive decimal number box used for BPM mod settings.
    /// </summary>
    public partial class SettingsBPMNumberBox : SettingsItem<double?>
    {
        private const string star_rating_filter_tooltip = "Choose whether the inclusive star range uses each map's original rating (pre-mod) or its exact rating after BPM Adjust is applied (post-mod).";

        public SettingsBPMNumberBox()
        {
            // This setting contains a dropdown menu. Keep it in front of the settings
            // which follow so the menu cannot be covered by the Audio mode control.
            Depth = -1;
        }

        protected override Drawable CreateControl() => new BPMNumberControl
        {
            RelativeSizeAxes = Axes.X,
        };

        private sealed partial class BPMNumberControl : CompositeDrawable, IHasCurrentValue<double?>
        {
            private const double slider_min_bpm = 140;
            private const double slider_max_bpm = 320;

            private readonly BindableWithCurrent<double?> current = new BindableWithCurrent<double?>();
            private readonly BindableDouble sliderCurrent = new BindableDouble(180)
            {
                MinValue = slider_min_bpm,
                MaxValue = slider_max_bpm,
                Precision = 1,
            };

            private readonly List<BPMPreset> presets = new List<BPMPreset>();

            public Bindable<double?> Current
            {
                get => current.Current;
                set => current.Current = value;
            }

            private BPMTextBox numberBox = null!;
            private FillFlowContainer presetsFlow = null!;
            private StarRatingFilterModeDropdown starRatingFilterMode = null!;
            private StarRatingTextBox starRatingMinimum = null!;
            private StarRatingTextBox starRatingMaximum = null!;
            private Container starRatingRange = null!;
            private FillFlowContainer postModCalculationControls = null!;
            private RoundedButton calculateMapsButton = null!;
            private RoundedButton cancelCalculationButton = null!;
            private OsuSpriteText calculationProgressText = null!;
            private RoundedButton? addPresetButton;
            private Bindable<string>? presetStorage;
            private bool updatingFromText;
            private bool updatingSlider;

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
                        new GridContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 25,
                            ColumnDimensions =
                            [
                                new Dimension(GridSizeMode.Absolute, 35),
                                new Dimension(),
                                new Dimension(GridSizeMode.Absolute, 35),
                            ],
                            Content = new[]
                            {
                                new Drawable[]
                                {
                                    new OsuSpriteText
                                    {
                                        Text = slider_min_bpm.ToString(CultureInfo.InvariantCulture),
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                    },
                                    new RoundedSliderBar<double>
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        Anchor = Anchor.Centre,
                                        Origin = Anchor.Centre,
                                        Current = sliderCurrent,
                                        KeyboardStep = 1,
                                    },
                                    new OsuSpriteText
                                    {
                                        Text = slider_max_bpm.ToString(CultureInfo.InvariantCulture),
                                        Anchor = Anchor.CentreRight,
                                        Origin = Anchor.CentreRight,
                                    },
                                }
                            }
                        },
                        presetsFlow = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Full,
                            Spacing = new Vector2(5),
                        },
                        new StarRatingFilterLabel
                        {
                            Text = "Show only maps:",
                            Margin = new MarginPadding { Top = 5 },
                        },
                        new GridContainer
                        {
                            // Keep the dropdown menu above the calculation controls which follow
                            // this row in the fill flow.
                            Depth = -1,
                            RelativeSizeAxes = Axes.X,
                            Height = 40,
                            ColumnDimensions =
                            [
                                new Dimension(GridSizeMode.Absolute, 180),
                                new Dimension(GridSizeMode.Absolute, 5),
                                new Dimension(),
                            ],
                            Content = new[]
                            {
                                new Drawable[]
                                {
                                    starRatingFilterMode = new StarRatingFilterModeDropdown
                                    {
                                        RelativeSizeAxes = Axes.X,
                                    },
                                    Empty(),
                                    starRatingRange = new Container
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Child = new GridContainer
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            ColumnDimensions =
                                            [
                                                new Dimension(),
                                                new Dimension(GridSizeMode.Absolute, 30),
                                                new Dimension(),
                                            ],
                                            Content = new[]
                                            {
                                                new Drawable[]
                                                {
                                                    starRatingMinimum = new StarRatingTextBox
                                                    {
                                                        RelativeSizeAxes = Axes.X,
                                                        PlaceholderText = "Min",
                                                        CommitOnFocusLost = true,
                                                    },
                                                    new StarRatingFilterLabel
                                                    {
                                                        Text = "to",
                                                        Anchor = Anchor.Centre,
                                                        Origin = Anchor.Centre,
                                                    },
                                                    starRatingMaximum = new StarRatingTextBox
                                                    {
                                                        RelativeSizeAxes = Axes.X,
                                                        PlaceholderText = "Max",
                                                        CommitOnFocusLost = true,
                                                    },
                                                }
                                            }
                                        }
                                    },
                                }
                            }
                        },
                        postModCalculationControls = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 0,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(5),
                            Alpha = 0,
                            Children = new Drawable[]
                            {
                                calculateMapsButton = new RoundedButton
                                {
                                    Width = 130,
                                    Height = 30,
                                    Text = "Calculate maps",
                                    Action = () => calculationController?.RequestCalculation(),
                                },
                                cancelCalculationButton = new RoundedButton
                                {
                                    Width = 80,
                                    Height = 30,
                                    Text = "Cancel",
                                    Action = () => calculationController?.RequestCancellation(),
                                },
                                calculationProgressText = new OsuSpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                },
                            }
                        },
                    }
                };

                starRatingFilterMode.Current.BindValueChanged(e =>
                {
                    starRatingRange.FadeTo(e.NewValue == BPMStarRatingFilterMode.Disabled ? 0.35f : 1, 100);

                    bool showCalculation = e.NewValue == BPMStarRatingFilterMode.PostMod;
                    postModCalculationControls.Height = showCalculation ? 30 : 0;
                    postModCalculationControls.FadeTo(showCalculation ? 1 : 0, 100);
                }, true);
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

                sliderCurrent.BindValueChanged(e =>
                {
                    if (!updatingSlider)
                        Current.Value = e.NewValue;
                });

                Current.BindValueChanged(e =>
                {
                    // Preserve transient input such as a trailing decimal separator while editing.
                    if (!updatingFromText && !numberBox.HasFocus)
                        numberBox.Current.Value = e.NewValue?.ToString("0.##", CultureInfo.CurrentCulture) ?? string.Empty;

                    updatingSlider = true;

                    try
                    {
                        if (e.NewValue is double value && double.IsFinite(value))
                            sliderCurrent.Value = Math.Clamp(value, slider_min_bpm, slider_max_bpm);
                    }
                    finally
                    {
                        updatingSlider = false;
                    }

                    updateAddPresetButton();
                });
            }

            [Resolved(canBeNull: true)]
            private BPMStarRatingCalculationController? calculationController { get; set; }

            [BackgroundDependencyLoader]
            private void load(OsuConfigManager config)
            {
                presetStorage = config.GetBindable<string>(OsuSetting.BPMAdjustPresets);
                presetStorage.BindValueChanged(e => loadPresets(e.NewValue), true);

                starRatingFilterMode.Current = config.GetBindable<BPMStarRatingFilterMode>(OsuSetting.BPMStarRatingFilterMode);
                starRatingMinimum.Current = config.GetBindable<string>(OsuSetting.BPMStarRatingFilterMinimum);
                starRatingMaximum.Current = config.GetBindable<string>(OsuSetting.BPMStarRatingFilterMaximum);

                if (calculationController != null)
                    calculationController.ProgressChanged += updateCalculationControls;

                updateCalculationControls();
            }

            private void updateCalculationControls()
            {
                if (calculationController == null)
                {
                    calculateMapsButton.Enabled.Value = false;
                    cancelCalculationButton.Hide();
                    calculationProgressText.Text = "Available in song select";
                    return;
                }

                bool loading = calculationController.State == BPMStarRatingCalculationState.Loading;
                bool calculating = calculationController.State == BPMStarRatingCalculationState.Calculating;
                calculateMapsButton.Enabled.Value = !loading && !calculating;
                calculateMapsButton.Text = calculationController.State == BPMStarRatingCalculationState.Completed ? "Recalculate maps" : "Calculate maps";
                cancelCalculationButton.FadeTo(calculating ? 1 : 0, 100);
                cancelCalculationButton.Enabled.Value = calculating;
                cancelCalculationButton.Width = calculating ? 80 : 0;

                calculationProgressText.Text = calculationController.State switch
                {
                    BPMStarRatingCalculationState.Loading => "Loading saved maps…",
                    BPMStarRatingCalculationState.Calculating => $"{calculationController.CompletedMaps:N0} / {calculationController.TotalMaps:N0} maps",
                    BPMStarRatingCalculationState.Completed => $"{calculationController.TotalMaps:N0} maps cached",
                    _ => "Not calculated",
                };
            }

            protected override void Dispose(bool isDisposing)
            {
                if (calculationController != null)
                    calculationController.ProgressChanged -= updateCalculationControls;

                base.Dispose(isDisposing);
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
                Name = "Target BPM input";
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

        private partial class StarRatingTextBox : OutlinedTextBox, IHasTooltip
        {
            public LocalisableString TooltipText => star_rating_filter_tooltip;

            public StarRatingTextBox()
            {
                InputProperties = new TextInputProperties(TextInputType.Text, false);
            }

            protected override bool CanAddCharacter(char character) =>
                char.IsAsciiDigit(character) || character == '.' || character == ',';

            protected override void OnFocusLost(FocusLostEvent e)
            {
                base.OnFocusLost(e);

                if (string.IsNullOrWhiteSpace(Current.Value))
                {
                    Current.Value = string.Empty;
                    return;
                }

                if (!BPMStarRatingFilter.TryParseBound(Current.Value, out double value))
                {
                    NotifyInputError();
                    Current.Value = string.Empty;
                    return;
                }

                Current.Value = value.ToString("0.##", CultureInfo.CurrentCulture);
            }
        }

        private partial class StarRatingFilterModeDropdown : OsuEnumDropdown<BPMStarRatingFilterMode>, IHasTooltip
        {
            public LocalisableString TooltipText => star_rating_filter_tooltip;

            // Mod settings live inside another scroll container. Keeping this menu short
            // avoids the nested scroll handling issue described in ppy/osu-framework#4536.
            protected override DropdownMenu CreateMenu() => base.CreateMenu().With(menu => menu.MaxHeight = 100);
        }

        private partial class StarRatingFilterLabel : OsuSpriteText, IHasTooltip
        {
            public LocalisableString TooltipText => star_rating_filter_tooltip;
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

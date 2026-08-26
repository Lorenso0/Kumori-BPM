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
using osu.Framework.Platform;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Mods;
using osuTK;
using CommonStrings = osu.Game.Resources.Localisation.Web.CommonStrings;

namespace osu.Game.Rulesets.Kumori.BPM
{
    public partial class SettingsBPMControl : SettingsItem<double?>
    {
        protected override Drawable CreateControl() => new BPMControl(() => SettingSourceObject as KumoriModBPMAdjust)
        {
            RelativeSizeAxes = Axes.X,
        };

        private sealed partial class BPMControl : CompositeDrawable, IHasCurrentValue<double?>
        {
            private const double slider_min_bpm = 40;
            private const double slider_max_bpm = 400;

            private readonly BindableWithCurrent<double?> current = new BindableWithCurrent<double?>();
            private readonly BindableDouble sliderCurrent = new BindableDouble(180)
            {
                MinValue = slider_min_bpm,
                MaxValue = slider_max_bpm,
                Precision = 1,
            };

            private readonly Func<KumoriModBPMAdjust?> getMod;
            private readonly List<BPMPreset> presets = new List<BPMPreset>();
            private OsuTextBox numberBox = null!;
            private OsuSpriteText preview = null!;
            private FillFlowContainer presetsFlow = null!;
            private RoundedButton? addPresetButton;
            private Storage storage = null!;
            private bool updatingFromText;
            private bool updatingSlider;
            private bool targetPersistenceReady;
            private ScheduledDelegate? targetSaveDebounce;

            [Resolved(canBeNull: true)]
            private IBindable<WorkingBeatmap>? workingBeatmap { get; set; }

            [Resolved(canBeNull: true)]
            private IBindable<IReadOnlyList<Mod>>? selectedMods { get; set; }

            [Resolved(canBeNull: true)]
            private MusicController? musicController { get; set; }

            public Bindable<double?> Current
            {
                get => current.Current;
                set => current.Current = value;
            }

            public BPMControl(Func<KumoriModBPMAdjust?> getMod)
            {
                this.getMod = getMod;
                AutoSizeAxes = Axes.Y;

                InternalChild = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(5),
                    Children =
                    [
                        numberBox = new OsuTextBox
                        {
                            RelativeSizeAxes = Axes.X,
                            CommitOnFocusLost = true,
                            PlaceholderText = "Target BPM",
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
                                    new OsuSpriteText { Text = slider_min_bpm.ToString(CultureInfo.InvariantCulture) },
                                    new RoundedSliderBar<double>
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        Current = sliderCurrent,
                                        KeyboardStep = 1,
                                    },
                                    new OsuSpriteText
                                    {
                                        Text = slider_max_bpm.ToString(CultureInfo.InvariantCulture),
                                        Anchor = Anchor.TopRight,
                                        Origin = Anchor.TopRight,
                                    },
                                },
                            },
                        },
                        preview = new OsuSpriteText
                        {
                            RelativeSizeAxes = Axes.X,
                            Text = "Select a beatmap to preview the BPM change.",
                        },
                        presetsFlow = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Full,
                            Spacing = new Vector2(5),
                        },
                    ],
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

                        if (double.TryParse(e.NewValue.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                                && value > 0
                                && double.IsFinite(value))
                        {
                            Current.Value = Math.Round(value, 2, MidpointRounding.AwayFromZero);
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

                    updatePreview();
                    updateAddPresetButton();

                    if (workingBeatmap?.Value != null)
                        synchroniseMenuRate();

                    if (targetPersistenceReady)
                    {
                        targetSaveDebounce?.Cancel();
                        targetSaveDebounce = Scheduler.AddDelayed(() => BPMTargetStore.Save(storage, Current.Value), 250);
                    }
                });
            }

            [BackgroundDependencyLoader]
            private void load(Storage storage)
            {
                this.storage = storage;
                presets.AddRange(BPMPresetStore.Load(storage));
                updatePresetButtons();
                double? savedTarget = BPMTargetStore.Load(storage);
                targetPersistenceReady = true;

                if (Current.Value == null && savedTarget != null)
                    Current.Value = savedTarget;

                if (workingBeatmap != null)
                    workingBeatmap.BindValueChanged(_ => updateSourceBPM(), true);
                else
                    updatePreview();
            }

            private void updateSourceBPM()
            {
                KumoriModBPMAdjust? mod = getMod();
                WorkingBeatmap? beatmap = workingBeatmap?.Value;

                if (mod == null || beatmap == null)
                {
                    updatePreview();
                    return;
                }

                double sourceBPM = BPMResolver.FromBeatmapInfo(beatmap.BeatmapInfo);
                mod.SetSourceBPM(sourceBPM);

                if (Current.Value == null && BPMResolver.IsValid(mod.SourceBPM))
                    Current.Value = Math.Round(mod.SourceBPM, 2, MidpointRounding.AwayFromZero);

                synchroniseMenuRate(sourceBPM);
                updatePreview();
            }

            private void synchroniseMenuRate(double? sourceBPM = null)
            {
                KumoriModBPMAdjust? controlMod = getMod();

                // The customisation controls can survive until the end of a frame after a ruleset
                // switch. Do not let that stale control mutate or refresh the new ruleset's mods.
                if (selectedMods != null && !BPMSongSelectSynchroniser.ContainsBPMAdjust(selectedMods.Value))
                    return;

                double source = sourceBPM
                                ?? (workingBeatmap?.Value is WorkingBeatmap beatmap ? BPMResolver.FromBeatmapInfo(beatmap.BeatmapInfo) : controlMod?.SourceBPM ?? 0);

                var instances = new List<KumoriModBPMAdjust>();

                if (controlMod != null)
                    instances.Add(controlMod);

                if (selectedMods != null)
                {
                    foreach (KumoriModBPMAdjust selected in selectedMods.Value.OfType<KumoriModBPMAdjust>())
                    {
                        if (instances.All(existing => !ReferenceEquals(existing, selected)))
                            instances.Add(selected);
                    }
                }

                if (!BPMSongSelectSynchroniser.Apply(instances, source, Current.Value))
                    return;

                Scheduler.AddOnce(() =>
                {
                    // Keep official BPM/length displays in sync, but never let a stale settings
                    // control pulse selection after BPM Adjust has actually been deselected.
                    if (selectedMods != null && !BPMSongSelectSynchroniser.ContainsBPMAdjust(selectedMods.Value))
                        return;

                    if (selectedMods is Bindable<IReadOnlyList<Mod>> mutableSelectedMods)
                        mutableSelectedMods.TriggerChange();

                    musicController?.ResetTrackAdjustments();
                });
            }

            private void updatePreview()
            {
                KumoriModBPMAdjust? mod = getMod();

                if (mod == null || !BPMResolver.IsValid(mod.SourceBPM) || Current.Value == null)
                {
                    preview.Text = "Select a beatmap and enter a target BPM.";
                    return;
                }

                preview.Text = FormattableString.Invariant(
                    $"{mod.SourceBPM:0.##} → {Current.Value.Value:0.##} BPM  •  {mod.SpeedChange.Value:0.####}x");
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
                if (Current.Value is not double value || !BPMResolver.IsValid(value))
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
                BPMPresetStore.Save(storage, presets);
                updatePresetButtons();
            }

            private void updateAddPresetButton()
            {
                if (addPresetButton == null)
                    return;

                addPresetButton.Enabled.Value = Current.Value is double value
                                                && BPMResolver.IsValid(value)
                                                && presets.All(p => p.BPM != Math.Round(value, 2, MidpointRounding.AwayFromZero));
            }

            protected override void Dispose(bool isDisposing)
            {
                targetSaveDebounce?.Cancel();

                if (targetPersistenceReady)
                    BPMTargetStore.Save(storage, Current.Value);

                base.Dispose(isDisposing);
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
                new OsuMenuItem(CommonStrings.ButtonsDelete, MenuItemType.Destructive, () => OnDelete?.Invoke(Value)),
            ];
        }
    }

    internal static class BPMSongSelectSynchroniser
    {
        public static bool ContainsBPMAdjust(IEnumerable<Mod> mods) => mods.Any(mod => mod is KumoriModBPMAdjust);

        public static bool Apply(IEnumerable<KumoriModBPMAdjust> mods, double sourceBPM, double? targetBPM)
        {
            bool changed = false;

            foreach (KumoriModBPMAdjust mod in mods)
            {
                double previousRate = mod.SpeedChange.Value;
                double previousSource = mod.SourceBPM;

                if (mod.TargetBPM.Value != targetBPM)
                    mod.TargetBPM.Value = targetBPM;

                mod.SetSourceBPM(sourceBPM);
                bool modChanged = previousSource != mod.SourceBPM || previousRate != mod.SpeedChange.Value;
                changed |= modChanged;
            }

            return changed;
        }
    }
}

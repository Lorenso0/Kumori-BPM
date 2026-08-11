// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Rulesets.Mods;
using osuTK;

namespace osu.Game.Overlays.Settings
{
    /// <summary>
    /// Presents the small set of distinct BPM audio treatments and keeps named pitch styles as presets.
    /// Legacy enum values remain playable but are migrated when opened in the editor.
    /// </summary>
    public partial class SettingsBPMAudioMode : SettingsEnumDropdown<BPMAdjustAudioMode>
    {
        private readonly RoundedSliderBar<double> customPitchSlider;
        private readonly OsuSpriteText customPitchValue;
        private readonly OsuSpriteText treatmentHint;

        public SettingsBPMAudioMode()
        {
            Items =
            [
                BPMAdjustAudioMode.PreservePitch,
                BPMAdjustAudioMode.AdjustPitch,
                BPMAdjustAudioMode.Balanced,
                BPMAdjustAudioMode.Adaptive,
                BPMAdjustAudioMode.CustomPitch,
            ];

            FlowContent.Add(new OsuSpriteText
            {
                Text = "Pitch presets",
                Font = OsuFont.Default.With(size: 14),
                Margin = new MarginPadding { Top = 5 },
            });

            FlowContent.Add(new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Full,
                Spacing = new Vector2(5),
                Children = new Drawable[]
                {
                    createPresetButton("Nightcore", 12 * Math.Log2(1.5)),
                    createPresetButton("Daycore", 12 * Math.Log2(0.75)),
                    createPresetButton("Chipmunk", 12),
                    createPresetButton("Deep", -12),
                }
            });

            FlowContent.Add(new GridContainer
            {
                RelativeSizeAxes = Axes.X,
                Height = 30,
                ColumnDimensions =
                [
                    new Dimension(GridSizeMode.Absolute, 100),
                    new Dimension(),
                    new Dimension(GridSizeMode.Absolute, 65),
                ],
                Content = new[]
                {
                    new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Text = "Custom pitch",
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                        },
                        customPitchSlider = new RoundedSliderBar<double>
                        {
                            RelativeSizeAxes = Axes.X,
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            KeyboardStep = 0.1f,
                        },
                        customPitchValue = new OsuSpriteText
                        {
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.CentreRight,
                        },
                    }
                }
            });

            FlowContent.Add(treatmentHint = new OsuSpriteText
            {
                RelativeSizeAxes = Axes.X,
                Font = OsuFont.Default.With(size: 13),
            });
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var mod = (ModBPMAdjust)SettingSourceObject;
            mod.MigrateLegacyAudioSettingsForEditor();

            customPitchSlider.Current = mod.CustomPitchSemitones;
            mod.CustomPitchSemitones.BindValueChanged(e => customPitchValue.Text = $"{e.NewValue:+0.#;-0.#;0} st", true);
            Current.BindValueChanged(e =>
            {
                bool custom = e.NewValue == BPMAdjustAudioMode.CustomPitch;
                customPitchSlider.FadeTo(custom ? 1 : 0.35f, 100);
                customPitchValue.FadeTo(custom ? 1 : 0.35f, 100);
                treatmentHint.Text = getTreatmentHint(e.NewValue);
            }, true);
        }

        private RoundedButton createPresetButton(string name, double semitones) => new RoundedButton
        {
            Width = 100,
            Height = 25,
            Text = name,
            TooltipText = $"Use {name} pitch ({semitones:+0.#;-0.#;0} semitones)",
            Action = () =>
            {
                var mod = (ModBPMAdjust)SettingSourceObject;
                mod.CustomPitchSemitones.Value = semitones;
                Current.Value = BPMAdjustAudioMode.CustomPitch;
            },
        };

        private static string getTreatmentHint(BPMAdjustAudioMode mode)
        {
            switch (mode)
            {
                case BPMAdjustAudioMode.PreservePitch:
                    return "Natural pitch; large BPM changes require heavier time stretching.";

                case BPMAdjustAudioMode.AdjustPitch:
                    return "Cleanest processing; pitch changes directly with playback speed.";

                case BPMAdjustAudioMode.Balanced:
                    return "Splits the BPM change evenly between pitch and time stretching.";

                case BPMAdjustAudioMode.Adaptive:
                    return "Preserves pitch from 0.75x to 1.5x, then adds only the pitch shift needed for cleaner extremes.";

                case BPMAdjustAudioMode.CustomPitch:
                    return "Uses the selected pitch and compensates tempo to keep the target BPM exact.";

                default:
                    return string.Empty;
            }
        }
    }

    public partial class SettingsBPMBeatAccentMode : SettingsEnumDropdown<BPMAdjustBeatAccentMode>
    {
        public SettingsBPMBeatAccentMode()
        {
            Items =
            [
                BPMAdjustBeatAccentMode.Off,
                BPMAdjustBeatAccentMode.Nightcore,
                BPMAdjustBeatAccentMode.Metronome,
            ];
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var mod = (ModBPMAdjust)SettingSourceObject;
            mod.MigrateLegacyAudioSettingsForEditor();

            if (Current.Value == BPMAdjustBeatAccentMode.Automatic)
                Current.Value = BPMAdjustBeatAccentMode.Off;
        }
    }
}

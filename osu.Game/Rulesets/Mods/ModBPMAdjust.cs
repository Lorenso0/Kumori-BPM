// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Framework.Utils;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.Mods
{
    public abstract class ModBPMAdjust : ModRateAdjust, IApplicableToBeatmapInfo, IApplicableToBeatmapBeforeConversion
    {
        private const double nightcore_pitch_adjust = 1.5;
        private const double daycore_pitch_adjust = 0.75;
        private const double chipmunk_pitch_adjust = 2;
        private const double deep_pitch_adjust = 0.5;
        private const double minimum_supported_tempo = 0.05;
        private const double experimental_upper_rate = 4;

        public override string Name => "BPM Adjust";
        public override string Acronym => "BPM";
        // Use the acronym rather than borrowing DT's icon. Combined with the compact
        // numeric extended information this renders as "BPM 200" without overlap.
        public override IconUsage? Icon => null;
        public override ModType Type => ModType.Fun;
        public override LocalisableString Description => "Play every map at your chosen BPM.";
        public override bool Ranked => false;
        public sealed override bool ValidForMultiplayer => false;

        /// <summary>
        /// The desired primary BPM. A null value leaves the map at its original rate.
        /// </summary>
        [SettingSource("Target BPM", "The primary BPM to play the selected map at", SettingControlType = typeof(SettingsBPMNumberBox))]
        public Bindable<double?> TargetBPM { get; } = new Bindable<double?>();

        [SettingSource("Audio mode", "Choose how pitch responds to the BPM change")]
        public Bindable<BPMAdjustAudioMode> AudioMode { get; } = new Bindable<BPMAdjustAudioMode>(BPMAdjustAudioMode.PreservePitch);

        [SettingSource("Custom pitch", "Pitch shift in semitones used by the Custom Pitch audio mode")]
        public BindableNumber<double> CustomPitchSemitones { get; } = new BindableDouble
        {
            MinValue = -12,
            MaxValue = 12,
            Precision = 0.1,
        };

        [SettingSource("Beat accents", "Add beat-synchronised percussion independently of the pitch mode")]
        public Bindable<BPMAdjustBeatAccentMode> BeatAccents { get; } = new Bindable<BPMAdjustBeatAccentMode>(BPMAdjustBeatAccentMode.Automatic);

        [SettingSource("Scale map stats with BPM", "Scale rate-sensitive map stats such as AR and OD, like Double Time and Half Time")]
        public BindableBool ScaleMapStatsWithBPM { get; } = new BindableBool(true);

        [SettingSource("Target initialised", "Retains whether a neutral target was explicitly selected", Visible = false)]
        public BindableBool TargetInitialised { get; } = new BindableBool();

        public override BindableNumber<double> SpeedChange { get; } = new BindableDouble(1)
        {
            MinValue = double.Epsilon,
            MaxValue = double.MaxValue,
        };

        /// <summary>
        /// The primary BPM of the selected or materialised beatmap.
        /// </summary>
        public double SourceBPM { get; private set; }

        public bool AudioFallbackActive { get; private set; }

        public bool RateWasLimited { get; private set; }

        private readonly BindableNumber<double> tempoAdjust = new BindableDouble(1);
        private readonly BindableNumber<double> frequencyAdjust = new BindableDouble(1);

        protected ModBPMAdjust()
        {
            TargetBPM.BindValueChanged(_ =>
            {
                TargetInitialised.Value = true;
                updateSpeedChange();
            });
            SpeedChange.BindValueChanged(_ => updateAudioAdjustments());
            AudioMode.BindValueChanged(_ => updateAudioAdjustments(), true);
            CustomPitchSemitones.BindValueChanged(_ => updateAudioAdjustments());
        }

        public void ApplyToBeatmapInfo(IBeatmapInfo beatmapInfo)
        {
            setSourceBPM(BPMResolver.FromBeatmapInfo(beatmapInfo));

            // First activation should be neutral. Once populated, this setting intentionally
            // persists as the user changes the selected map.
            if (!TargetInitialised.Value && TargetBPM.Value == null && BPMResolver.IsValid(SourceBPM))
                TargetBPM.Value = Math.Round(SourceBPM, 2, MidpointRounding.AwayFromZero);
        }

        public void ApplyToBeatmapBeforeConversion(IBeatmap beatmap)
        {
            setSourceBPM(BPMResolver.FromBeatmap(beatmap));

            if (!TargetInitialised.Value && TargetBPM.Value == null && BPMResolver.IsValid(SourceBPM))
                TargetBPM.Value = Math.Round(SourceBPM, 2, MidpointRounding.AwayFromZero);
        }

        public override void ApplyToTrack(IAdjustableAudioComponent track)
        {
            // Both adjustments stay attached so changing audio mode in song select is instantaneous.
            // An adjustment value of 1 has no effect.
            track.AddAdjustment(AdjustableProperty.Frequency, frequencyAdjust);
            track.AddAdjustment(AdjustableProperty.Tempo, tempoAdjust);
        }

        public override IEnumerable<(LocalisableString setting, LocalisableString value)> SettingDescription
        {
            get
            {
                if (TargetBPM.Value != null)
                    yield return ("Target BPM", FormattableString.Invariant($"{TargetBPM.Value.Value:0.##} BPM"));

                yield return ("Speed change", FormattableString.Invariant($"{SpeedChange.Value:0.####}x"));

                if (!AudioMode.IsDefault)
                    yield return ("Audio mode", getAudioModeName(AudioMode.Value));

                if (AudioMode.Value == BPMAdjustAudioMode.CustomPitch)
                    yield return ("Pitch shift", FormattableString.Invariant($"{CustomPitchSemitones.Value:+0.#;-0.#;0} semitones"));

                if (!BeatAccents.IsDefault)
                    yield return ("Beat accents", BeatAccents.Value.ToString());

                if (!ScaleMapStatsWithBPM.Value)
                    yield return ("Map stats", "Unscaled");

                if (AudioFallbackActive)
                    yield return ("Audio warning", "Frequency fallback active");

                if (RateWasLimited)
                    yield return ("Rate warning", "Limited to the nearest representable rate");
                else if (SpeedChange.Value < minimum_supported_tempo || SpeedChange.Value > experimental_upper_rate)
                    yield return ("Rate warning", "Extreme rate; audio may be unstable");
            }
        }

        public override string ExtendedIconInformation =>
            TargetBPM.Value == null ? string.Empty : FormattableString.Invariant($"{TargetBPM.Value.Value:0.##}");

        public override Mod DeepClone()
        {
            var clone = (ModBPMAdjust)base.DeepClone();

            // SourceBPM is derived from the selected beatmap rather than being a serialised setting.
            // Difficulty calculation clones mods after obtaining a cached playable beatmap, so that
            // clone cannot resolve the source BPM from the beatmap again. Preserve it here to ensure
            // the clone retains the same clock rate as the selected mod.
            clone.setSourceBPM(SourceBPM);

            return clone;
        }

        private void setSourceBPM(double bpm)
        {
            SourceBPM = BPMResolver.IsValid(bpm) ? bpm : 0;
            updateSpeedChange();
        }

        private void updateSpeedChange()
        {
            RateWasLimited = false;

            if (!BPMResolver.IsValid(SourceBPM) || TargetBPM.Value == null || !BPMResolver.IsValid(TargetBPM.Value.Value))
            {
                SpeedChange.Value = 1;
                return;
            }

            double rate = TargetBPM.Value.Value / SourceBPM;

            if (rate == 0)
            {
                rate = double.Epsilon;
                RateWasLimited = true;
            }
            else if (double.IsPositiveInfinity(rate))
            {
                rate = double.MaxValue;
                RateWasLimited = true;
            }

            SpeedChange.Value = rate;
        }

        private void updateAudioAdjustments()
        {
            AudioFallbackActive = false;

            double frequency;
            double tempo;

            switch (AudioMode.Value)
            {
                case BPMAdjustAudioMode.PreservePitch:
                case BPMAdjustAudioMode.PreservePitchWithAccents:
                    frequency = 1;
                    tempo = SpeedChange.Value;
                    break;

                case BPMAdjustAudioMode.AdjustPitch:
                    frequency = SpeedChange.Value;
                    tempo = 1;
                    break;

                case BPMAdjustAudioMode.Nightcore:
                case BPMAdjustAudioMode.NightcorePitchOnly:
                    frequency = nightcore_pitch_adjust;
                    tempo = SpeedChange.Value / nightcore_pitch_adjust;
                    break;

                case BPMAdjustAudioMode.Daycore:
                    frequency = daycore_pitch_adjust;
                    tempo = SpeedChange.Value / daycore_pitch_adjust;
                    break;

                case BPMAdjustAudioMode.Balanced:
                    frequency = Math.Sqrt(SpeedChange.Value);
                    tempo = frequency;
                    break;

                case BPMAdjustAudioMode.CustomPitch:
                    frequency = Math.Pow(2, CustomPitchSemitones.Value / 12);
                    tempo = SpeedChange.Value / frequency;
                    break;

                case BPMAdjustAudioMode.Chipmunk:
                    frequency = chipmunk_pitch_adjust;
                    tempo = SpeedChange.Value / chipmunk_pitch_adjust;
                    break;

                case BPMAdjustAudioMode.Deep:
                    frequency = deep_pitch_adjust;
                    tempo = SpeedChange.Value / deep_pitch_adjust;
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            // TrackBass cannot accept a tempo below 0.05. Preserve the requested
            // combined playback rate by moving the unsupported portion to frequency.
            // This intentionally avoids clamping the gameplay rate.
            if (tempo < minimum_supported_tempo)
            {
                frequency = SpeedChange.Value / minimum_supported_tempo;
                tempo = minimum_supported_tempo;
                AudioFallbackActive = true;
            }

            frequencyAdjust.Value = frequency;
            tempoAdjust.Value = tempo;
        }

        private static string getAudioModeName(BPMAdjustAudioMode mode)
        {
            switch (mode)
            {
                case BPMAdjustAudioMode.PreservePitch:
                    return "Preserve Pitch";

                case BPMAdjustAudioMode.AdjustPitch:
                    return "Adjust Pitch";

                case BPMAdjustAudioMode.Nightcore:
                    return "Nightcore";

                case BPMAdjustAudioMode.Daycore:
                    return "Daycore";

                case BPMAdjustAudioMode.Balanced:
                    return "Balanced";

                case BPMAdjustAudioMode.CustomPitch:
                    return "Custom Pitch";

                case BPMAdjustAudioMode.Chipmunk:
                    return "Chipmunk";

                case BPMAdjustAudioMode.Deep:
                    return "Deep";

                case BPMAdjustAudioMode.NightcorePitchOnly:
                    return "Nightcore Pitch Only";

                case BPMAdjustAudioMode.PreservePitchWithAccents:
                    return "Preserve Pitch + Accents";

                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
        }

        public override void ResetSettingsToDefaults()
        {
            base.ResetSettingsToDefaults();
            TargetInitialised.Value = false;
        }

        protected void CompensateApproachRate(BeatmapDifficulty difficulty, DifficultyRange preemptRange)
        {
            if (ScaleMapStatsWithBPM.Value)
                return;

            double preempt = IBeatmapDifficultyInfo.DifficultyRange(difficulty.ApproachRate, preemptRange) * SpeedChange.Value;
            difficulty.ApproachRate = (float)IBeatmapDifficultyInfo.InverseDifficultyRange(preempt, preemptRange);
        }

        protected void CompensateOverallDifficulty(BeatmapDifficulty difficulty, DifficultyRange greatWindowRange)
        {
            if (ScaleMapStatsWithBPM.Value)
                return;

            double greatHitWindow = IBeatmapDifficultyInfo.DifficultyRange(difficulty.OverallDifficulty, greatWindowRange) * SpeedChange.Value;
            difficulty.OverallDifficulty = (float)IBeatmapDifficultyInfo.InverseDifficultyRange(greatHitWindow, greatWindowRange);
        }
    }

    public abstract partial class ModBPMAdjust<TObject> : ModBPMAdjust, IApplicableToDrawableRuleset<TObject>
        where TObject : HitObject
    {
        public void ApplyToDrawableRuleset(DrawableRuleset<TObject> drawableRuleset)
        {
            BPMAdjustBeatAccentMode accentMode = BeatAccents.Value;

            if (accentMode == BPMAdjustBeatAccentMode.Automatic)
            {
                accentMode = AudioMode.Value == BPMAdjustAudioMode.Nightcore
                             || AudioMode.Value == BPMAdjustAudioMode.PreservePitchWithAccents
                    ? BPMAdjustBeatAccentMode.Nightcore
                    : BPMAdjustBeatAccentMode.Off;
            }

            switch (accentMode)
            {
                case BPMAdjustBeatAccentMode.Off:
                    return;

                case BPMAdjustBeatAccentMode.Nightcore:
                    bool playHats = Precision.AlmostEquals(drawableRuleset.Beatmap.Difficulty.SliderTickRate % 2, 0);
                    drawableRuleset.Overlays.Add(new ModNightcore<TObject>.NightcoreBeatContainer(playHats));
                    return;

                case BPMAdjustBeatAccentMode.Metronome:
                    drawableRuleset.Overlays.Add(new BPMMetronomeBeatContainer());
                    return;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    public enum BPMAdjustAudioMode
    {
        PreservePitch,
        AdjustPitch,
        Nightcore,
        Daycore,
        Balanced,
        CustomPitch,
        Chipmunk,
        Deep,
        NightcorePitchOnly,
        PreservePitchWithAccents,
    }

    public enum BPMAdjustBeatAccentMode
    {
        Automatic,
        Off,
        Nightcore,
        Metronome,
    }
}

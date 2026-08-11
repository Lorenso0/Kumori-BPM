// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        private const double recommended_minimum_tempo = 0.75;
        private const double recommended_maximum_tempo = 1.5;
        private const double extreme_minimum_audio_adjustment = 0.25;
        private const double extreme_maximum_audio_adjustment = 4;
        private const double large_pitch_shift_semitones = 8;

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

        [SettingSource("Audio treatment", "Choose how pitch responds to the BPM change", SettingControlType = typeof(SettingsBPMAudioMode))]
        public Bindable<BPMAdjustAudioMode> AudioMode { get; } = new Bindable<BPMAdjustAudioMode>(BPMAdjustAudioMode.PreservePitch);

        [SettingSource("Custom pitch", "Pitch shift in semitones used by the Custom Pitch audio treatment", Visible = false)]
        public BindableNumber<double> CustomPitchSemitones { get; } = new BindableDouble
        {
            MinValue = -12,
            MaxValue = 12,
            // Fine enough to preserve the exact pitch of legacy fixed-frequency presets.
            // The editor still exposes 0.1-semitone keyboard steps and a compact display.
            Precision = 0.0001,
        };

        [SettingSource("Beat accents", "Add beat-synchronised percussion independently of the pitch mode", SettingControlType = typeof(SettingsBPMBeatAccentMode))]
        public Bindable<BPMAdjustBeatAccentMode> BeatAccents { get; } = new Bindable<BPMAdjustBeatAccentMode>(BPMAdjustBeatAccentMode.Automatic);

        [SettingSource("Hitsound pitch", "Choose whether gameplay hitsounds follow the playback rate, music pitch, or keep their original pitch")]
        public Bindable<BPMAdjustHitsoundMode> HitsoundMode { get; } = new Bindable<BPMAdjustHitsoundMode>(BPMAdjustHitsoundMode.FollowPlaybackRate);

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

        /// <summary>
        /// Returns the currently effective audio and timing values for display in configuration UI.
        /// </summary>
        public BPMAdjustPreview CreatePreview(double originalLength = 0)
        {
            double adjustedLength = originalLength > 0 && double.IsFinite(originalLength)
                ? originalLength / SpeedChange.Value
                : 0;

            double pitchSemitones = frequencyAdjust.Value > 0
                ? 12 * Math.Log2(frequencyAdjust.Value)
                : 0;

            bool heavyTimeStretching = tempoAdjust.Value < recommended_minimum_tempo || tempoAdjust.Value > recommended_maximum_tempo;
            bool largePitchShift = Math.Abs(pitchSemitones) > large_pitch_shift_semitones;
            bool extremeProcessing = frequencyAdjust.Value < extreme_minimum_audio_adjustment
                                     || frequencyAdjust.Value > extreme_maximum_audio_adjustment
                                     || tempoAdjust.Value < extreme_minimum_audio_adjustment
                                     || tempoAdjust.Value > extreme_maximum_audio_adjustment;

            return new BPMAdjustPreview(
                SourceBPM,
                TargetBPM.Value,
                SpeedChange.Value,
                frequencyAdjust.Value,
                tempoAdjust.Value,
                pitchSemitones,
                originalLength,
                adjustedLength,
                ScaleMapStatsWithBPM.Value,
                AudioFallbackActive,
                RateWasLimited || extremeProcessing || SpeedChange.Value > experimental_upper_rate,
                heavyTimeStretching,
                largePitchShift);
        }

        private readonly BindableNumber<double> tempoAdjust = new BindableDouble(1);
        private readonly BindableNumber<double> frequencyAdjust = new BindableDouble(1);

        private IAdjustableAudioComponent? track;
        private bool frequencyAdjustmentAttached;
        private bool tempoAdjustmentAttached;

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
            if (ReferenceEquals(this.track, track))
                return;

            detachAudioAdjustments();

            this.track = track;
            synchroniseAudioAdjustments();
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

                if (!HitsoundMode.IsDefault)
                    yield return ("Hitsound pitch", getHitsoundModeName(HitsoundMode.Value));

                if (!ScaleMapStatsWithBPM.Value)
                    yield return ("Map stats", "Unscaled");

                if (AudioFallbackActive)
                    yield return ("Audio warning", "Frequency fallback active");

                if (RateWasLimited)
                    yield return ("Rate warning", "Limited to the nearest representable rate");
                else
                {
                    BPMAdjustPreview preview = CreatePreview();

                    if (preview.ExtremeRate)
                        yield return ("Audio warning", "Extreme processing; audio may be unstable");
                    else if (preview.HeavyTimeStretching)
                        yield return ("Audio warning", "Heavy time stretching may reduce quality");
                    else if (preview.LargePitchShift)
                        yield return ("Audio warning", "Large pitch shift may reduce quality");
                }
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

        public override void ApplyToSample(IAdjustableAudioComponent sample)
        {
            switch (HitsoundMode.Value)
            {
                case BPMAdjustHitsoundMode.FollowPlaybackRate:
                    sample.AddAdjustment(AdjustableProperty.Frequency, SpeedChange);
                    break;

                case BPMAdjustHitsoundMode.FollowMusicPitch:
                    sample.AddAdjustment(AdjustableProperty.Frequency, frequencyAdjust);
                    break;

                case BPMAdjustHitsoundMode.PreservePitch:
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        /// Converts treatments now represented by custom-pitch presets into the simplified editor controls.
        /// Playback continues to understand the legacy values for old scores and replays which bypass the editor.
        /// </summary>
        public void MigrateLegacyAudioSettingsForEditor()
        {
            switch (AudioMode.Value)
            {
                case BPMAdjustAudioMode.Nightcore:
                    migrateCustomPitch(nightcore_pitch_adjust, BPMAdjustBeatAccentMode.Nightcore);
                    break;

                case BPMAdjustAudioMode.NightcorePitchOnly:
                    migrateCustomPitch(nightcore_pitch_adjust, BPMAdjustBeatAccentMode.Off);
                    break;

                case BPMAdjustAudioMode.Daycore:
                    migrateCustomPitch(daycore_pitch_adjust);
                    break;

                case BPMAdjustAudioMode.Chipmunk:
                    migrateCustomPitch(chipmunk_pitch_adjust);
                    break;

                case BPMAdjustAudioMode.Deep:
                    migrateCustomPitch(deep_pitch_adjust);
                    break;

                case BPMAdjustAudioMode.PreservePitchWithAccents:
                    if (BeatAccents.Value == BPMAdjustBeatAccentMode.Automatic)
                        BeatAccents.Value = BPMAdjustBeatAccentMode.Nightcore;

                    AudioMode.Value = BPMAdjustAudioMode.PreservePitch;
                    break;
            }

            void migrateCustomPitch(double frequency, BPMAdjustBeatAccentMode? legacyAccents = null)
            {
                CustomPitchSemitones.Value = 12 * Math.Log2(frequency);

                if (BeatAccents.Value == BPMAdjustBeatAccentMode.Automatic)
                    BeatAccents.Value = legacyAccents ?? BPMAdjustBeatAccentMode.Off;

                AudioMode.Value = BPMAdjustAudioMode.CustomPitch;
            }
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

                case BPMAdjustAudioMode.Adaptive:
                    tempo = Math.Clamp(SpeedChange.Value, recommended_minimum_tempo, recommended_maximum_tempo);
                    frequency = SpeedChange.Value / tempo;
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

            bool requiresFrequencyAdjustment = !Precision.AlmostEquals(frequency, 1);
            bool requiresTempoAdjustment = !Precision.AlmostEquals(tempo, 1);
            bool bothAdjustmentsChanging = !Precision.AlmostEquals(frequencyAdjust.Value, frequency)
                                           && !Precision.AlmostEquals(tempoAdjust.Value, tempo);

            // Remove processors which are no longer required before updating their values.
            // Keeping a neutral processor attached differs from lazer's built-in rate mods and,
            // more importantly, briefly applies both the old and new rate during a mode switch.
            // That transient state can underrun the low-latency WASAPI path and produce a crackle.
            if (bothAdjustmentsChanging)
            {
                detachFrequencyAdjustment();
                detachTempoAdjustment();
            }

            if (!requiresFrequencyAdjustment)
                detachFrequencyAdjustment();

            if (!requiresTempoAdjustment)
                detachTempoAdjustment();

            frequencyAdjust.Value = frequency;
            tempoAdjust.Value = tempo;

            synchroniseAudioAdjustments();
        }

        private void synchroniseAudioAdjustments()
        {
            if (track == null)
                return;

            if (!frequencyAdjustmentAttached && !Precision.AlmostEquals(frequencyAdjust.Value, 1))
            {
                track.AddAdjustment(AdjustableProperty.Frequency, frequencyAdjust);
                frequencyAdjustmentAttached = true;
            }

            if (!tempoAdjustmentAttached && !Precision.AlmostEquals(tempoAdjust.Value, 1))
            {
                track.AddAdjustment(AdjustableProperty.Tempo, tempoAdjust);
                tempoAdjustmentAttached = true;
            }
        }

        private void detachAudioAdjustments()
        {
            detachFrequencyAdjustment();
            detachTempoAdjustment();
            track = null;
        }

        private void detachFrequencyAdjustment()
        {
            if (track == null || !frequencyAdjustmentAttached)
                return;

            track.RemoveAdjustment(AdjustableProperty.Frequency, frequencyAdjust);
            frequencyAdjustmentAttached = false;
        }

        private void detachTempoAdjustment()
        {
            if (track == null || !tempoAdjustmentAttached)
                return;

            track.RemoveAdjustment(AdjustableProperty.Tempo, tempoAdjust);
            tempoAdjustmentAttached = false;
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

                case BPMAdjustAudioMode.Adaptive:
                    return "Adaptive";

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

        private static string getHitsoundModeName(BPMAdjustHitsoundMode mode)
        {
            switch (mode)
            {
                case BPMAdjustHitsoundMode.FollowPlaybackRate:
                    return "Follow playback rate";

                case BPMAdjustHitsoundMode.FollowMusicPitch:
                    return "Follow music pitch";

                case BPMAdjustHitsoundMode.PreservePitch:
                    return "Preserve pitch";

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

    public readonly record struct BPMAdjustPreview(
        double SourceBPM,
        double? TargetBPM,
        double Rate,
        double FrequencyAdjustment,
        double TempoAdjustment,
        double PitchSemitones,
        double OriginalLength,
        double AdjustedLength,
        bool ScaleMapStats,
        bool AudioFallbackActive,
        bool ExtremeRate,
        bool HeavyTimeStretching,
        bool LargePitchShift);

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
        [Description("Preserve Pitch")]
        PreservePitch,

        [Description("Adjust Pitch")]
        AdjustPitch,

        Nightcore,
        Daycore,

        Balanced,

        [Description("Custom Pitch")]
        CustomPitch,

        Chipmunk,
        Deep,

        [Description("Nightcore Pitch Only")]
        NightcorePitchOnly,

        [Description("Preserve Pitch + Accents")]
        PreservePitchWithAccents,

        Adaptive,
    }

    public enum BPMAdjustBeatAccentMode
    {
        Automatic,
        Off,
        Nightcore,
        Metronome,
    }

    public enum BPMAdjustHitsoundMode
    {
        [Description("Follow Playback Rate")]
        FollowPlaybackRate,

        [Description("Follow Music Pitch")]
        FollowMusicPitch,

        [Description("Preserve Pitch")]
        PreservePitch,
    }
}

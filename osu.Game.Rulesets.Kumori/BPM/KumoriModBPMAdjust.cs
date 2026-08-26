// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Framework.Utils;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Scoring;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.Kumori.BPM
{
    public class KumoriModBPMAdjust : Mod, IApplicableToRate, IApplicableAfterBeatmapConversion, IApplicableToBeatmapProcessor,
                                      IApplicableToDrawableRuleset<OsuHitObject>
    {
        private const double minimum_supported_tempo = 0.05;
        private const double recommended_minimum_tempo = 0.75;
        private const double recommended_maximum_tempo = 1.5;

        internal const string SPECTATOR_MARKER_SETTING = "kumori_spectator";

        public override string Name => "BPM Adjust";
        public override string Acronym => "BPM";
        public override IconUsage? Icon => null;
        public override ModType Type => ModType.DifficultyIncrease;
        public override LocalisableString Description => "Play every map at your chosen BPM.";
        public override bool Ranked => false;
        public override bool ValidForMultiplayer => false;
        public override bool ValidForFreestyleAsRequiredMod => true;
        public override bool ValidForMultiplayerAsFreeMod => false;

        [SettingSource("Global star-range song filter", "Filter the whole song list by original or exact post-mod stars", SettingControlType = typeof(SettingsBPMStarFilterControl))]
        public Bindable<KumoriStarRatingFilterMode> StarFilterMode { get; } = new Bindable<KumoriStarRatingFilterMode>();

        public Bindable<string> StarFilterMinimum { get; } = new Bindable<string>(string.Empty);

        public Bindable<string> StarFilterMaximum { get; } = new Bindable<string>(string.Empty);

        [SettingSource("Target BPM", "The primary BPM to play the selected map at", SettingControlType = typeof(SettingsBPMControl))]
        public Bindable<double?> TargetBPM { get; } = new Bindable<double?>();

        [SettingSource("Audio treatment", "Choose how pitch responds to the BPM change", SettingControlType = typeof(SettingsBPMAudioMode))]
        public Bindable<BPMAdjustAudioMode> AudioMode { get; } = new Bindable<BPMAdjustAudioMode>(BPMAdjustAudioMode.PreservePitch);

        [SettingSource("Custom pitch", "Pitch shift in semitones used by Custom Pitch")]
        public BindableNumber<double> CustomPitchSemitones { get; } = new BindableDouble
        {
            MinValue = -12,
            MaxValue = 12,
            Precision = 0.1,
        };

        [SettingSource("Beat accents", "Add beat-synchronised percussion independently of pitch")]
        public Bindable<BPMAdjustBeatAccentMode> BeatAccents { get; } = new Bindable<BPMAdjustBeatAccentMode>(BPMAdjustBeatAccentMode.Off);

        [SettingSource("Hitsound pitch", "Choose whether hitsounds follow playback, music pitch, or remain unchanged")]
        public Bindable<BPMAdjustHitsoundMode> HitsoundMode { get; } = new Bindable<BPMAdjustHitsoundMode>(BPMAdjustHitsoundMode.FollowPlaybackRate);

        [SettingSource("Scale map stats with BPM", "Scale rate-sensitive map stats like Double Time and Half Time")]
        public BindableBool ScaleMapStatsWithBPM { get; } = new BindableBool(true);

        // osu!'s spectator server only accepts the four built-in numeric mode IDs. This hidden,
        // non-default setting survives APIMod transport and lets a viewer with Kumori installed
        // restore the custom ruleset locally before spectator gameplay is constructed.
        [SettingSource("", "", SettingControlType = typeof(SettingsKumoriSpectatorMarker))]
        public BindableBool KumoriSpectator { get; } = new BindableBool();

        public BindableNumber<double> SpeedChange { get; } = new BindableDouble(1)
        {
            MinValue = double.Epsilon,
            MaxValue = double.MaxValue,
        };

        public double SourceBPM { get; private set; }

        public bool AudioFallbackActive { get; private set; }

        private readonly BindableNumber<double> tempoAdjust = new BindableDouble(1);
        private readonly BindableNumber<double> frequencyAdjust = new BindableDouble(1);

        private IAdjustableAudioComponent? track;
        private bool frequencyAdjustmentAttached;
        private bool tempoAdjustmentAttached;

        public KumoriModBPMAdjust()
        {
            KumoriSpectator.Value = true;
            TargetBPM.BindValueChanged(_ => updateSpeedChange());
            SpeedChange.BindValueChanged(_ => updateAudioAdjustments());
            AudioMode.BindValueChanged(_ => updateAudioAdjustments(), true);
            CustomPitchSemitones.BindValueChanged(_ => updateAudioAdjustments());
        }

        public void ApplyToBeatmap(IBeatmap beatmap)
        {
            SetSourceBPM(BPMResolver.FromBeatmap(beatmap));
        }

        public void ApplyToBeatmapProcessor(IBeatmapProcessor beatmapProcessor) => ApplyDifficultyCompensation(beatmapProcessor.Beatmap.Difficulty);

        internal void ApplyDifficultyCompensation(BeatmapDifficulty difficulty)
        {
            if (ScaleMapStatsWithBPM.Value)
                return;

            double preempt = IBeatmapDifficultyInfo.DifficultyRange(difficulty.ApproachRate, OsuHitObject.PREEMPT_RANGE) * SpeedChange.Value;
            difficulty.ApproachRate = (float)IBeatmapDifficultyInfo.InverseDifficultyRange(preempt, OsuHitObject.PREEMPT_RANGE);

            double greatHitWindow = IBeatmapDifficultyInfo.DifficultyRange(difficulty.OverallDifficulty, OsuHitWindows.GREAT_WINDOW_RANGE) * SpeedChange.Value;
            difficulty.OverallDifficulty = (float)IBeatmapDifficultyInfo.InverseDifficultyRange(greatHitWindow, OsuHitWindows.GREAT_WINDOW_RANGE);
        }

        public void SetSourceBPM(double bpm)
        {
            SourceBPM = BPMResolver.IsValid(bpm) ? bpm : 0;
            updateSpeedChange();
        }

        public void ApplyToTrack(IAdjustableAudioComponent track)
        {
            if (ReferenceEquals(this.track, track))
                return;

            detachAudioAdjustments();
            this.track = track;
            synchroniseAudioAdjustments();
        }

        public void ApplyToSample(IAdjustableAudioComponent sample)
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

        public void ApplyToDrawableRuleset(DrawableRuleset<OsuHitObject> drawableRuleset)
        {
            switch (BeatAccents.Value)
            {
                case BPMAdjustBeatAccentMode.Off:
                    return;

                case BPMAdjustBeatAccentMode.Nightcore:
                    bool playHats = Precision.AlmostEquals(drawableRuleset.Beatmap.Difficulty.SliderTickRate % 2, 0);
                    drawableRuleset.Overlays.Add(new ModNightcore<OsuHitObject>.NightcoreBeatContainer(playHats));
                    return;

                case BPMAdjustBeatAccentMode.Metronome:
                    drawableRuleset.Overlays.Add(new BPMMetronomeBeatContainer());
                    return;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public override IEnumerable<(LocalisableString setting, LocalisableString value)> SettingDescription
        {
            get
            {
                if (TargetBPM.Value != null)
                    yield return ("Target BPM", FormattableString.Invariant($"{TargetBPM.Value.Value:0.##} BPM"));

                yield return ("Speed change", FormattableString.Invariant($"{SpeedChange.Value:0.####}x"));

                if (!AudioMode.IsDefault)
                    yield return ("Audio mode", AudioMode.Value.GetDescription());

                if (AudioMode.Value == BPMAdjustAudioMode.CustomPitch)
                    yield return ("Pitch shift", FormattableString.Invariant($"{CustomPitchSemitones.Value:+0.#;-0.#;0} semitones"));

                if (!BeatAccents.IsDefault)
                    yield return ("Beat accents", BeatAccents.Value.ToString());

                if (!HitsoundMode.IsDefault)
                    yield return ("Hitsound pitch", HitsoundMode.Value.GetDescription());

                if (!ScaleMapStatsWithBPM.Value)
                    yield return ("Map stats", "Unscaled");

                if (AudioFallbackActive)
                    yield return ("Audio warning", "Frequency fallback active");
            }
        }

        public override string ExtendedIconInformation => TargetBPM.Value == null
            ? string.Empty
            : FormattableString.Invariant($"{TargetBPM.Value.Value:0.##}");

        public double ApplyToRate(double time, double rate) => rate * SpeedChange.Value;

        public override Mod DeepClone()
        {
            var clone = (KumoriModBPMAdjust)base.DeepClone();
            clone.SetSourceBPM(SourceBPM);
            return clone;
        }

        public override Type[] IncompatibleMods => [typeof(ModTimeRamp), typeof(ModAdaptiveSpeed), typeof(ModRateAdjust)];

        private void updateSpeedChange()
        {
            if (!BPMResolver.IsValid(SourceBPM) || TargetBPM.Value == null || !BPMResolver.IsValid(TargetBPM.Value.Value))
            {
                SpeedChange.Value = 1;
                return;
            }

            double rate = TargetBPM.Value.Value / SourceBPM;
            SpeedChange.Value = double.IsPositiveInfinity(rate) ? double.MaxValue : Math.Max(double.Epsilon, rate);
        }

        private void updateAudioAdjustments()
        {
            AudioFallbackActive = false;

            double frequency;
            double tempo;

            switch (AudioMode.Value)
            {
                case BPMAdjustAudioMode.PreservePitch:
                    frequency = 1;
                    tempo = SpeedChange.Value;
                    break;

                case BPMAdjustAudioMode.AdjustPitch:
                    frequency = SpeedChange.Value;
                    tempo = 1;
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

                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (tempo < minimum_supported_tempo)
            {
                frequency = SpeedChange.Value / minimum_supported_tempo;
                tempo = minimum_supported_tempo;
                AudioFallbackActive = true;
            }

            bool bothChanging = !Precision.AlmostEquals(frequencyAdjust.Value, frequency)
                                && !Precision.AlmostEquals(tempoAdjust.Value, tempo);

            if (bothChanging)
            {
                detachFrequencyAdjustment();
                detachTempoAdjustment();
            }

            if (Precision.AlmostEquals(frequency, 1))
                detachFrequencyAdjustment();

            if (Precision.AlmostEquals(tempo, 1))
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
    }

    public enum BPMAdjustAudioMode
    {
        [Description("Preserve Pitch")]
        PreservePitch,

        [Description("Adjust Pitch")]
        AdjustPitch,

        Balanced,

        Adaptive,

        [Description("Custom Pitch")]
        CustomPitch,
    }

    public enum BPMAdjustBeatAccentMode
    {
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

    /// <summary>
    /// Keeps the transport-only spectator marker out of the mod settings layout.
    /// </summary>
    public partial class SettingsKumoriSpectatorMarker : SettingsCheckbox
    {
        public SettingsKumoriSpectatorMarker()
        {
            Alpha = 0;
            CanBeShown.Value = false;
        }
    }
}

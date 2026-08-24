// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics;
using osu.Game.Audio;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Graphics.Containers;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.Kumori.BPM
{
    public partial class BPMMetronomeBeatContainer : BeatSyncedContainer
    {
        private PausableSkinnableSound? beatSample;
        private PausableSkinnableSound? downbeatSample;
        private int lastBeat = int.MinValue;

        public BPMMetronomeBeatContainer()
        {
            Divisor = 1;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren =
            [
                beatSample = new PausableSkinnableSound(new SampleInfo("Gameplay/nightcore-hat")),
                downbeatSample = new PausableSkinnableSound(new SampleInfo("Gameplay/nightcore-finish")),
            ];
        }

        protected override void OnNewBeat(int beatIndex, TimingControlPoint timingPoint, EffectControlPoint effectPoint, ChannelAmplitudes amplitudes)
        {
            base.OnNewBeat(beatIndex, timingPoint, effectPoint, amplitudes);

            if (!IsBeatSyncedWithTrack || lastBeat == beatIndex)
                return;

            lastBeat = beatIndex;
            int beatsPerBar = timingPoint.TimeSignature.Numerator;
            int beatInBar = ((beatIndex % beatsPerBar) + beatsPerBar) % beatsPerBar;

            if (beatInBar == 0)
                downbeatSample?.Play();
            else
                beatSample?.Play();
        }
    }
}

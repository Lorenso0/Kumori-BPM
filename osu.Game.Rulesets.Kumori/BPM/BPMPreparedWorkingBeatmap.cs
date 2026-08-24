// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Skinning;
using osu.Game.Storyboards;

namespace osu.Game.Rulesets.Kumori.BPM
{
    /// <summary>
    /// Rebinds BPM Adjust to the beatmap being calculated. Official rate mods have one rate for
    /// every map, while 1:1 BPM has a different rate per map; without this wrapper song-list panels
    /// would all use the rate of whichever map happened to be selected.
    /// </summary>
    internal sealed class BPMPreparedWorkingBeatmap : IWorkingBeatmap
    {
        private readonly IWorkingBeatmap inner;

        public BPMPreparedWorkingBeatmap(IWorkingBeatmap inner)
        {
            this.inner = inner;
        }

        public IBeatmapInfo BeatmapInfo => inner.BeatmapInfo;
        public bool BeatmapLoaded => inner.BeatmapLoaded;
        public bool TrackLoaded => inner.TrackLoaded;
        public IBeatmap Beatmap => inner.Beatmap;
        public Waveform Waveform => inner.Waveform;
        public Storyboard Storyboard => inner.Storyboard;
        public ISkin Skin => inner.Skin;
        public Track Track => inner.Track;

        public Texture GetBackground() => inner.GetBackground();
        public Texture GetPanelBackground() => inner.GetPanelBackground();

        public IBeatmap GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList<Mod>? mods = null)
        {
            prepareMods(BeatmapInfo, mods);
            return inner.GetPlayableBeatmap(ruleset, mods);
        }

        public IBeatmap GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList<Mod> mods, CancellationToken cancellationToken)
        {
            prepareMods(BeatmapInfo, mods);
            return inner.GetPlayableBeatmap(ruleset, mods, cancellationToken);
        }

        internal static void PrepareMods(IBeatmapInfo beatmapInfo, IEnumerable<Mod>? mods) => prepareMods(beatmapInfo, mods);

        private static void prepareMods(IBeatmapInfo beatmapInfo, IEnumerable<Mod>? mods)
        {
            if (mods == null)
                return;

            double sourceBPM = BPMResolver.FromBeatmapInfo(beatmapInfo);

            foreach (KumoriModBPMAdjust bpm in mods.OfType<KumoriModBPMAdjust>())
                bpm.SetSourceBPM(sourceBPM);
        }

        public Track LoadTrack() => inner.LoadTrack();
        public Stream GetStream(string storagePath) => inner.GetStream(storagePath);
        public void BeginAsyncLoad() => inner.BeginAsyncLoad();
        public void CancelAsyncLoad() => inner.CancelAsyncLoad();
        public void PrepareTrackForPreview(bool looping, double? offsetFromPreviewPoint = null) => inner.PrepareTrackForPreview(looping, offsetFromPreviewPoint);
    }
}

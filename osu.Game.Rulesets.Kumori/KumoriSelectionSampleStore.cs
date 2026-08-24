// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.IO.Stores;

namespace osu.Game.Rulesets.Kumori
{
    /// <summary>
    /// Supplies the short chime requested by osu!'s toolbar for the Kumori ruleset. Generating the
    /// small PCM sample keeps the external ruleset self-contained and avoids an extra loose asset.
    /// </summary>
    internal sealed class KumoriSelectionSampleStore : IResourceStore<byte[]>
    {
        private const string resource_name = "Samples/UI/ruleset-select-kumori.wav";
        private static readonly Lazy<byte[]> sample = new Lazy<byte[]>(createSample);

        public byte[] Get(string name) => isSelectionSample(name) ? sample.Value : null!;

        public Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(Get(name));

        public Stream? GetStream(string name) => isSelectionSample(name) ? new MemoryStream(sample.Value, false) : null;

        public IEnumerable<string> GetAvailableResources()
        {
            yield return resource_name;
        }

        public void Dispose()
        {
        }

        private static bool isSelectionSample(string name) =>
            name.Replace('\\', '/').Equals(resource_name, StringComparison.OrdinalIgnoreCase);

        private static byte[] createSample()
        {
            const int sampleRate = 44100;
            const double duration = 0.46;
            int sampleCount = (int)(sampleRate * duration);

            using var stream = new MemoryStream(44 + sampleCount * sizeof(short));
            using var writer = new BinaryWriter(stream);

            writer.Write("RIFF"u8);
            writer.Write(36 + sampleCount * sizeof(short));
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * sizeof(short));
            writer.Write((short)sizeof(short));
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(sampleCount * sizeof(short));

            for (int i = 0; i < sampleCount; i++)
            {
                double time = (double)i / sampleRate;
                double value = 0.34 * chimeVoice(time, 0.000, 659.255, 8.5)
                               + 0.25 * chimeVoice(time, 0.045, 987.767, 9.5)
                               + 0.19 * chimeVoice(time, 0.090, 1318.510, 11.0)
                               + 0.08 * chimeVoice(time, 0.015, 329.628, 7.0);

                // Remove the tail cleanly even if a decoder keeps the final PCM frame alive.
                double finalFade = Math.Clamp((duration - time) / 0.045, 0, 1);
                value = Math.Clamp(value * finalFade, -0.95, 0.95);
                writer.Write((short)Math.Round(value * short.MaxValue));
            }

            return stream.ToArray();
        }

        private static double chimeVoice(double time, double delay, double frequency, double decay)
        {
            double localTime = time - delay;

            if (localTime < 0)
                return 0;

            double envelope = (1 - Math.Exp(-localTime * 120)) * Math.Exp(-localTime * decay);
            double fundamental = Math.Sin(2 * Math.PI * frequency * localTime);
            double shimmer = 0.18 * Math.Sin(4 * Math.PI * frequency * localTime + 0.35);
            return envelope * (fundamental + shimmer);
        }
    }
}

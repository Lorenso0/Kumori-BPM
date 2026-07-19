// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace osu.Game.Configuration
{
    public static class BPMPresetStore
    {
        private const int current_version = 1;

        public static IReadOnlyList<BPMPreset> Deserialise(string? serialised)
        {
            if (string.IsNullOrWhiteSpace(serialised))
                return Array.Empty<BPMPreset>();

            try
            {
                var document = JsonConvert.DeserializeObject<BPMPresetDocument>(serialised);

                if (document?.Version == current_version)
                    return normalise(document.Presets);
            }
            catch (JsonException)
            {
                // Fall through to the legacy semicolon format.
            }

            var migrated = new List<BPMPreset>();

            foreach (string valueText in serialised.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    migrated.Add(BPMPreset.Create(value));
            }

            return normalise(migrated);
        }

        public static string Serialise(IEnumerable<BPMPreset> presets) =>
            JsonConvert.SerializeObject(new BPMPresetDocument
            {
                Version = current_version,
                Presets = normalise(presets).ToList(),
            });

        private static IReadOnlyList<BPMPreset> normalise(IEnumerable<BPMPreset>? presets) =>
            presets?.Where(p => BPMResolverProxy.IsValid(p.BPM))
                    .Select(p => new BPMPreset
                    {
                        BPM = Math.Round(p.BPM, 2, MidpointRounding.AwayFromZero),
                        Name = string.IsNullOrWhiteSpace(p.Name)
                            ? BPMPreset.CreateName(p.BPM)
                            : p.Name.Trim(),
                    })
                    .GroupBy(p => p.BPM)
                    .Select(group => group.First())
                    .OrderBy(p => p.BPM)
                    .ToArray()
            ?? Array.Empty<BPMPreset>();

        private sealed class BPMPresetDocument
        {
            [JsonProperty("version")]
            public int Version { get; set; }

            [JsonProperty("presets")]
            public List<BPMPreset> Presets { get; set; } = new List<BPMPreset>();
        }

        // Keep configuration independent of ruleset implementation types.
        private static class BPMResolverProxy
        {
            public static bool IsValid(double bpm) => bpm > 0 && double.IsFinite(bpm);
        }
    }

    public sealed class BPMPreset
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("bpm")]
        public double BPM { get; set; }

        public static BPMPreset Create(double bpm) => new BPMPreset
        {
            BPM = bpm,
            Name = CreateName(bpm),
        };

        internal static string CreateName(double bpm) =>
            FormattableString.Invariant($"{Math.Round(bpm, 2, MidpointRounding.AwayFromZero):0.##} BPM");
    }
}

// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using osu.Framework.Logging;
using osu.Framework.Platform;

namespace osu.Game.Rulesets.Kumori.BPM
{
    public static class BPMPresetStore
    {
        private const int current_version = 1;
        private const string durable_file = "kumori-bpm-presets.json";
        private const string temporary_file = "kumori-bpm-presets.json.tmp";

        public static IReadOnlyList<BPMPreset> Load(Storage storage)
        {
            if (!storage.Exists(durable_file))
                return Array.Empty<BPMPreset>();

            try
            {
                using Stream? stream = storage.GetStream(durable_file);

                if (stream == null)
                    return Array.Empty<BPMPreset>();

                using var reader = new StreamReader(stream);
                var document = JsonConvert.DeserializeObject<BPMPresetDocument>(reader.ReadToEnd());
                return document?.Version == current_version ? normalise(document.Presets) : Array.Empty<BPMPreset>();
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to load Kumori BPM presets");
                return Array.Empty<BPMPreset>();
            }
        }

        public static void Save(Storage storage, IEnumerable<BPMPreset> presets)
        {
            string serialised = JsonConvert.SerializeObject(new BPMPresetDocument
            {
                Version = current_version,
                Presets = normalise(presets).ToList(),
            });

            try
            {
                using (Stream stream = storage.GetStream(temporary_file, FileAccess.Write, FileMode.Create))
                using (var writer = new StreamWriter(stream))
                    writer.Write(serialised);

                storage.Move(temporary_file, durable_file);
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to save Kumori BPM presets");
            }
        }

        private static IReadOnlyList<BPMPreset> normalise(IEnumerable<BPMPreset>? presets) =>
            presets?.Where(p => BPMResolver.IsValid(p.BPM))
                    .Select(p => BPMPreset.Create(p.BPM))
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
    }

    public sealed class BPMPreset
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("bpm")]
        public double BPM { get; set; }

        public static BPMPreset Create(double bpm)
        {
            double rounded = Math.Round(bpm, 2, MidpointRounding.AwayFromZero);

            return new BPMPreset
            {
                BPM = rounded,
                Name = FormattableString.Invariant($"{rounded:0.##} BPM"),
            };
        }
    }
}

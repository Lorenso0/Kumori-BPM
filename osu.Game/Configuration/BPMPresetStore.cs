// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using osu.Framework.Logging;
using osu.Framework.Platform;

namespace osu.Game.Configuration
{
    public static class BPMPresetStore
    {
        private const int current_version = 1;
        private const string durable_file = "bpm-presets.json";
        private const string temporary_file = "bpm-presets.json.tmp";

        /// <summary>
        /// Loads presets from Kumori's durable file, falling back to the legacy shared
        /// configuration value and migrating it when necessary.
        /// </summary>
        public static IReadOnlyList<BPMPreset> Load(Storage storage, string? legacySerialised)
        {
            if (storage.Exists(durable_file))
            {
                try
                {
                    using Stream? stream = storage.GetStream(durable_file);

                    if (stream != null)
                    {
                        using var reader = new StreamReader(stream);

                        if (tryDeserialise(reader.ReadToEnd(), out IReadOnlyList<BPMPreset> durablePresets))
                            return durablePresets;
                    }
                }
                catch (Exception exception)
                {
                    Logger.Error(exception, "Failed to load durable BPM presets");
                }
            }

            IReadOnlyList<BPMPreset> legacyPresets = Deserialise(legacySerialised);

            if (legacyPresets.Count > 0)
                Save(storage, legacyPresets);

            return legacyPresets;
        }

        /// <summary>
        /// Immediately and atomically persists presets outside the shared lazer configuration.
        /// </summary>
        public static string Save(Storage storage, IEnumerable<BPMPreset> presets)
        {
            string serialised = Serialise(presets);

            try
            {
                using (Stream stream = storage.GetStream(temporary_file, FileAccess.Write, FileMode.Create))
                using (var writer = new StreamWriter(stream))
                    writer.Write(serialised);

                storage.Move(temporary_file, durable_file);
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to save durable BPM presets");
            }

            return serialised;
        }

        public static IReadOnlyList<BPMPreset> Deserialise(string? serialised)
        {
            return tryDeserialise(serialised, out IReadOnlyList<BPMPreset> presets) ? presets : Array.Empty<BPMPreset>();
        }

        private static bool tryDeserialise(string? serialised, out IReadOnlyList<BPMPreset> presets)
        {
            presets = Array.Empty<BPMPreset>();

            if (string.IsNullOrWhiteSpace(serialised))
                return false;

            try
            {
                var document = JsonConvert.DeserializeObject<BPMPresetDocument>(serialised);

                if (document?.Version == current_version)
                {
                    presets = normalise(document.Presets);
                    return true;
                }
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

            if (migrated.Count == 0)
                return false;

            presets = normalise(migrated);
            return true;
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

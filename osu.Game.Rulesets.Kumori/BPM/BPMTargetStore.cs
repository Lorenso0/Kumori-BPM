// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using Newtonsoft.Json;
using osu.Framework.Logging;
using osu.Framework.Platform;

namespace osu.Game.Rulesets.Kumori.BPM
{
    public static class BPMTargetStore
    {
        private const int current_version = 1;
        private const string durable_file = "kumori-bpm-target.json";
        private const string temporary_file = "kumori-bpm-target.json.tmp";

        public static double? Load(Storage storage)
        {
            if (!storage.Exists(durable_file))
                return null;

            try
            {
                using Stream? stream = storage.GetStream(durable_file);

                if (stream == null)
                    return null;

                using var reader = new StreamReader(stream);
                TargetDocument? document = JsonConvert.DeserializeObject<TargetDocument>(reader.ReadToEnd());

                return document?.Version == current_version && document.TargetBPM is double target && BPMResolver.IsValid(target)
                    ? target
                    : null;
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to load Kumori target BPM");
                return null;
            }
        }

        public static void Save(Storage storage, double? targetBPM)
        {
            double? normalised = targetBPM is double target && BPMResolver.IsValid(target)
                ? Math.Round(target, 2, MidpointRounding.AwayFromZero)
                : null;
            string serialised = JsonConvert.SerializeObject(new TargetDocument
            {
                Version = current_version,
                TargetBPM = normalised,
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
                Logger.Error(exception, "Failed to save Kumori target BPM");
            }
        }

        private sealed class TargetDocument
        {
            [JsonProperty("version")]
            public int Version { get; set; }

            [JsonProperty("target_bpm")]
            public double? TargetBPM { get; set; }
        }
    }
}

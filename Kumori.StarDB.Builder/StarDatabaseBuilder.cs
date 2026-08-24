// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets.Kumori;
using osu.Game.Rulesets.Kumori.BPM;
using osu.Game.Rulesets.Mods;
using Realms;

namespace Kumori.StarDB.Builder;

internal sealed class StarDatabaseBuilder
{
    private const int maps_per_write = 100;

    public BuilderResult Run(BuilderRequest request, IProgress<BuilderProgress> progress, CancellationToken cancellationToken)
    {
        string input = ResolveInput(request.InputDirectory);
        string output = Path.GetFullPath(request.OutputDirectory);
        Directory.CreateDirectory(output);

        progress.Report(new BuilderProgress(BuilderStage.Scanning, "Reading your lazer library from Realm…"));
        List<BeatmapFile> beatmaps = RealmBeatmapScanner.Scan(input, cancellationToken);

        if (beatmaps.Count == 0)
            throw new InvalidOperationException("No osu!standard beatmaps with stored files were found in client.realm.");

        progress.Report(new BuilderProgress(BuilderStage.Preparing, $"Found {beatmaps.Count:N0} maps. Checking saved progress…", Total: beatmaps.Count));

        var ruleset = new KumoriRuleset();
        IReadOnlyList<KumoriBulkStarRatingProfile> profiles = KumoriBulkStarRatingPlan.Create(ruleset.RulesetInfo);
        string[] profileKeys = profiles.Select(profile => profile.ProfileKey).ToArray();

#pragma warning disable CS8625 // NativeStorage only requires a host for shell integration, which this tool never invokes.
        var storage = new NativeStorage(output, null);
#pragma warning restore CS8625

        KumoriStarRatingIndex.EnsureProfiles(storage, profileKeys);
        KumoriStarRatingIndex.SetProfilesComplete(storage, profileKeys, false, updateMemory: false);
        HashSet<string> indexed = KumoriStarRatingIndex.GetFullyIndexedBeatmapKeys(storage, profileKeys);
        List<BeatmapFile> pending = beatmaps.Where(beatmap => !indexed.Contains(beatmap.Hash)).ToList();
        int reused = beatmaps.Count - pending.Count;

        if (pending.Count == 0)
        {
            KumoriStarRatingIndex.SetProfilesComplete(storage, profileKeys, true, updateMemory: false);
            var alreadyComplete = new BuilderResult(beatmaps.Count, 0, reused, 0, TimeSpan.Zero, Path.Combine(output, KumoriStarRatingIndex.DatabaseFilename));
            progress.Report(createProgress(alreadyComplete.Total, alreadyComplete.Total, reused, profiles.Count, 0, Stopwatch.StartNew(), BuilderStage.Complete,
                "Database is already complete."));
            return alreadyComplete;
        }

        int completed = reused;
        int failed = 0;
        var stopwatch = Stopwatch.StartNew();

        progress.Report(createProgress(completed, beatmaps.Count, reused, profiles.Count, failed, stopwatch, BuilderStage.Calculating,
            $"Calculating {pending.Count:N0} maps; {reused:N0} resumed."));

        void flush(BuilderWorker worker)
        {
            if (worker.Writes.Count == 0)
                return;

            KumoriStarRatingIndex.StoreBatch(storage, worker.Writes, updateMemory: false, profilesAlreadyEnsured: true);
            worker.Writes.Clear();
        }

        void process(BeatmapFile file, BuilderWorker worker)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool mapFailed = false;

            try
            {
                Beatmap beatmap = decode(file);
                double sourceBPM = BPMResolver.FromBeatmap(beatmap);

                if (!BPMResolver.IsValid(sourceBPM))
                    throw new InvalidDataException("Beatmap has no valid uninherited timing point.");

                beatmap.BeatmapInfo.Hash = file.Hash;
                beatmap.BeatmapInfo.BPM = sourceBPM;
                KumoriStarOnlyBatchCalculator calculator = worker.Ruleset.CreateStarOnlyBatchCalculator(new FlatWorkingBeatmap(beatmap));

                for (int profileIndex = 0; profileIndex < worker.Profiles.Count; profileIndex += 2)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    BuilderProfile noHidden = worker.Profiles[profileIndex];
                    BuilderProfile hidden = worker.Profiles[profileIndex + 1];
                    double? noHiddenRating = null;
                    double? hiddenRating = null;

                    try
                    {
                        foreach (KumoriModBPMAdjust bpm in noHidden.Mods.OfType<KumoriModBPMAdjust>())
                            bpm.SetSourceBPM(sourceBPM);

                        foreach (KumoriModBPMAdjust bpm in hidden.Mods.OfType<KumoriModBPMAdjust>())
                            bpm.SetSourceBPM(sourceBPM);

                        KumoriStarRatingPair pair = calculator.CalculatePair(noHidden.Mods, hidden.Mods, cancellationToken);
                        noHiddenRating = pair.NoHidden;
                        hiddenRating = pair.Hidden;

                        if (!double.IsFinite(noHiddenRating.Value))
                            noHiddenRating = null;

                        if (!double.IsFinite(hiddenRating.Value))
                            hiddenRating = null;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        mapFailed = true;
                    }

                    worker.Writes.Add(new KumoriStarRatingDatabaseWrite(noHidden.ProfileKey, file.Hash, noHiddenRating, !noHiddenRating.HasValue));
                    worker.Writes.Add(new KumoriStarRatingDatabaseWrite(hidden.ProfileKey, file.Hash, hiddenRating, !hiddenRating.HasValue));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                mapFailed = true;

                foreach (BuilderProfile profile in worker.Profiles)
                    worker.Writes.Add(new KumoriStarRatingDatabaseWrite(profile.ProfileKey, file.Hash, null, true));

                progress.Report(new BuilderProgress(BuilderStage.Warning, $"Unavailable map {file.DisplayName}: {exception.Message}"));
            }

            if (mapFailed)
                Interlocked.Increment(ref failed);

            if (worker.Writes.Count >= maps_per_write * profiles.Count)
                flush(worker);

            int current = Interlocked.Increment(ref completed);

            if (current == beatmaps.Count || current % 25 == 0)
                progress.Report(createProgress(current, beatmaps.Count, reused, profiles.Count, Volatile.Read(ref failed), stopwatch, BuilderStage.Calculating));
        }

        int nextMap = -1;
        Task[] workerTasks = Enumerable.Range(0, Math.Min(request.Workers, pending.Count)).Select(_ =>
            Task.Factory.StartNew(() =>
            {
                var worker = new BuilderWorker(profiles);

                try
                {
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int index = Interlocked.Increment(ref nextMap);

                        if (index >= pending.Count)
                            break;

                        process(pending[index], worker);
                    }
                }
                finally
                {
                    flush(worker);
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

        try
        {
            Task.WaitAll(workerTasks);
        }
        catch (AggregateException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(createProgress(completed, beatmaps.Count, reused, profiles.Count, failed, stopwatch, BuilderStage.Finalising, "Finalising the portable database…"));
        KumoriStarRatingIndex.SetProfilesComplete(storage, profileKeys, true, updateMemory: false);
        stopwatch.Stop();

        var result = new BuilderResult(beatmaps.Count, beatmaps.Count - reused, reused, failed, stopwatch.Elapsed,
            Path.Combine(output, KumoriStarRatingIndex.DatabaseFilename));
        progress.Report(createProgress(beatmaps.Count, beatmaps.Count, reused, profiles.Count, failed, stopwatch, BuilderStage.Complete, "Database complete."));
        return result;
    }

    public static string ResolveInput(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            string explicitPath = Path.GetFullPath(requested);

            if (File.Exists(explicitPath) && string.Equals(Path.GetFileName(explicitPath), "client.realm", StringComparison.OrdinalIgnoreCase))
                explicitPath = Path.GetDirectoryName(explicitPath)!;

            if (!Directory.Exists(explicitPath))
                throw new DirectoryNotFoundException($"Input folder does not exist: {explicitPath}");

            return resolveStorageRedirect(explicitPath);
        }

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string lazerData = Path.Combine(appData, "osu");

        if (Directory.Exists(lazerData))
            return resolveStorageRedirect(lazerData);

        throw new DirectoryNotFoundException("Could not find lazer's data folder. Choose the folder containing client.realm.");
    }

    private static string resolveStorageRedirect(string path)
    {
        string storageConfiguration = Path.Combine(path, "storage.ini");

        if (File.Exists(storageConfiguration))
        {
            string? redirect = File.ReadLines(storageConfiguration)
                                   .Select(line => line.Split('=', 2, StringSplitOptions.TrimEntries))
                                   .Where(parts => parts.Length == 2 && parts[0].Equals("FullPath", StringComparison.OrdinalIgnoreCase))
                                   .Select(parts => parts[1])
                                   .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(redirect) && Directory.Exists(redirect))
                path = Path.GetFullPath(redirect);
        }

        if (!File.Exists(Path.Combine(path, "client.realm")) || !Directory.Exists(Path.Combine(path, "files")))
            throw new DirectoryNotFoundException($"This is not a lazer data folder: {path}");

        return path;
    }

    private static Beatmap decode(BeatmapFile file)
    {
        using FileStream stream = File.Open(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new LineBufferedReader(stream);
        return Decoder.GetDecoder<Beatmap>(reader).Decode(reader);
    }

    private static BuilderProgress createProgress(int completed, int total, int reused, int profileCount, int failed, Stopwatch stopwatch,
                                                   BuilderStage stage, string? message = null)
    {
        int calculated = Math.Max(0, completed - reused);
        double mapsPerSecond = stopwatch.Elapsed.TotalSeconds <= 0 ? 0 : calculated / stopwatch.Elapsed.TotalSeconds;
        double remainingSeconds = mapsPerSecond <= 0 ? 0 : (total - completed) / mapsPerSecond;
        TimeSpan? remaining = remainingSeconds <= 0 ? null : TimeSpan.FromSeconds(remainingSeconds);
        message ??= $"{completed:N0} of {total:N0} maps";
        return new BuilderProgress(stage, message, completed, total, (long)completed * profileCount, reused, failed, mapsPerSecond, remaining);
    }

    private sealed class BuilderWorker
    {
        public readonly KumoriRuleset Ruleset = new KumoriRuleset();
        public readonly IReadOnlyList<BuilderProfile> Profiles;
        public readonly List<KumoriStarRatingDatabaseWrite> Writes = new List<KumoriStarRatingDatabaseWrite>(maps_per_write * KumoriBulkStarRatingPlan.ProfileCount);

        public BuilderWorker(IEnumerable<KumoriBulkStarRatingProfile> profiles)
        {
            Profiles = profiles.Select(profile => new BuilderProfile(profile.ProfileKey, profile.CreateWorkerMods())).ToArray();
        }
    }

    private sealed record BuilderProfile(string ProfileKey, Mod[] Mods);
}

internal sealed record BeatmapFile(string Path, string Hash)
{
    public string DisplayName => System.IO.Path.GetFileName(Path);
}

internal static class RealmBeatmapScanner
{
    private const ulong realm_schema_version = 51;

    public static List<BeatmapFile> Scan(string dataDirectory, CancellationToken cancellationToken)
    {
        string realmPath = Path.Combine(dataDirectory, "client.realm");
        string filesPath = Path.Combine(dataDirectory, "files");
        var configuration = new RealmConfiguration(realmPath)
        {
            IsReadOnly = true,
            SchemaVersion = realm_schema_version,
        };

        using Realm realm = Realm.GetInstance(configuration);
        var beatmaps = new Dictionary<string, BeatmapFile>(StringComparer.Ordinal);

        foreach (BeatmapInfo beatmap in realm.All<BeatmapInfo>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (beatmap.Hidden || beatmap.Ruleset.OnlineID != 0 || beatmap.BeatmapSet?.DeletePending == true || string.IsNullOrWhiteSpace(beatmap.Hash))
                continue;

            string hash = beatmap.Hash;

            if (hash.Length < 2 || beatmaps.ContainsKey(hash))
                continue;

            string path = Path.Combine(filesPath, hash[..1], hash[..2], hash);

            if (File.Exists(path))
                beatmaps.Add(hash, new BeatmapFile(path, hash));
        }

        return beatmaps.Values.OrderBy(beatmap => beatmap.Hash, StringComparer.Ordinal).ToList();
    }
}

internal sealed record BuilderRequest(string InputDirectory, string OutputDirectory, int Workers);

internal sealed record BuilderResult(int Total, int Calculated, int Reused, int Failed, TimeSpan Elapsed, string DatabasePath);

internal sealed record BuilderProgress(BuilderStage Stage, string Message, int Completed = 0, int Total = 0, long Ratings = 0, int Reused = 0,
                                       int Failed = 0, double MapsPerSecond = 0, TimeSpan? Remaining = null);

internal enum BuilderStage
{
    Scanning,
    Preparing,
    Calculating,
    Finalising,
    Warning,
    Complete,
}

// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.Kumori.Updates
{
    /// <summary>
    /// Checks the repository's latest GitHub Release and stages a verified replacement DLL.
    /// Windows keeps loaded assemblies locked, so a short-lived PowerShell helper performs the
    /// replacement after the current osu! process exits.
    /// </summary>
    internal static class KumoriAutoUpdater
    {
        private const string ruleset_filename = "osu.Game.Rulesets.Kumori.dll";
        private const string checksum_filename = ruleset_filename + ".sha256";
        private const string installed_marker_filename = ".kumori-update-installed";
        private const string latest_release_url = "https://api.github.com/repos/Lorenso0/Kumori-BPM/releases/latest";
        private const int maximum_release_metadata_bytes = 1024 * 1024;
        private const int maximum_checksum_bytes = 4096;
        private const int maximum_ruleset_bytes = 32 * 1024 * 1024;

        private static readonly TimeSpan check_interval = TimeSpan.FromMinutes(15);
        private static int started;

        internal static string ReplacementScriptForTesting => replacement_script;
        internal static TimeSpan CheckIntervalForTesting => check_interval;

        public static void StartIfInstalled()
        {
            if (Interlocked.Exchange(ref started, 1) != 0 || !OperatingSystem.IsWindows())
                return;

            string rulesetPath = typeof(KumoriRuleset).Assembly.Location;

            if (!IsInstalledRulesetPath(rulesetPath))
                return;

            string directory = Path.GetDirectoryName(rulesetPath)!;
            string? installedVersion = ConsumeInstalledVersion(directory);

            if (installedVersion != null)
                KumoriUpdateNotificationBus.Post($"Kumori updated successfully to v{installedVersion}.");

            _ = Task.Run(() => monitorForUpdates(rulesetPath));
        }

        internal static bool IsInstalledRulesetPath(string path) =>
            string.Equals(Path.GetFileName(path), ruleset_filename, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "rulesets", StringComparison.OrdinalIgnoreCase);

        internal static bool TryParseReleaseVersion(string value, out Version version)
        {
            version = new Version();

            if (!value.StartsWith("ruleset-v", StringComparison.OrdinalIgnoreCase))
                return false;

            string semanticVersion = value["ruleset-v".Length..];
            int metadataIndex = semanticVersion.IndexOfAny(['-', '+']);

            if (metadataIndex >= 0)
                semanticVersion = semanticVersion[..metadataIndex];

            string[] components = semanticVersion.Split('.');

            if (components.Length != 3
                || !int.TryParse(components[0], out int major)
                || !int.TryParse(components[1], out int minor)
                || !int.TryParse(components[2], out int patch)
                || major < 0 || minor < 0 || patch < 0)
                return false;

            version = new Version(major, minor, patch);
            return true;
        }

        internal static bool TryReadChecksum(string value, out string checksum)
        {
            checksum = value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

            return checksum.Length == 64 && checksum.All(Uri.IsHexDigit);
        }

        internal static string? ConsumeInstalledVersion(string directory)
        {
            string markerPath = Path.Combine(directory, installed_marker_filename);

            try
            {
                if (!File.Exists(markerPath))
                    return null;

                string version = File.ReadAllText(markerPath).Trim();
                File.Delete(markerPath);
                return TryParseReleaseVersion($"ruleset-v{version}", out Version parsed) ? parsed.ToString() : null;
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to read Kumori update-completion marker");
                return null;
            }
        }

        internal static KumoriUpdateCandidate? ParseCandidate(string json, Version currentVersion)
        {
            GitHubRelease? release = JsonSerializer.Deserialize<GitHubRelease>(json);
            string tagName = release?.TagName ?? string.Empty;
            string versionPortion = tagName.StartsWith("ruleset-v", StringComparison.OrdinalIgnoreCase)
                ? tagName["ruleset-v".Length..]
                : string.Empty;
            int metadataIndex = versionPortion.IndexOf('+');
            string versionWithoutMetadata = metadataIndex >= 0 ? versionPortion[..metadataIndex] : versionPortion;

            if (release == null || release.Draft || release.Prerelease
                || versionWithoutMetadata.Contains('-')
                || !TryParseReleaseVersion(tagName, out Version releaseVersion)
                || releaseVersion <= normaliseVersion(currentVersion))
                return null;

            GitHubReleaseAsset? ruleset = release.Assets?.SingleOrDefault(asset => asset.Name == ruleset_filename);
            GitHubReleaseAsset? checksum = release.Assets?.SingleOrDefault(asset => asset.Name == checksum_filename);

            if (!tryCreateGitHubUri(ruleset?.DownloadUrl, out Uri? rulesetUri)
                || !tryCreateGitHubUri(checksum?.DownloadUrl, out Uri? checksumUri))
                return null;

            string? digest = ruleset?.Digest;

            if (digest != null && !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                return null;

            return new KumoriUpdateCandidate(releaseVersion, rulesetUri, checksumUri, digest?["sha256:".Length..]);
        }

        private static async Task monitorForUpdates(string rulesetPath)
        {
            string directory = Path.GetDirectoryName(rulesetPath)!;
            string disabledPath = Path.Combine(directory, ".kumori-auto-update.disabled");
            string statePath = Path.Combine(directory, ".kumori-auto-update.state");

            while (!File.Exists(disabledPath))
            {
                if (await checkForUpdate(rulesetPath, statePath).ConfigureAwait(false))
                    return;

                await Task.Delay(check_interval).ConfigureAwait(false);
            }
        }

        private static async Task<bool> checkForUpdate(string rulesetPath, string statePath)
        {
            string directory = Path.GetDirectoryName(rulesetPath)!;

            try
            {
                Version currentVersion = normaliseVersion(typeof(KumoriRuleset).Assembly.GetName().Version ?? new Version());

                using var client = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(30),
                };
                client.DefaultRequestHeaders.UserAgent.ParseAdd($"Kumori-Ruleset-Updater/{currentVersion}");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

                string releaseJson = await downloadText(client, new Uri(latest_release_url), maximum_release_metadata_bytes).ConfigureAwait(false);
                KumoriUpdateCandidate? candidate = ParseCandidate(releaseJson, currentVersion);

                if (candidate == null)
                {
                    touchState(statePath, currentVersion);
                    return false;
                }

                string checksumText = await downloadText(client, candidate.ChecksumUri, maximum_checksum_bytes).ConfigureAwait(false);

                if (!TryReadChecksum(checksumText, out string expectedHash)
                    || candidate.GitHubDigest != null && !string.Equals(candidate.GitHubDigest, expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The GitHub release checksum metadata did not agree.");

                byte[] rulesetBytes = await downloadBytes(client, candidate.RulesetUri, maximum_ruleset_bytes).ConfigureAwait(false);
                string actualHash = Convert.ToHexString(SHA256.HashData(rulesetBytes));

                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The downloaded Kumori ruleset failed SHA-256 verification.");

                string temporaryPath = Path.Combine(directory, $".kumori-update-{candidate.Version}.tmp");
                string stagedPath = Path.Combine(directory, $".kumori-update-{candidate.Version}.dll.pending");
                await File.WriteAllBytesAsync(temporaryPath, rulesetBytes).ConfigureAwait(false);
                File.Move(temporaryPath, stagedPath, true);

                launchReplacementHelper(rulesetPath, stagedPath, expectedHash, candidate.Version);
                touchState(statePath, candidate.Version);
                KumoriUpdateNotificationBus.Post($"Kumori v{candidate.Version} is ready and will install when osu! closes.");
                Logger.Log($"Kumori update {candidate.Version} downloaded and will be installed when osu! closes.", level: LogLevel.Important);
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Kumori automatic update check failed");
                return false;
            }
        }

        private static void touchState(string statePath, Version version)
        {
            try
            {
                File.WriteAllText(statePath, $"{DateTime.UtcNow:O}|{version}");
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to save Kumori automatic-update state");
            }
        }

        private static void launchReplacementHelper(string targetPath, string stagedPath, string expectedHash, Version version)
        {
            string helperPath = Path.Combine(Path.GetTempPath(), $"KumoriRulesetUpdater-{Guid.NewGuid():N}.ps1");
            File.WriteAllText(helperPath, replacement_script);

            string powershellPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe");

            if (!File.Exists(powershellPath))
                powershellPath = "powershell.exe";

            var startInfo = new ProcessStartInfo
            {
                FileName = powershellPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(helperPath);
            startInfo.ArgumentList.Add("-ProcessId");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            startInfo.ArgumentList.Add("-StagedDll");
            startInfo.ArgumentList.Add(stagedPath);
            startInfo.ArgumentList.Add("-TargetDll");
            startInfo.ArgumentList.Add(targetPath);
            startInfo.ArgumentList.Add("-ExpectedHash");
            startInfo.ArgumentList.Add(expectedHash);
            startInfo.ArgumentList.Add("-Version");
            startInfo.ArgumentList.Add(version.ToString());

            Process.Start(startInfo)?.Dispose();
        }

        private static async Task<string> downloadText(HttpClient client, Uri uri, int maximumBytes) =>
            System.Text.Encoding.UTF8.GetString(await downloadBytes(client, uri, maximumBytes).ConfigureAwait(false));

        private static async Task<byte[]> downloadBytes(HttpClient client, Uri uri, int maximumBytes)
        {
            using HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength > maximumBytes)
                throw new InvalidDataException($"Kumori update download exceeded {maximumBytes} bytes.");

            await using Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[81920];

            while (true)
            {
                int read = await input.ReadAsync(buffer).ConfigureAwait(false);

                if (read == 0)
                    break;

                if (output.Length + read > maximumBytes)
                    throw new InvalidDataException($"Kumori update download exceeded {maximumBytes} bytes.");

                output.Write(buffer, 0, read);
            }

            return output.ToArray();
        }

        private static Version normaliseVersion(Version version) =>
            new Version(Math.Max(0, version.Major), Math.Max(0, version.Minor), Math.Max(0, version.Build));

        private static bool tryCreateGitHubUri(string? value, out Uri uri)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed)
                && parsed.Scheme == Uri.UriSchemeHttps
                && (parsed.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                    || parsed.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
            {
                uri = parsed;
                return true;
            }

            uri = null!;
            return false;
        }

        private const string replacement_script = """
param(
    [Parameter(Mandatory = $true)][int] $ProcessId,
    [Parameter(Mandatory = $true)][string] $StagedDll,
    [Parameter(Mandatory = $true)][string] $TargetDll,
    [Parameter(Mandatory = $true)][string] $ExpectedHash,
    [Parameter(Mandatory = $true)][string] $Version
)

$ErrorActionPreference = 'Stop'
$logPath = Join-Path ([IO.Path]::GetDirectoryName($TargetDll)) 'kumori-updater.log'

function Get-Sha256([string] $Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $sha256 = [Security.Cryptography.SHA256]::Create()
        try {
            return ([BitConverter]::ToString($sha256.ComputeHash($stream))).Replace('-', '')
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

try {
    if ([IO.Path]::GetFileName($TargetDll) -cne 'osu.Game.Rulesets.Kumori.dll') {
        throw 'Refusing to replace an unexpected target filename.'
    }

    if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($StagedDll)) -ne [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($TargetDll))) {
        throw 'Refusing to use a staged DLL outside the rulesets directory.'
    }

    Wait-Process -Id $ProcessId -ErrorAction SilentlyContinue

    $stagedHash = Get-Sha256 $StagedDll
    if ($stagedHash -ine $ExpectedHash) {
        throw 'The staged Kumori DLL failed its final checksum verification.'
    }

    $backupPath = "$TargetDll.bak"
    [IO.File]::Copy($TargetDll, $backupPath, $true)

    try {
        [IO.File]::Copy($StagedDll, $TargetDll, $true)
        $installedHash = Get-Sha256 $TargetDll

        if ($installedHash -ine $ExpectedHash) {
            throw 'The installed Kumori DLL failed checksum verification.'
        }
    }
    catch {
        [IO.File]::Copy($backupPath, $TargetDll, $true)
        throw
    }

    Remove-Item -LiteralPath $StagedDll -Force
    [IO.File]::WriteAllText((Join-Path ([IO.Path]::GetDirectoryName($TargetDll)) '.kumori-update-installed'), $Version)
    Add-Content -LiteralPath $logPath -Value "$(Get-Date -Format o) Installed Kumori $Version. Backup: $backupPath"
}
catch {
    Add-Content -LiteralPath $logPath -Value "$(Get-Date -Format o) Update failed: $($_.Exception.Message)"
}
finally {
    Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
}
""";

        private sealed class GitHubRelease
        {
            [JsonPropertyName("tag_name")]
            public string? TagName { get; init; }

            [JsonPropertyName("draft")]
            public bool Draft { get; init; }

            [JsonPropertyName("prerelease")]
            public bool Prerelease { get; init; }

            [JsonPropertyName("assets")]
            public GitHubReleaseAsset[]? Assets { get; init; }
        }

        private sealed class GitHubReleaseAsset
        {
            [JsonPropertyName("name")]
            public string? Name { get; init; }

            [JsonPropertyName("browser_download_url")]
            public string? DownloadUrl { get; init; }

            [JsonPropertyName("digest")]
            public string? Digest { get; init; }
        }
    }

    internal sealed record KumoriUpdateCandidate(Version Version, Uri RulesetUri, Uri ChecksumUri, string? GitHubDigest);
}

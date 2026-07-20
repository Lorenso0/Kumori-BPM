<p align="center">
  <img width="240" alt="Kumori logo" src="assets/kumori-icon.png">
</p>

# Kumori BPM

Kumori is an unofficial Windows build of [osu!lazer](https://github.com/ppy/osu)
with a target-BPM gameplay mod. It is based on the public
`2026.711.0-lazer` release.

This project is not affiliated with or endorsed by ppy Pty Ltd. The osu! name
and branding belong to their respective owners.

## BPM Adjust

The **BPM Adjust (BPM)** mod appears in the Fun category for osu!, taiko,
catch, and mania. It can:

- Set a target BPM which is recalculated for each selected beatmap.
- Choose from pitch-preserving, speed-linked, Nightcore, Daycore, Balanced,
  custom-semitone, Chipmunk, and Deep audio treatments.
- Add optional Nightcore percussion or a simple metronome independently of pitch.
- Optionally preserve the map's real-time AR and OD behavior.
- Save reusable BPM presets.
- Store its settings in local scores and replays.

See [BPM_MOD.md](BPM_MOD.md) for detailed behavior, limitations, and safety
information.

## Safety

Kumori is an unranked custom client:

- Official score submission is disabled for the entire build.
- BPM Adjust is unavailable in multiplayer.
- Login, chat, beatmap browsing, downloads, and leaderboards remain enabled.
- Installer and Velopack portable builds update from Kumori's GitHub releases.

## Installing

Download `KumoriBPM-win-Setup.exe` from the repository's GitHub Releases page.
The per-user installer creates a separate Kumori application directory and does
not require administrator access. Once installed, Kumori checks for releases in
the background, downloads updates, and offers to restart when they are ready.

Existing users of releases older than `2026.711.0-kumori.3` must install this
version manually once. Automatic updates work for subsequent releases.

For a self-contained alternative, download and extract
`KumoriBPM-win-Portable.zip`, then run `Kumori BPM.exe`. This package also
self-updates. The versioned `kumori-osu-*-win-x64.zip` remains available as a
legacy manual-update archive.

The releases are unsigned, so Windows SmartScreen may display a warning.

By default Kumori uses lazer's normal `osu` data profile. Do not run official
lazer and Kumori simultaneously, and back up the lazer data directory before
switching between substantially different client versions.

For an isolated profile:

```powershell
& ".\Kumori BPM.exe" --bpm-isolated
```

## Building

The repository pins the required .NET SDK in `global.json`.

```powershell
dotnet restore osu.Desktop.slnf
dotnet build osu.Desktop.slnf -c Release
dotnet test osu.Game.Tests/osu.Game.Tests.csproj -c Release --filter "FullyQualifiedName~ModBPMAdjust|FullyQualifiedName~BPMCustomBuildPolicy"
dotnet test osu.Game.Rulesets.Osu.Tests/osu.Game.Rulesets.Osu.Tests.csproj -c Release --filter "FullyQualifiedName~ModBPMAdjust"
```

To create the audited Windows release ZIP and checksum:

```powershell
dotnet tool restore
.\scripts\Publish-Release.ps1
```

Generated files are written below `artifacts/releases/`. The release script
also creates the Velopack installer, self-updating portable package, update
feed, and full/delta update packages.

## Maintaining the fork

The canonical upstream remote should be named `upstream`. For a new lazer
release, create a branch from the matching upstream tag, replay the Kumori
commits, resolve conflicts, update `KumoriVersion`, and rerun the full release
validation.

Do not re-enable ppy's production deployment, NuGet publishing, Sentry, or
internal infrastructure workflows in this fork.

## Licence

The upstream osu! source and Kumori changes are distributed under the
[MIT licence](LICENCE). Third-party libraries retain their own licences; the
release packaging script generates a notice from the restored NuGet metadata.

The osu! resource package has separate terms described by
[ppy/osu-resources](https://github.com/ppy/osu-resources).

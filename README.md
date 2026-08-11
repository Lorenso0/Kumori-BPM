<p align="center">
  <img width="240" alt="Kumori logo" src="assets/kumori-icon.png">
</p>

# Kumori BPM

Kumori is an unofficial Windows build of [osu!lazer](https://github.com/ppy/osu)
with a target-BPM gameplay mod. It is based on the public
`2026.804.2-lazer` release.

This project is not affiliated with or endorsed by ppy Pty Ltd. The osu! name
and branding belong to their respective owners.

## BPM Adjust

The **BPM Adjust (BPM)** mod appears in the Fun category for osu!, taiko,
catch, and mania. It can:

- Set a target BPM which is recalculated for each selected beatmap.
- Choose a distinct Preserve Pitch, Adjust Pitch, Balanced, Adaptive, or Custom
  Pitch treatment, with Nightcore, Daycore, Chipmunk, and Deep pitch presets.
- Add explicit Nightcore percussion or a metronome independently of pitch.
- Choose whether hitsounds follow playback speed, the music pitch, or preserve pitch.
- Optionally preserve the map's real-time AR and OD behavior.
- Save reusable BPM presets.
- Store its settings in local scores and replays.
- Preview the effective rate, duration, pitch, tempo processing, audio-quality
  risk, stat behavior, and variable-BPM range before playing.

See [BPM_MOD.md](BPM_MOD.md) for detailed behavior, limitations, and safety
information.

## Safety

Kumori is an unranked custom client:

- Official score submission is disabled for the entire build.
- BPM Adjust is unavailable in multiplayer.
- Login, chat, the friends list, beatmap browsing, downloads, and leaderboards
  remain enabled.
- Official realtime presence, multiplayer, and spectating are disabled because
  ppy's realtime service does not accept custom executable hashes. Friends will
  not see Kumori sessions as online.
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

Kumori uses its isolated `osu-bpm` data profile by default, preventing database
conflicts with official lazer. Existing users who intentionally want to continue
using the shared official profile can launch with:

```powershell
& ".\Kumori BPM.exe" --bpm-shared-profile
```

The old explicit isolated option remains accepted for shortcuts and scripts:

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

The scheduled `Update from upstream lazer` workflow checks ppy's official
GitHub releases daily. A new `-lazer` tag is merged, release metadata is updated,
and the complete build/test/package audit runs before the `kumori` branch and
matching `-kumori.1` tag are pushed. A successful update dispatches the release
workflow automatically. Merge conflicts create an issue and never publish an
unverified build.

Do not re-enable ppy's production deployment, NuGet publishing, Sentry, or
internal infrastructure workflows in this fork.

## Licence

The upstream osu! source and Kumori changes are distributed under the
[MIT licence](LICENCE). Third-party libraries retain their own licences; the
release packaging script generates a notice from the restored NuGet metadata.

The osu! resource package has separate terms described by
[ppy/osu-resources](https://github.com/ppy/osu-resources).

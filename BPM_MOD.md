# BPM Adjust Mod

This repository is a local custom build of [ppy/osu](https://github.com/ppy/osu) with a target-BPM gameplay mod.

## Source

- Upstream repository: `https://github.com/ppy/osu.git`
- Upstream commit: `b11b274d1cb5c22eabe9dba5df14fa1e4ecc4e6d`
- Local branch: `bpm-adjust-mod`
- Licence: the upstream MIT licence in `LICENCE` remains in effect.

No unofficial osu! source files or third-party mod implementations are included.

## Using the mod

1. Open solo song select and choose a beatmap.
2. Open the mod selector.
3. Find **BPM Adjust (BPM)** in the Fun column.
4. Enter a positive target BPM with up to two decimal places, or use the synchronized 140–320 BPM slider. Typed values outside the slider range remain valid.
5. Select an audio mode:
   - **Preserve Pitch** changes tempo without shifting the song's pitch.
   - **Adjust Pitch** changes pitch with playback speed.
   - **Nightcore** uses lazer's fixed 1.5x pitch treatment and synced beat accents while compensating tempo to reach the target BPM.
6. Choose whether **Scale map stats with BPM** should be enabled:
   - **Enabled** keeps DT/HT-style rate scaling for AR and OD.
   - **Disabled** compensates rate-sensitive stats so their real-time approach and hit windows match the map's original values. Object spacing, song duration, and playback speed still follow the selected BPM.

The target remains selected when changing maps. The mod recalculates `rate = target BPM / map primary BPM` for every difficulty.

Clear the target field before typing a replacement. An empty field or a transient `0` is treated as neutral playback and will not initialise itself again until the mod is newly enabled.

Use **+ SAVE** below the target field to store a reusable BPM button. Click a saved BPM to apply it, or right-click it to delete it. These BPM shortcuts persist between launches in a versioned JSON format. Existing semicolon-separated presets are migrated automatically the next time presets are changed.

For variable-BPM maps, lazer's most common BPM is changed to the target and every timing section is scaled proportionally.

Local score panels display the saved target beside the **BPM** mod icon (for example, `174.5 BPM`). The target, audio mode, map-stat scaling toggle, and explicit neutral/cleared state are retained in local score, replay, and Personal Preset mod settings.

## Safety and online play

The mod is always unranked, disabled in multiplayer, and incompatible with every other rate-changing mod. The custom executable never submits scores to official endpoints, whether or not BPM Adjust is selected; all scores and replays remain local.

Login, chat, friends, beatmap browsing, downloads, and leaderboards remain enabled. Only the server's known pinned-version liveness response is treated as non-fatal. Other network failures continue to be reported normally.

Any positive finite target is accepted. At tempos below the audio backend's 0.05x limit, the remaining slowdown is moved to frequency so the requested combined rate remains intact without crashing. The mod details show when this fallback is active. If a target/source ratio exceeds the range representable by a `double`, the nearest positive representable rate is used instead of silently returning to `1x`. Extreme rates are experimental and may cause distorted or silent audio, poor performance, or unplayable timing.

## Data sharing

By default this release intentionally uses lazer's normal `osu` profile. It therefore reads and writes the same storage configured by `%APPDATA%\osu\storage.ini`, including:

- `client.realm` for maps, collections, local scores, presets, and metadata;
- `files/` for beatmaps, skins, and replay content;
- `game.ini` and `framework.ini` for settings.

This keeps maps and collections identical between the official and BPM builds. Do not run both builds at the same time, and back up the entire lazer data directory before moving between substantially different client versions. Official lazer preserves BPM score JSON but represents the unrecognised mod as `BPM??` and cannot reproduce its gameplay rate.

The BPM build hides non-practice visual/physics gimmick mods from the mod selector to keep it focused. Their implementations remain registered internally, so existing scores, replays, and presets which use them continue to resolve.

For isolated testing, launch with `--bpm-isolated`. This uses a separate `osu-bpm` profile and IPC pipe:

```powershell
& ".\artifacts\osu-lazer-bpm\osu!.exe" --bpm-isolated
```

## Build

The required .NET 8 SDK is selected by `global.json`.

```powershell
dotnet restore osu.Desktop.slnf
dotnet test osu.Game.Tests/osu.Game.Tests.csproj --filter "FullyQualifiedName~ModBPMAdjust"
dotnet build osu.Desktop.slnf -c Release
dotnet publish osu.Desktop/osu.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/osu-lazer-bpm
```

Run the published client with:

```powershell
& ".\artifacts\osu-lazer-bpm\osu!.exe"
```

This is a custom client build. It cannot be installed as a mod DLL into the official osu!lazer release.

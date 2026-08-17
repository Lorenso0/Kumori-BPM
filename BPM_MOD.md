# BPM Adjust Mod

This repository is a local custom build of [ppy/osu](https://github.com/ppy/osu) with a target-BPM gameplay mod.

## Source

- Upstream repository: `https://github.com/ppy/osu.git`
- Upstream release: `2026.804.2-lazer`
- Upstream commit: `3c1c96f742e7aae2ff67a7361e058fe91ca3b955`
- Local branch: `kumori`
- Kumori release: `2026.804.2-kumori.8`
- Executable version: `2026.804.2-lazer`
- Licence: the upstream MIT licence in `LICENCE` remains in effect.

No unofficial osu! source files or third-party mod implementations are included.

## Using the mod

1. Open solo song select and choose a beatmap.
2. Open the mod selector.
3. Find **BPM Adjust (BPM)** in the Fun column.
4. Enter a positive target BPM with up to two decimal places, or use the synchronized 140–320 BPM slider. Typed values outside the slider range remain valid.
   A live summary shows the source and target BPM, effective rate, original and adjusted duration, pitch shift, tempo processing, map-stat behavior, variable-BPM range, and extreme-rate/fallback warnings.
5. Optionally use **Show only maps** to filter song select to an inclusive star-rating range:
   - **Star rating pre-mod** uses each map's original displayed star rating.
   - **Star rating post-mod** automatically loads a completed profile for the selected target BPM, ruleset, and mod settings. If no complete profile exists, it shows a **Calculate maps** button for one complete exact pass using the same ruleset difficulty calculation as the song-select star display. The pass shows completed/total progress, and **Cancel** aborts without applying partial results. Exact results and unavailable maps are persisted by beatmap hash, so every star range can reuse the completed dataset across future sessions without retrying failed maps.
   - **Disabled** restores the full song list while retaining the entered range for later use.
6. Select an audio treatment:
   - **Preserve Pitch** changes tempo without shifting the song's pitch.
   - **Adjust Pitch** changes pitch with playback speed.
   - **Balanced** splits the rate equally between pitch and tempo adjustments to reduce extreme processing artifacts.
   - **Adaptive** preserves pitch between 0.75x and 1.5x tempo, then moves only the remaining extreme change into pitch for cleaner playback.
   - **Custom Pitch** applies the selected **Custom pitch** shift from -12 to +12 semitones and compensates tempo so the target BPM remains exact.
   - **Nightcore**, **Daycore**, **Chipmunk**, and **Deep** buttons select their familiar pitch as a Custom Pitch preset. This keeps named styles convenient without duplicating audio modes.
   - Old scores and presets using the previous named audio modes remain playable and are converted to the equivalent treatment when opened in the mod editor.
7. Select a **Beat accents** mode:
   - **Off** disables additional beat sounds.
   - **Nightcore** adds lazer's beat-synchronised kick, clap, hat, and finish pattern with any audio mode.
   - **Metronome** adds one click per beat with a stronger downbeat.
8. Select a **Hitsound pitch** mode:
   - **Follow Playback Rate** retains lazer's normal DT/HT behavior.
   - **Follow Music Pitch** makes gameplay hitsounds follow the selected music pitch treatment.
   - **Preserve Pitch** leaves gameplay hitsounds at their original pitch.
9. Choose whether **Scale map stats with BPM** should be enabled:
   - **Enabled** keeps DT/HT-style rate scaling for AR and OD.
   - **Disabled** compensates rate-sensitive stats so their real-time approach and hit windows match the map's original values. Object spacing, song duration, and playback speed still follow the selected BPM.

The target remains selected when changing maps. The mod recalculates `rate = target BPM / map primary BPM` for every difficulty.

Clear the target field before typing a replacement. An empty field or a transient `0` is treated as neutral playback and will not initialise itself again until the mod is newly enabled.

Use **+ SAVE** below the target field to store a reusable BPM button. Click a saved BPM to apply it, or right-click it to delete it. These BPM shortcuts persist between launches in a versioned JSON format. Existing semicolon-separated presets are migrated automatically the next time presets are changed.

For variable-BPM maps, lazer's most common BPM is changed to the target and every timing section is scaled proportionally.

Local score panels display the saved target beside the **BPM** mod acronym (for example, `BPM 174.5`). The target, audio mode, custom pitch, beat accents, map-stat scaling toggle, and explicit neutral/cleared state are retained in local score, replay, and Personal Preset mod settings.

## Safety and online play

The mod is always unranked, disabled in multiplayer, and incompatible with every other rate-changing mod. The custom executable never submits scores to official endpoints, whether or not BPM Adjust is selected; all scores and replays remain local.

Login, chat, friends-list retrieval, beatmap browsing, downloads, and leaderboards remain enabled. Official realtime presence, multiplayer, and spectating are disabled because ppy's realtime server validates the modified game assembly hash and rejects custom builds. Friends therefore do not see a Kumori-only session as online. The known realtime-version liveness response is kept separate from API availability so it cannot disable otherwise working API and chat features; other network failures continue to be reported normally.

Any positive finite target is accepted. The live preview classifies the effective processing as clean, heavy time stretching, a large pitch shift, or extreme processing based on the actual tempo and frequency stages rather than the combined rate alone. At tempos below the audio backend's 0.05x limit, the remaining slowdown is moved to frequency so the requested combined rate remains intact without crashing. If a target/source ratio exceeds the range representable by a `double`, the nearest positive representable rate is used instead of silently returning to `1x`. Extreme rates are experimental and may cause distorted or silent audio, poor performance, or unplayable timing.

## Data sharing

By default Kumori uses lazer's normal `osu` profile, so existing settings, beatmaps, collections, skins, local scores, and replays carry over automatically. This profile contains:

- `client.realm` for maps, collections, local scores, presets, and metadata;
- `files/` for beatmaps, skins, and replay content;
- `game.ini` and `framework.ini` for settings.

Do not run official lazer and Kumori simultaneously against this shared profile, and back up the data directory before moving between substantially different client versions. Official lazer preserves BPM score JSON but represents the unrecognised mod as `BPM??` and cannot reproduce its gameplay rate.

The BPM build hides non-practice visual/physics gimmick mods from the mod selector to keep it focused. Their implementations remain registered internally, so existing scores, replays, and presets which use them continue to resolve.

For isolated testing, launch with `--bpm-isolated`. This uses the separate `osu-bpm` profile and IPC pipe:

```powershell
& ".\Kumori BPM.exe" --bpm-isolated
```

`--bpm-shared-profile` remains accepted as an explicit spelling of the default shared behavior.

## Build

The required .NET 8 SDK is selected by `global.json`.

```powershell
dotnet restore osu.Desktop.slnf
dotnet tool restore
dotnet build osu.Desktop.slnf -c Release
dotnet test osu.Game.Tests/osu.Game.Tests.csproj -c Release --filter "FullyQualifiedName~ModBPMAdjust|FullyQualifiedName~BPMCustomBuildPolicy"
dotnet test osu.Game.Rulesets.Osu.Tests/osu.Game.Rulesets.Osu.Tests.csproj -c Release --filter "FullyQualifiedName~ModBPMAdjust"
.\scripts\Publish-Release.ps1
```

The audited release artifacts are written to `artifacts/releases/`. Install with
the generated setup executable, or extract the Velopack portable ZIP and run:

```powershell
& ".\Kumori BPM.exe"
```

This is a custom client build. It cannot be installed as a mod DLL into the official osu!lazer release.

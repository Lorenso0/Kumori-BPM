# Kumori BPM release

This is an unofficial Windows x64 build based on osu!lazer
`2026.804.2-lazer`.

## What's new

- Updated the upstream osu!lazer base from `2026.726.0-lazer` to
  `2026.804.2-lazer`.
- Includes the latest upstream gameplay, editor, spectator, storyboard, input,
  localisation, and performance fixes.
- Preserves Kumori's BPM Adjust mod, post-mod star-rating profiles, updater,
  unranked safety policy, and isolated-profile option.
- Shows a live BPM/rate/duration/pitch/tempo preview in the mod settings.
- Keeps only active BPM tempo/pitch processors attached, preventing low-latency
  WASAPI crackle during track and audio-mode changes.
- Simplifies audio to five distinct treatments, adds an Adaptive quality mode,
  turns named pitch styles into presets, and makes beat accents explicit.
- Adds independent hitsound-pitch control and effective audio-quality warnings.
- Uses lazer's normal `osu` profile by default so existing settings, maps,
  collections, skins, scores, and replays carry over automatically.
- Keeps `--bpm-isolated` available for a separate `osu-bpm` testing profile.
- Clearly disables unsupported official realtime presence, multiplayer, and
  spectating while retaining API login, chat, browsing, and downloads.
- Existing BPM settings, local scores, replays, and presets remain compatible.

## Install

1. Download `KumoriBPM-win-Setup.exe` and its `.sha256` file.
2. Verify the SHA-256 checksum.
3. Run the installer. It installs per-user and does not require administrator
   access.

Kumori checks this GitHub repository for updates in the background. After an
update downloads, click its notification to restart and apply it.

For a self-contained installation, extract `KumoriBPM-win-Portable.zip` and run
`Kumori BPM.exe`. The portable package also supports automatic updates.

Users upgrading from a release older than `2026.711.0-kumori.3` must install or
extract an updater-enabled package manually once.

The executables are unsigned and may trigger Windows SmartScreen.

## Important safety information

- Official score submission is disabled for the whole client.
- BPM Adjust is unranked and unavailable in multiplayer.
- The default profile is shared with official lazer; never run both clients
  simultaneously, and back up the data directory before major version changes.
- Launch with `--bpm-isolated` when a separate testing profile is preferred.
- Friends do not see Kumori sessions as online because official realtime
  presence does not accept custom build hashes.
- Automatic updates are accepted only from the `Lorenso0/Kumori-BPM` GitHub
  release feed.

See `BPM_MOD.md` in the archive for complete usage and compatibility details.

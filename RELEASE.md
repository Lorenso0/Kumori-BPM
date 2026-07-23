# Kumori BPM release

This is an unofficial Windows x64 build based on osu!lazer
`2026.711.0-lazer`.

## What's new

- Fixed saved star-rating profiles not being found after updating or changing
  BPM audio/presentation settings that do not affect difficulty.
- Existing legacy calculation files are adopted and migrated automatically;
  the previously calculated ratings are activated without another full pass.
- Star-rating cache keys now contain only difficulty-affecting BPM settings, so
  future installations resolve the same profile consistently.
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
- The default profile shares data with official lazer. Back up your lazer data
  and never run both clients simultaneously.
- Launch with `--bpm-isolated` to use a separate `osu-bpm` profile.
- Automatic updates are accepted only from the `Lorenso0/Kumori-BPM` GitHub
  release feed.

See `BPM_MOD.md` in the archive for complete usage and compatibility details.

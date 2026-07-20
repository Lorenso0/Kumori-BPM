# Kumori BPM release

This is an unofficial, portable Windows x64 build based on osu!lazer
`2026.711.0-lazer`.

## Install

1. Download the `win-x64.zip` asset and its `.sha256` file.
2. Verify the ZIP's SHA-256 checksum.
3. Extract the ZIP into a new directory.
4. Run `osu!.exe`.

The executable is unsigned and may trigger Windows SmartScreen.

## Important safety information

- Official score submission is disabled for the whole client.
- BPM Adjust is unranked and unavailable in multiplayer.
- The default profile shares data with official lazer. Back up your lazer data
  and never run both clients simultaneously.
- Launch with `--bpm-isolated` to use a separate `osu-bpm` profile.
- This build does not self-update.

See `BPM_MOD.md` in the archive for complete usage and compatibility details.

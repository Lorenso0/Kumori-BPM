# Kumori BPM ruleset for the official osu! client

`osu.Game.Rulesets.Kumori` is an external custom ruleset for official osu!lazer.
It delegates the complete playfield, hit objects, scoring, input, skinning, editor,
and difficulty implementation to the official osu! ruleset, then adds Kumori's
target-BPM mod on top.

This project is not affiliated with or endorsed by ppy Pty Ltd. The osu! name
and branding belong to their respective owners.

## Included

- target BPM with decimal text entry and a 40–400 BPM convenience slider;
- saved BPM shortcuts in `kumori-bpm-presets.json`;
- Preserve Pitch, Adjust Pitch, Balanced, Adaptive, and Custom Pitch audio modes;
- Nightcore, Daycore, Chipmunk, and Deep custom-pitch shortcuts;
- independent Nightcore or metronome beat accents;
- hitsounds which follow playback rate, music pitch, or preserve pitch;
- optional DT/HT-style AR and OD scaling;
- a global inclusive star-range song filter using imported pre-mod stars or exact
  post-mod stars calculated and persisted per BPM/mod setup;
- SQLite-backed, shareable post-mod indexing with JSON migration, cancellation
  checkpoints, and lock-free cached carousel lookups;
- a separate resumable map-first builder for exact Hidden-neutral 220–270 BPM
  ratings using fixed DA AR10/HP0, without running lazer;
- limited active-play compatibility for memory readers: the in-flight score temporarily exposes
  osu!standard's mode ID while preserving BPM Adjust's custom acronym, target, settings, and exact
  playback rate, without mutating osu!'s live ruleset identity;
- local score, replay, and personal-preset serialisation through normal mod settings;
- all standard osu! mods alongside BPM Adjust.

The ruleset is unranked and has no official online ruleset ID. Scores remain local.
Translated mod data is restored before osu! imports the local score. Kumori always keeps its
custom-ruleset identity; spoofing the live online ID causes official song-select state corruption.

## Download and install

Kumori no longer ships a modified osu! client, installer, or portable client.
New [ruleset releases](https://github.com/Lorenso0/Kumori-BPM/releases/latest)
contain only the external ruleset and its checksum:

- [`osu.Game.Rulesets.Kumori.dll`](https://github.com/Lorenso0/Kumori-BPM/releases/latest/download/osu.Game.Rulesets.Kumori.dll)
- [`osu.Game.Rulesets.Kumori.dll.sha256`](https://github.com/Lorenso0/Kumori-BPM/releases/latest/download/osu.Game.Rulesets.Kumori.dll.sha256)

Close osu!, then copy `osu.Game.Rulesets.Kumori.dll` into the `rulesets`
directory inside the official osu! data folder. In osu!, open Settings and use
**Open osu! folder** to find that data folder. Create `rulesets` if it does not
already exist, replace any older Kumori DLL, then restart the official client
and select **Kumori** from the ruleset icons.

## Build from source

Run:

```powershell
./scripts/Publish-Ruleset.ps1
```

The compiled file is written to
`artifacts/rulesets/osu.Game.Rulesets.Kumori.dll` and installs the same way as
the release download.

After selecting Kumori, enable **Show converts** once in song select so standard maps
are available to the custom ruleset. The setting is remembered by osu!.

The star-range controls are inside **Mods → BPM Adjust**. Pre-mod filtering is
immediate. For post-mod filtering, press **Calculate maps** once for each new
BPM/mod setup; the exact profile is reused in later sessions.

## Shared 220–270 star database

Build the standalone Windows executable once with:

```powershell
./scripts/Publish-StarDB-Builder.ps1
```

Close lazer, then run:

```powershell
./artifacts/star-db-builder/Kumori.StarDB.Builder.exe
```

The builder automatically resolves `%APPDATA%\osu\storage.ini`, opens lazer's
`client.realm` read-only, and follows each beatmap record to its exact file in
the hashed Realm file store. It does not crawl for loose `.osu` files and it does
not run inside the game.

The Windows interface shows the detected Realm and output folders, lets either
folder be changed with a picker, and defaults to every logical CPU core. Press
**Start / Resume** to begin. The progress bar shows maps, ratings, speed, ETA,
resumed work, and unavailable maps. **Pause safely** finishes the active SQLite
batches before stopping; the next run resumes them automatically.

For a source-tree run without publishing first:

```powershell
./scripts/Run-StarDB-Builder.ps1
```

The standalone builder:

- calculates every whole-number BPM from 220 through 270, inclusive;
- calculates Hidden-neutral stars, with DA fixed to AR10/HP0;
- keeps **Scale map stats with BPM** disabled;
- loads each unique beatmap hash once, then reuses its difficulty calculator for
  all 51 profiles;
- uses every processor core and writes results in large SQLite
  transactions;
- resumes from fully completed maps after cancellation or restart;
- records unavailable maps as completed work so they do not loop forever.

The result is `kumori-star-ratings.db` in the selected output directory (the
detected lazer data directory by default). Completed builds checkpoint SQLite's write-ahead
log into that single file. Close osu! before copying it into the osu! data folder
or replacing an existing database. Beatmaps are identified by their portable
file hash, and profile keys include osu!'s difficulty-algorithm version, so patch
releases can share compatible values without silently reusing incompatible stars. Existing JSON
profiles under `kumori-star-ratings/` migrate into SQLite when first loaded.

HP and OD do not affect raw osu!standard star rating and therefore do not create
separate profiles. AR and CS do affect star rating and remain part of the profile
identity.

Hidden keeps its normal gameplay behaviour but does not change Kumori star rating,
so No Mod and Hidden share the same exact-rating profile.

Custom rulesets track osu! APIs and may need a rebuild after a client update.

## Publishing a ruleset release

Pushing a tag named `ruleset-v*` runs the GitHub Actions release workflow. It
builds and tests the ruleset, creates or updates the matching GitHub Release,
and attaches only `osu.Game.Rulesets.Kumori.dll` plus its SHA-256 checksum.

For a complete maintainer release, tell the coding agent `update.md`. The
[UPDATE.md](UPDATE.md) runbook instructs it to validate, update GitHub, create
the next versioned release, wait for the action, and verify the downloaded DLL.

## Licence

Kumori is distributed under the [MIT licence](LICENCE). Its dependencies retain
their respective licences.

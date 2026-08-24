# Kumori ruleset release runbook

## Trigger and authorisation

When the user says `update.md`, read this entire file and execute the complete
release workflow below. That phrase authorises the routine repository and
GitHub changes required for the release: validation, committing the intended
work, pushing the default branch, creating the next release tag, waiting for
GitHub Actions, and verifying the public assets.

Do not stop for confirmation between routine steps. This authorisation does not
cover unrelated destructive changes, deleting historical releases, overwriting
unrelated work, or publishing a release that failed validation.

## Release scope

Kumori is released only as `osu.Game.Rulesets.Kumori.dll` for the official
osu!lazer client.

- Do not restore or build the old custom osu! client.
- Do not publish `osu!.exe`, an installer, a portable client, a ZIP archive, or
  an updater package.
- Do not attach the standalone star-database builder unless the user explicitly
  requests it for that release.
- The release assets must be the ruleset DLL and its SHA-256 checksum. GitHub's
  automatic source archives are expected.

## 1. Preflight

1. Read the current request and inspect the entire worktree.
2. Preserve unrelated or unfinished user changes. Include only changes intended
   for this release.
3. Fetch `origin/kumori` and confirm the local `kumori` branch is based on the
   current remote tip. Integrate remote changes safely if necessary.
4. Inspect the latest `ruleset-v*` tag. Unless the user specifies a version,
   increment its semantic-version patch component (for example,
   `ruleset-v1.2.3` becomes `ruleset-v1.2.4`).
5. Confirm that the chosen tag does not exist locally or remotely.

## 2. Validate

1. Format any touched C# files.
2. Run:

   ```powershell
   dotnet test osu.Game.Rulesets.Kumori.Tests/osu.Game.Rulesets.Kumori.Tests.csproj -c Release --no-restore -warnaserror
   dotnet build osu.Game.Rulesets.Kumori/osu.Game.Rulesets.Kumori.csproj -c Release --no-restore -warnaserror
   ./scripts/Publish-Ruleset.ps1
   git diff --check
   ```

3. Confirm all tests pass, the Release build has no errors, and
   `artifacts/rulesets/osu.Game.Rulesets.Kumori.dll` has a fresh timestamp and
   SHA-256 hash.
4. Do not commit, tag, or release when validation fails. Diagnose and fix an
   in-scope failure first.

## 3. Update GitHub

1. Update `README.md` or other public documentation when installation,
   compatibility, features, or release behaviour changed.
2. Review the final diff and ensure generated `artifacts`, `bin`, and `obj`
   files are not staged.
3. Commit all intended release changes with a concise message.
4. Push the commit to `origin/kumori`.
5. Confirm GitHub's default branch now points to the pushed commit.

## 4. Create the release

1. Create an annotated tag using the version selected during preflight.
2. Push that tag to `origin`. The
   `.github/workflows/release.yml` action must build, test, attest, and publish
   the DLL release automatically.
3. Watch the action through completion. Do not treat a queued or running action
   as success.
4. If the action fails, inspect its logs, correct the cause, revalidate, and use
   a new patch tag rather than silently moving a published tag.

## 5. Verify the public release

1. Confirm the new release is neither a draft nor a prerelease and is now the
   repository's latest release.
2. Confirm its uploaded assets are exactly:

   - `osu.Game.Rulesets.Kumori.dll`
   - `osu.Game.Rulesets.Kumori.dll.sha256`

3. Download both assets from GitHub.
4. Hash the downloaded DLL and confirm it matches the published checksum.
5. Report the commit, tag, release URL, workflow URL, asset URLs, test result,
   DLL size, and verified SHA-256 hash to the user.

`UPDATE.md` is this private operational trigger/runbook. It must never be used
as the public GitHub Release notes. Release notes are generated from the commits
included since the previous tag.

# Contributing to Kumori

Kumori is a narrowly scoped fork of osu!lazer. Contributions should relate to
BPM Adjust, custom-client safety, Kumori packaging, or keeping those changes
compatible with a newer upstream release.

General osu! bugs and features should be reported or contributed to
[ppy/osu](https://github.com/ppy/osu).

## Before opening a pull request

1. Base the change on the active Kumori branch.
2. Keep upstream behavior unchanged unless the difference is required by the
   BPM feature or custom-client safety policy.
3. Add or update automated tests.
4. Build and run the focused tests:

```powershell
dotnet restore osu.Desktop.slnf
dotnet build osu.Desktop.slnf -c Release -warnaserror
dotnet test osu.Game.Tests/osu.Game.Tests.csproj -c Release --filter "FullyQualifiedName~ModBPMAdjust|FullyQualifiedName~BPMCustomBuildPolicy"
dotnet test osu.Game.Rulesets.Osu.Tests/osu.Game.Rulesets.Osu.Tests.csproj -c Release --filter "FullyQualifiedName~ModBPMAdjust"
```

Changes to score submission, multiplayer eligibility, API liveness handling,
or release packaging require explicit regression coverage. The client must fail
safely rather than risk submitting a custom score.

By contributing, you agree that your contribution is distributed under the
repository's MIT licence.

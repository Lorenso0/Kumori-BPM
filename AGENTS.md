# Repository instructions

## Required post-edit executable rebuild

After making any edit in this repository, rebuild the self-contained Windows
executable before handing the work back to the user:

```powershell
./scripts/Publish-Standalone.ps1
```

The task is not complete until the script succeeds and the root `osu!.exe` has
an updated timestamp and SHA-256 hash. Run relevant tests before this final
publish so the executable always contains the last verified source state.

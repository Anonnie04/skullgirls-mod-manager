# Contributing

Use Windows and the .NET 9 SDK. Run `powershell -File Build.ps1 -Test` from a checkout. The default test suite creates disposable fixtures; a game installation is not required.

See [Building and verification](docs/BUILDING.md) for the full build instructions,
or the [user guide](docs/USER_GUIDE.md) for installation and everyday use.

To opt into read-only checks against your installation, set `SKULLGIRLS_GAME_ROOT` to the folder containing `SkullGirls.exe` before running the tests. FFmpeg on PATH enables independent audio comparisons. Never add game archives, extracted game assets, local mods, downloads, settings, or backup profiles to Git.

UI checks require an installed game and an available Windows audio output device: run `dist/SkullgirlsModManager.exe --smoke-ui`. They use isolated disposable storage and silent samples for transport checks. They do not apply mods.

Keep archive validation and recovery behavior intact. Describe behavior changes and relevant checks in pull requests. This is a GPL-3.0 derived project; retain upstream attribution and dependency license notices.

## Preparing a release

1. Update the version in `App/App.csproj` and release notes in `CHANGELOG.md`.
2. Stage the intended source changes with Git, then run `powershell -File Package.ps1`.
3. Distribute both ZIP files in `artifacts/`: the Windows build and matching source. Keep the notices and `licenses/` directory in the Windows build.

The Windows ZIP requires the .NET 9 Desktop Runtime. Extract it to a writable folder, then run `SkullgirlsModManager.exe`. It creates `Mods/` and `Data/` beside the executable. GitHub Actions builds the same packages as downloadable workflow artifacts; it does not publish releases automatically.

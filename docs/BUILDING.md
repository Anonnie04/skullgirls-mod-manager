# Building and verification

[Back to the README](../README.md)

For the native Linux graphical app, see [Linux desktop build](LINUX.md).

## Build from source

Use Windows and the .NET 9 SDK. The first build restores the NuGet packages for
IO and DDS encoding dependencies. From the source checkout, run:

```powershell
powershell -File Build.ps1 -Test
```

This runs verification and publishes the app to `dist/`. Open
**dist/SkullgirlsModManager.exe** after building. **Launch Skullgirls Mod Manager.cmd**
also builds and launches from a checkout. To build without running tests:

```powershell
powershell -File Build.ps1
```

The published app uses the .NET 9 Desktop Runtime.

## Verification

The console suite uses disposable game fixtures to check import, load order, archive replacement/addition, payload preservation, rollback after a locked second archive, persisted crash recovery, restore and hash guards. When `SKULLGIRLS_GAME_ROOT` is set to your installation folder, it also reads the game archive indexes and repacks copies of `core.gfs` under test output, including a byte-identical no-change round trip. Actual installed archives are never test destinations.

`dist/SkullgirlsModManager.exe --smoke-ui` renders screenshots and checks connection, mod rows, asset previews, single-file drop events, automatic mod creation and PNG-to-DDS staging using a fresh isolated `UiSmokeData/run-*` directory. It validates playback without playing sound or applying mods. Core tests check exact path routing, revision history, incompatible-file rejection and texture format/mip preservation. Audio tests compare exact PCM samples with FFmpeg when available. Sample DDS/PCX outputs have also been compared with Pillow.

`dist/SkullgirlsModManager.exe --smoke-review` runs the connection and library review checks without the audio/asset browser suite: update and rollback dialogs, explicit ID mapping, contributor navigation, automatic winner refresh after reordering, compact-window visibility, read-only recovery status, and named profile picker/review/selection restoration. Both smoke modes use isolated library/profile data and never apply mods. Set `SKULLGIRLS_GAME_ROOT` if automatic game discovery is unavailable. Review screenshots are saved as `mod-update-preview.png`, `conflicts-preview.png`, `recovery-preview.png` and `profiles-preview.png`.

Core profile checks cover persistence, missing mods, version changes, reimports, explicit overwrite, stale reviews, disabled-by-default new library entries, failed marker/order writes and interrupted-switch recovery. Named profiles share the library and never modify installation backups or baseline history.

Preview format references: [Microsoft DDS guide](https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dx-graphics-dds-pguide), [Microsoft ADPCM overview](https://learn.microsoft.com/en-us/windows/win32/xaudio2/adpcm-overview).

## Release packaging

See [Contributing](../CONTRIBUTING.md#preparing-a-release) for versioning and
`Package.ps1`. GitHub Actions builds Windows and matching source ZIPs as workflow
artifacts; it does not publish releases automatically. No game files or mods are included.

Source is GPL-3.0; the copied GFS library retains its MIT license. Include the
matching source and dependency notices when distributing builds. See
[Third-party notices](../THIRD-PARTY-NOTICES.md).

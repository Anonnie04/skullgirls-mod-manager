# Skullgirls Mod Manager

A native Windows GUI adapted from Velvet Beautifier for local Skullgirls GFS replacement mods. Source reuse and licenses are documented in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Launch

For a Windows release, extract the ZIP to a writable folder and run **SkullgirlsModManager.exe**. Install the .NET 9 Desktop Runtime if prompted. From a source checkout, double-click **Launch Skullgirls Mod Manager.cmd** to build and launch, or run **dist/SkullgirlsModManager.exe** after building.

The provided build uses the installed .NET 9 Desktop Runtime. Building requires the .NET 9 SDK and an initial NuGet restore for the IO and DDS encoding dependencies. Run `powershell -File Build.ps1` to rebuild, or `powershell -File Build.ps1 -Test` to run verification first.

## Use

1. Connect the folder containing `SkullGirls.exe` and `data01`. Steam libraries are searched automatically; use Browse if the game is elsewhere.
2. Import a replacement **folder** or **ZIP**. New imports are disabled. Enable the mods you want with the checkboxes; use Move up/down for order. Later mods win conflicts.
3. Click **Preview changes** to review affected archive entries. Burgundy override rows replace an earlier mod's asset.
4. Close Skullgirls and click **Apply enabled mods**. The application stages and verifies every affected archive before installing anything. It keeps backups per installation.
5. **Restore originals** reinstalls the backups captured before this manager first changed those archives. Enabled selections are retained. Disabling/removing mods and applying also removes their previously applied changes.

Use **New mod**, **Game archives > Preview assets**, or double-click an archive to open the asset browser. Switch archives, search paths or filter by type, then select an asset. New mod opens story artwork first and lets you build the mod by dropping replacements.

## Preview → drop → mod

1. In **Game archives > Preview assets**, select the asset you want to replace.
2. Leave **Save replacements to** on **Create a new mod** and enter its name, or select an existing mod.
3. Drop **one file** onto the preview or the dashed drop area. **Choose file** does the same thing.
4. The app creates `mod.json`, names the replacement after the selected asset, and puts it under the correct `archive-name.gfs/internal/path` automatically. It selects the new mod so further drops go into that same mod, including assets in other archives.
5. The preview switches to **STAGED REPLACEMENT**. Use **Show original / Show replacement** to compare. **Open mod folder** opens Windows Explorer at the mod folder. Before the first drop, it creates the named empty mod, selects it and opens its folder immediately.
6. Close the browser, enable the new mod, review **Preview changes**, then **Apply enabled mods**.

New mods start disabled. Dropping or choosing a file never applies it to the game. Existing mods retain their enabled selection; use Apply to install updated replacements. If you overwrite a staged asset, its previous copy is kept in `Data/replacement-history`.

For a DDS asset, drop a matching DDS or a **PNG/JPEG/BMP**: the app converts ordinary images to the original DXT1/DXT3/DXT5 or RGB16/24/32 format and mip count. A size mismatch offers a resize confirmation; fitting the full image can change proportions. Inspect the staged preview before applying. Native DDS files must already match size, compression and mip count. PCX replacements require matching dimensions. WAV files must use the original sample rate and channel count (duration may differ); other assets need the native extension, with common text extensions interchangeable. File validation checks structure, not every game-specific semantic constraint. No MP3 conversion, bank editing or sprite conversion is performed.

## Asset previews

The character dropdown filters the selected archive by roster character and shows counts. It keeps your character selection when changing archives if that character has assets there; otherwise it resets to **All characters**. **Other / shared** contains paths with no recognized roster name. Story chapter folders and named guests both count; names are inferred from paths, not image content. Search also accepts display names such as **Big Band** and **Ms. Fortune**. Assets are grouped by character with numeric filename order (palette 2 before 10); click the path or character column to change sorting.

Audio previews support **Play**, **Pause / Resume**, **Stop**, and **Loop**. Click the waveform to seek, including while paused or before starting playback. The playhead and time display follow the output device's PCM position. Stop resets to the beginning. Loop restarts the complete track after reaching the end; it is intended for previewing rather than sample-perfect loop editing.

For a selected DDS, **Save PNG** exports the full-resolution decoded image (including transparency, without checkerboard or preview scaling). It exports whichever version is currently displayed: original or staged replacement. **PNG → DDS** selects a PNG and saves a separate DDS using the selected game texture's dimensions, compression/pixel masks and mip count. A size mismatch offers resizing. Supported targets are DXT1/3/5 and uncompressed RGB16/24/32 2D DDS. The converted DDS can then be dropped onto the asset to stage it in a mod. DXT recompression is lossy; inspect the replacement before applying.

- **Images:** DDS DXT1/3/5 and RGB16/24/32 (including RGB565), indexed/RGB PCX, PNG/JPEG/BMP. DDS displays the first mip level. Use Fit or 100% (drag to pan), and checker/dark/light backgrounds for transparency. These are individual textures/art rather than assembled stages or UI screens.
- **Audio:** PCM8/16 and Microsoft ADPCM WAV, including installed music and dialogue. Waveform, duration, format, Play/Pause/Resume/Stop, click-to-seek and Loop. No autoplay; changing selection or closing the browser stops playback.
- **Text:** configuration, Lua, JSON and shaders, read-only, first 1 MB; UTF-8 and UTF-16 BOM support.
- **Other formats:** bounded hex/ASCII header preview. Sound banks, stage meshes and compiled UI formats are not expanded.

Limits: images 32 megapixels / 64 MB input; audio 128 MB input and 10 minutes / 32 million decoded samples. Large or unsupported assets show a header view. **Save original** exports one original asset outside the game installation.

Supported fighting-character entries in `characters-art.gfs`, `characters-art-pt.gfs` and `characters-foits.gfs` show a clickable link to [Skullgirls Sprite Workshop](https://github.com/Anonnie04/skullgirls-sprite-workshop). This scope matches the local Workshop's character roster, art banks, metadata and older `temp/` aliases. The Workshop reads palettes from `sprites.gfs` as supporting data; this does not make the whole archive Workshop-only. Standalone DDS textures, story portraits and cutscenes are decoded here and can use the regular replacement workflow. This app does not modify the Workshop.

The interface uses Skullgirls-inspired Art Deco styling: charcoal, burgundy, cream lettering, brass-gold borders and geometric dividers. It does not copy game graphics into the UI.

## Replacement mod layout

```text
My Mod/
  mod.json
  core.gfs/
    core/
      music.ini
  ui.gfs/
    [exact internal path to a changed asset]
```

`data01/archive-name.gfs/...` is also supported. `.gfs` names in a replacement mod are **directories**, with only the changed assets inside. A folder with this layout but no metadata receives generated metadata on import. A ZIP may wrap a single mod folder.

Velvet `mod.json` metadata is reused:

```json
{
  "id": "local.my-mod",
  "name": "My Mod",
  "author": "Your name",
  "desc": "What the mod changes",
  "version": "1.0",
  "url": null
}
```

Each ID must be unique. To replace an imported version, remove it from the library and import the new version. Removed imports are retained under `Data/removed-mods`.

## Storage and recovery

`Mods/` beside the launcher is the default mod library. The app scans its mod subfolders at startup, creates new mods there, and starts folder/ZIP import dialogs there. Click **YOUR MODS • OPEN FOLDER** to open it. Existing `Data/mods` is migrated on the first updated launch, preserving load order and enabled markers. `Data/` contains settings and installation-specific backup profiles. Keep this directory: it contains the originals needed for restore. No TFH files or Velvet settings are shared.

Applying uses a persistent transaction journal, temporary files next to the game archives, atomic per-file replacement and rollback copies. A failure rolls back already-installed archives. After a process interruption, use **Backups & tools > Recover interrupted operation** before applying or launching from the manager. Keep enough free space for backups and staged copies; the apply review gives a rough allowance.

If the current archive differs from the manager's last installed hash, or a backup fails its checksum, apply stops before committing. This protects game updates and external mods. This version does not automatically adopt a new Steam build: keep the existing Data directory for recovery, verify/reinstall the game's files with Steam, and archive the relevant profile outside `Data/profiles` before starting a fresh baseline. A first baseline is a copy of the files currently installed; it is not checked against official Steam hashes.

## Scope

- Windows GUI; GFS version 1.1, ASCII internal paths.
- Folder/ZIP replacement imports, explicit load order, conflict preview, streaming repack, full payload checks, backups and restore.
- New GFS entries can be packed correctly, but the game must already know how to reference them. Packing does not register characters, stages or assets with game scripts.
- No sprite/animation/palette editor, character registration, TFHRES support, TFH level packs, executable patches, online downloads or GameBanana integration.
- Packed GFS files are not accepted directly as replacement mods. Extract them first and package changed assets in the layout above.
- Archive and filesystem behavior are tested; effects of individual mod assets still require testing in-game. An image replacement mod has been tested in-game by the user; individual replacements still need their own checks.

## Verification

The console suite uses disposable game fixtures to check import, load order, archive replacement/addition, payload preservation, rollback after a locked second archive, persisted crash recovery, restore and hash guards. When `SKULLGIRLS_GAME_ROOT` is set to your installation folder, it also reads the game archive indexes and repacks copies of `core.gfs` under test output, including a byte-identical no-change round trip. Actual installed archives are never test destinations.

`dist/SkullgirlsModManager.exe --smoke-ui` renders screenshots and checks connection, mod rows, asset previews, single-file drop events, automatic mod creation and PNG-to-DDS staging using a fresh isolated `UiSmokeData/run-*` directory. It validates playback without playing sound or applying mods. Core tests check exact path routing, revision history, incompatible-file rejection and texture format/mip preservation. Audio tests compare exact PCM samples with FFmpeg when available. Sample DDS/PCX outputs have also been compared with Pillow.

Preview format references: [Microsoft DDS guide](https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dx-graphics-dds-pguide), [Microsoft ADPCM overview](https://learn.microsoft.com/en-us/windows/win32/xaudio2/adpcm-overview).

Source is GPL-3.0; the copied GFS library retains its MIT license. When distributing builds, include source and third-party notices as required by those licenses.


See [CONTRIBUTING.md](CONTRIBUTING.md) for optional game checks, UI checks, and release packaging. Windows builds and matching source ZIPs are produced by the GitHub Actions workflow; no game files or mods are included.

# Mod Manager user guide

[Back to the README](../README.md)

- [Set up the manager](#set-up-the-manager)
- [Install and manage mods](#install-and-manage-mods)
- [Named mod profiles](#named-mod-profiles)
- [Create a mod from an asset preview](#create-a-mod-from-an-asset-preview)
- [Browse and preview assets](#browse-and-preview-assets)
- [Mod file layout](#mod-file-layout)
- [Storage, backups and recovery](#storage-backups-and-recovery)
- [Scope and limitations](#scope-and-limitations)

## Set up the manager

Extract the full Windows release ZIP to a writable folder and run
**SkullgirlsModManager.exe**. Install the .NET 9 Desktop Runtime if prompted.
The manager creates **Mods** and **Data** beside the executable.

### One-time mod support setup

Some game builds verify `core.gfs`, `characters-foits.gfs`, and `ui.gfs` at startup. Use **Backups & tools → Set up mod support** once and select the supported `CoreMessaging.dll` or a ZIP containing it. Import your actual mod separately with **Import mod**. The manager verifies the loader checksum and saves a copy in its Data profile, outside the game folder.

After reinstalling the game, enable your mods and click **Apply**: saved support is restored automatically. The setup button also restores support without selecting a file again. Keep the manager's Data folder when reinstalling the game.

Unknown DLLs are never overwritten. The loader is user-supplied, not bundled with the app. Restore originals restores assets and leaves mod support available.

## Install and manage mods

1. Connect the folder containing `SkullGirls.exe` and `data01`. Steam libraries are searched automatically; use Browse if the game is elsewhere.
2. Import a replacement **folder** or **ZIP**. New imports are disabled. Enable the mods you want with the checkboxes; use Move up/down for order. Later mods win conflicts.
3. Click **Preview changes** to review affected assets. Burgundy rows mark conflicting files and name the final winning mod.
4. Close Skullgirls and click **Apply enabled mods**. The application stages and verifies every affected archive before installing anything. It keeps backups per installation.
5. **Restore originals** reinstalls the saved archive baselines. These start as the files captured before the manager's first change; Update archive can explicitly adopt a newer baseline. Enabled selections are retained. Disabling/removing mods and applying removes changes made over the saved baselines.

Use **New mod**, **Game archives > Preview assets**, or double-click an archive to open the asset browser. Switch archives, search paths or filter by type, then select an asset. New mod opens story artwork first and lets you build the mod by dropping replacements.

### Update an imported mod

Select a library entry, then choose **Update mod… > Update from folder…** or **Update from ZIP…**. Choose the complete replacement package. Review the old and new versions and the added, changed and removed assets, then click **Update library**. Enabled status, selection and load-order position are retained. Files absent from the new package are removed from the library entry. Validation failures and cancelled reviews leave the current package intact.

Packages should retain their mod ID. If the ID differs, explicitly check the mapping option to replace the selected entry; an ID already used by another library entry is rejected. Packages without mod.json receive a generated local ID and also need this mapping.

**Update mod… > Restore previous version…** previews the most recently retained package and its saved enabled status. Confirm **Restore version** to restore it at the same load-order position. Each replacement retains the outgoing package under `Mods/.versions`, so the next restore can undo the last version switch. Keep this folder for rollback. An interrupted package swap is recovered automatically when the manager next opens the library.

Updating or restoring a library version does not change the game. Use **Preview changes**, then **Apply enabled mods** to install the selected contents. No external editor is required.

### Named mod profiles

Use **Profiles…** under the library to save named sets of enabled mods and load order. **Save current…** captures the current library selection; enter a name and confirm replacement if that name already exists. Later checkbox and order changes do not automatically overwrite the saved profile. Save your current setup before switching if you want to return to it.

Select a saved profile and choose **Preview switch…**. The review shows the proposed order and enabled state, saved and current package versions, changed mod IDs, and missing entries. Confirm **Switch library selection** to load it. Available mods retain their saved relative order. Missing entries are omitted; mods added since the profile was saved move to the end and are disabled. A reimport can be matched by its mod ID when that match is unambiguous.

Profiles reference the shared library. They do not duplicate packages, pin old package contents, or copy game backups. Version warnings compare the packages' declared version labels; use **Update mod… > Restore previous version…** if you need an earlier retained package. Switching profiles does not install anything: use **Preview changes**, then the usual **Apply enabled mods** action.

Profiles are saved in `Mods/.mod-profiles.json` alongside the shared library. Keep that file and the library together. Profiles are separate from installation-specific baseline state and recovery history; switching never adopts a new baseline, restores game files, or discards history. An interrupted library-selection switch restores the previous enabled settings and order when the manager next opens the library.

### Explain conflicts

In **Change preview**, select an asset to see every enabled mod contributing to its exact archive and asset path. The contributor table shows library load-order positions, overridden files and the final winner. Later enabled mods win; the complete winning file replaces earlier contributions without merging them. Use **Go to selected mod** or double-click a contributor to select its library entry and open its details. Mods with identical display names still navigate to their own entries. Loose artwork uses the same explanation.

Move a mod up or down while a preview is displayed to refresh the preview and winner automatically. Changing enabled selections clears the old preview; click **Preview changes** again.

Recognized VRSN 9 character metadata gets a warning about whole-file replacement of its palette-name table and art memory sizes. Check the winning metadata against the enabled palette and art packages. Unrecognized metadata retains file-level precedence information. Successful repacking does not establish semantic compatibility, and these checks do not depend on an external editor.

### Active archive folder

With mod support installed and `data02` present, Apply and Restore target only `data02`, using its separate backups. `data01` is left unchanged. The confirmation shows the target archive folder.

## Create a mod from an asset preview

1. In **Game archives > Preview assets**, select the asset you want to replace.
2. Leave **Save replacements to** on **Create a new mod** and enter its name, or select an existing mod.
3. Drop **one file** onto the preview or the dashed drop area. **Choose file** does the same thing.
4. The app creates `mod.json`, names the replacement after the selected asset, and puts it under the correct `archive-name.gfs/internal/path` automatically. It selects the new mod so further drops go into that same mod, including assets in other archives.
5. The preview switches to **STAGED REPLACEMENT**. Use **Show original / Show replacement** to compare. **Open mod folder** opens Windows Explorer at the mod folder. Before the first drop, it creates the named empty mod, selects it and opens its folder immediately.
6. Close the browser, enable the new mod, review **Preview changes**, then **Apply enabled mods**.

New mods start disabled. Dropping or choosing a file never applies it to the game. Existing mods retain their enabled selection; use Apply to install updated replacements. If you overwrite a staged asset, its previous copy is kept in `Data/replacement-history`.

### Replacement formats

For a DDS asset, drop a matching DDS or a **PNG/JPEG/BMP**: the app converts ordinary images to the original DXT1/DXT3/DXT5 or RGB16/24/32 format and mip count. A size mismatch offers a resize confirmation; fitting the full image can change proportions. Inspect the staged preview before applying. Native DDS files must already match size, compression and mip count. PCX replacements require matching dimensions. WAV files must use the original sample rate and channel count (duration may differ); other assets need the native extension, with common text extensions interchangeable. File validation checks structure, not every game-specific semantic constraint. No MP3 conversion, bank editing or sprite conversion is performed.

## Browse and preview assets

### Characters and searching

The character dropdown filters the selected archive by roster character and shows counts. It keeps your character selection when changing archives if that character has assets there; otherwise it resets to **All characters**. **Other / shared** contains paths with no recognized roster name. Story chapter folders and named guests both count; names are inferred from paths, not image content. Search also accepts display names such as **Big Band** and **Ms. Fortune**. Assets are grouped by character with numeric filename order (palette 2 before 10); click the path or character column to change sorting.

### Audio playback

Audio previews support **Play**, **Pause / Resume**, **Stop**, and **Loop**. Click the waveform to seek, including while paused or before starting playback. The playhead and time display follow the output device's PCM position. Stop resets to the beginning. Loop restarts the complete track after reaching the end; it is intended for previewing rather than sample-perfect loop editing.

### Export and convert textures

For a selected DDS, **Save PNG** exports the full-resolution decoded image (including transparency, without checkerboard or preview scaling). It exports whichever version is currently displayed: original or staged replacement. **PNG → DDS** selects a PNG and saves a separate DDS using the selected game texture's dimensions, compression/pixel masks and mip count. A size mismatch offers resizing. Supported targets are DXT1/3/5 and uncompressed RGB16/24/32 2D DDS. The converted DDS can then be dropped onto the asset to stage it in a mod. DXT recompression is lossy; inspect the replacement before applying.

### Supported previews and limits

- **Images:** DDS DXT1/3/5 and RGB16/24/32 (including RGB565), indexed/RGB PCX, PNG/JPEG/BMP. DDS displays the first mip level. Use Fit or 100% (drag to pan), and checker/dark/light backgrounds for transparency. These are individual textures/art rather than assembled stages or UI screens.
- **Audio:** PCM8/16 and Microsoft ADPCM WAV, including installed music and dialogue. Waveform, duration, format, Play/Pause/Resume/Stop, click-to-seek and Loop. No autoplay; changing selection or closing the browser stops playback.
- **Text:** configuration, Lua, JSON and shaders, read-only, first 1 MB; UTF-8 and UTF-16 BOM support.
- **Other formats:** bounded hex/ASCII header preview. Sound banks, stage meshes and compiled UI formats are not expanded.

Limits: images 32 megapixels / 64 MB input; audio 128 MB input and 10 minutes / 32 million decoded samples. Large or unsupported assets show a header view. **Save original** exports one original asset outside the game installation.

### Sprite Workshop integration

Supported fighting-character entries in `characters-art.gfs`, `characters-art-pt.gfs` and `characters-foits.gfs` show a clickable link to [Skullgirls Sprite Workshop](https://github.com/Anonnie04/skullgirls-sprite-workshop). This scope matches the local Workshop's character roster, art banks, metadata and older `temp/` aliases. The Workshop reads palettes from `sprites.gfs` as supporting data; this does not make the whole archive Workshop-only. Standalone DDS textures, story portraits and cutscenes are decoded here and can use the regular replacement workflow. This app does not modify the Workshop.

### Imported sprite packages

When importing a sprite package with VRSN 9 character metadata, the manager grows advertised art memory sizes to cover the supplied art banks. Some relocated Workshop packages retain the old sizes even though replacement art is appended. Only the two size fields are adjusted; other metadata and the source ZIP stay unchanged. Adjustments are listed in `sprite-storage-repairs.json` inside the imported mod folder. Existing imported mods need to be reimported to receive this correction. Applying and restoring still use the normal verified archive transaction.

The interface uses Skullgirls-inspired Art Deco styling: charcoal, burgundy, cream lettering, brass-gold borders and geometric dividers. It does not copy game graphics into the UI.

## Mod file layout

Digital Art Compendium PNG replacements can be installed from `loose/Digital Art Compendium/...png` inside a mod. The same mod may also contain GFS replacements. Loose-file mods import from folders or ZIPs, appear in change previews, obey load order, and use the same transaction as archive changes. Existing artwork is backed up; disabling a mod or using Restore originals restores replaced files and removes files that were originally absent. Files outside the mod's list remain unchanged. External edits and changed backups block installation. Other loose-file types and destinations are not supported.

```text
My Mod/
  mod.json
  core.gfs/
    core/
      music.ini
  loose/
    Digital Art Compendium/
      Character Art/
        example.png
  ui.gfs/
    [exact internal path to a changed asset]
```

`data01/archive-name.gfs/...` is also supported. `.gfs` names in a replacement mod are **directories**, with only the changed assets inside. A folder with this layout but no metadata receives generated metadata on import. A ZIP may wrap a single mod folder.

### Mod details

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

## Storage, backups and recovery

`Mods/` beside the launcher is the default mod library. The app scans its mod subfolders at startup, creates new mods there, and starts folder/ZIP import dialogs there. Click **YOUR MODS • OPEN FOLDER** to open it. Existing `Data/mods` is migrated on the first updated launch, preserving load order and enabled markers. `Data/` contains settings and installation-specific backup profiles. Keep this directory: it contains the originals needed for restore. No TFH files or Velvet settings are shared.

### Interrupted operations

Applying uses a persistent transaction journal, temporary files next to the game archives, atomic per-file replacement and rollback copies. A failure rolls back already-installed archives. After a process interruption, use **Backups & tools > Recover interrupted operation** before applying or launching from the manager. This also completes an interrupted archive-baseline update after checking the preserved snapshots and current game file. Keep enough free space for backups and staged copies; the apply review gives a rough allowance.

### Inspect installation and recovery status

Open **Backups & tools** or click **Refresh status** to check the connected installation's tracked archives and loose files against saved hashes. The view identifies pending installation or baseline-update journals, externally changed files, missing files, missing or damaged original backups, and the number of retained baseline history entries. Untracked files have no saved baseline to verify. Verification can take time for large archives and reports a point-in-time result; Apply and recovery still perform their own checks.

For an interrupted operation, use the existing **Recover interrupted operation** action. The status view does not label in-flight transaction changes as external edits. For an externally changed archive, review it in **Game archives** before choosing **Update archive…**. Inspect backup problems before applying or restoring; opening **Open backup folder** or **Open baseline history** does not change game files.

**Restore originals** installs the saved baseline and removes mod-added files. **Update archive…** instead keeps the installed archive as the new baseline, including customizations already present, and preserves the old backup and state in history. Merely checking status performs neither action.

### Changed archives and baseline updates

If the current archive differs from the manager's last installed hash, or a backup fails its checksum, apply stops before committing. To keep a valid archive changed by Steam or another tool, close the game, select it in **Game archives**, and click **Update archive…**. Review and confirm to use the installed file as the new baseline. The game file is not modified. Existing customizations become part of that baseline, so disabling mods or using Restore originals will retain them. Recheck mod compatibility after game updates and preview before applying.

The previous backup, complete profile state, and adopted file snapshot are preserved in a dated directory under the profile's `baseline-history`. Open it through **Backups & tools > Open baseline history**. Do not delete history or the Data directory while reviewing or recovering an update. Baselines are copies of installed files; they are not checked against official Steam hashes. Updating a baseline refuses corrupted previous backups, stale confirmations, and unresolved transactions.

## Scope and limitations

- Windows GUI; GFS version 1.1, ASCII internal paths.
- Folder/ZIP replacement imports, explicit load order, conflict preview, streaming repack, full payload checks, backups and restore.
- New GFS entries can be packed correctly, but the game must already know how to reference them. Packing does not register characters, stages or assets with game scripts.
- No sprite/animation/palette editor, character registration, TFHRES support, TFH level packs, executable patches, online downloads or GameBanana integration.
- Packed GFS files are not accepted directly as replacement mods. Extract them first and package changed assets in the layout above.
- Archive and filesystem behavior are tested; effects of individual mod assets still require testing in-game. An image replacement mod has been tested in-game by the user; individual replacements still need their own checks.

For build instructions and verification details, see [Building from source](BUILDING.md).

# Changelog

## 0.4.1

- Add optional replacement path groups within a mod, with an Options button beside matching mods in both Windows and Linux interfaces.
- Persist component selections across restarts, Windows named profiles and package updates; disabled components use normal preview, conflict and restore handling.
- Import AOTC standalone schema 1 ZIPs directly, verifying replacement checksums and converting optional modules into toggles within the same mod. The optional announcer starts disabled; installer scripts and loader DLLs are excluded.
- Add Build-All.ps1 to publish Windows x64 and self-contained Linux x64 builds from one checkout.

## 0.4.0

- Add a native Linux x64 graphical app with an Art Deco skull crest, geometric borders, and burgundy-and-gold styling.
- Discover and reconnect to standard, Snap, Flatpak and additional Steam libraries automatically.
- Import existing Windows GFS replacement and loose-artwork ZIPs unchanged, with enable controls, ordering, reviews, backups, restore and recovery.
- Add reversible native archive support for one audited Linux executable, with exact before/after checksums, original backup, permission preservation and unknown-build rejection.
- Normalize Windows ZIP separators and enforce case-sensitive Unix path containment.
- Add Linux builds, release packaging and native compatibility regression checks.
- AOTC 0.15.1 was applied and confirmed working with the supported native Linux build. Windows DLL plugins are not translated.

## 0.3.17

- Save named mod profiles with shared-library enabled selections and load order; review missing mods, changed versions and changed IDs before switching the library selection.
- Append unsaved new mods disabled, resolve unambiguous reimports by ID, and recover failed or interrupted selection switches without changing installation baselines or restore history.

- Explain conflicts per asset with all contributing mods, load-order positions and final winner; navigate directly to each library entry and refresh the winner after reordering.
- Warn about palette and associated art effects for recognized VRSN 9 character metadata, retaining file-level explanations for unsupported content.
- Show read-only installation recovery status, external changes, backup verification problems and baseline history through Backups & tools, using existing recovery and baseline actions.
- Make UI smoke startup deterministic and verify update/rollback reviews, explicit ID mapping, conflict navigation and compact layouts.
- Update imported mods from complete folders or ZIPs while preserving enabled status, selection and load order.
- Review versions and added, changed and removed assets; require explicit mapping for a different mod ID.
- Retain previous packages and enabled settings for version rollback, reject packages changed after preview, and recover interrupted library swaps on startup.

## 0.3.16

- Grow VRSN 9 character art memory sizes on import when a sprite package supplies larger art banks with stale FOIT sizes.
- Preserve other metadata and original ZIPs; record each size adjustment in the imported mod's sprite-storage-repairs.json. Imports remain disabled until selected.

## 0.3.15

- Revert dual-folder writes: use only the active data02 archives with mod support.
- Show the selected archive folder in the apply confirmation.

## 0.3.14

- Apply mods to both data01 and the compatibility loader's data02 folder, with separate backups and restore state.
- Show changes for the second folder in previews, including restores.

## 0.3.13

- Save verified mod support outside the game folder and restore it automatically on Apply after a game reinstall.
- Replace the compatibility file picker entry with Set up mod support, with a one-time explanation and saved-file recovery.
- Allow reapplying mods when reinstalled archives match their verified original backups.

## 0.3.12

- Add Update archive in Game archives to explicitly adopt the installed archive as the new baseline without modifying the game file.
- Preserve the previous baseline and profile state in dated baseline history, with checksum validation and recovery for interrupted updates.
- Keep external-change protections and require a fresh review if the archive or profile changes after confirmation.

## 0.3.11

- Use generic compatibility instructions and accept supported loader ZIPs regardless of their folder layout.

## 0.3.10

- Detect protected-archive modifications that require runtime verification compatibility.
- Block Apply with actionable instructions when the supported loader is missing.
- Install the checksum-pinned compatibility loader from the user's ZIP through Backups & tools, without replacing existing different loaders or changing the executable.

## 0.3.9

- Install Digital Art Compendium PNG mods from folders or ZIPs with conflict previews and load order.
- Back up and restore loose artwork; remove mod-added files when disabled.
- Apply archives and loose artwork in one recoverable transaction, with checksum guards and interrupted-operation recovery.

## 0.3.8

- Native Windows mod library with ordered folder/ZIP replacements, change preview, verified backups, restore, and interrupted-operation recovery.
- DDS/PCX image, text, and WAV previews; PNG export and PNG-to-DDS conversion.
- Drag-and-drop replacement staging with automatic archive paths and revision history.
- Character filtering, numeric asset sorting, and links to the separate Sprite Workshop for supported fighting-character packages.
- Audio waveform with device-position playhead, pause/resume, click-to-seek, stop, and loop.
- Dedicated Mods folder, default import location, and migration of the previous library.
- Portable Steam library discovery and fixture-based Windows build checks.

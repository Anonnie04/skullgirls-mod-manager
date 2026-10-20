# Changelog

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

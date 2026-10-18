# Changelog

## 0.3.10

- Detect protected-archive modifications that require runtime verification compatibility.
- Block Apply with actionable instructions when the supported loader is missing.
- Install the checksum-pinned AOTC loader from the user's ZIP through Backups & tools, without replacing existing different loaders or changing the executable.

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

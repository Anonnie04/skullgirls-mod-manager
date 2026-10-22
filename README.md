# Skullgirls Mod Manager

A native Windows manager for Skullgirls replacement mods, adapted from
[Velvet Beautifier](https://github.com/ThemModdingHerds/velvet-beautifier).
Import mods, preview their changes, and apply them with verified backups.

[Download](https://github.com/Anonnie04/skullgirls-mod-manager/releases/latest) · [User guide](docs/USER_GUIDE.md) · [Report an issue](https://github.com/Anonnie04/skullgirls-mod-manager/issues)

- Import mod folders or ZIPs and control load order.
- Preview images, audio and text from game archives.
- Create replacement mods by dropping files onto asset previews.
- Review conflicts, apply enabled mods, and restore saved originals.

## Quick start

1. Extract the Windows ZIP and run **SkullgirlsModManager.exe**. Install the .NET 9 Desktop Runtime if prompted.
2. Choose your Skullgirls folder, import your mods, and enable the ones you want.
3. Review **Preview changes**, close Skullgirls, then **Apply enabled mods**.

Some mods require the one-time **Set up mod support** step. See the
[user guide](docs/USER_GUIDE.md) for setup, asset replacement, backups and recovery.
Keep the manager's **Data** folder; it contains your restore backups.

[Build from source](docs/BUILDING.md) · [Contributing](CONTRIBUTING.md) · [Changelog](CHANGELOG.md) · [Sprite Workshop](https://github.com/Anonnie04/skullgirls-sprite-workshop)

Unofficial tool; no game files or compatibility loader are bundled.
Source uses [GPL-3.0](LICENSE); the copied GFS library retains MIT.
Upstream attribution and dependency licenses are in [Third-party notices](THIRD-PARTY-NOTICES.md).

# Skullgirls Mod Manager

A mod manager for Skullgirls replacement mods, adapted from
[Velvet Beautifier](https://github.com/ThemModdingHerds/velvet-beautifier).
Import mods, preview their changes, and apply them with verified backups.

[Download](https://github.com/Anonnie04/skullgirls-mod-manager/releases/latest) · [User guide](docs/USER_GUIDE.md) · [Report an issue](https://github.com/Anonnie04/skullgirls-mod-manager/issues)

- Import mod folders or ZIPs and control load order.
- Save and switch named mod selections and load orders with profile reviews.
- Preview images, audio and text from game archives.
- Create replacement mods by dropping files onto asset previews.
- Review conflicts, apply enabled mods, and restore saved originals.

## Linux

Download the Linux x64 `.tar.gz` from Releases, extract it, and run
`./SkullgirlsModManager`. The app finds standard, Snap and Flatpak Steam
installations automatically. Import your existing mod ZIP unchanged, enable it,
set up native mod support if required, then preview and apply.

Linux includes an Art Deco desktop interface, archive and loose-artwork mod
installation, load order, verified backups, restoration and recovery. Asset
editing, audio previews, mod updates and named profile controls currently use
the Windows interface. Native compatibility setup supports one verified Linux
game executable; unknown versions are rejected. Windows DLL mods require a
native port. See [Linux instructions](docs/LINUX.md).

## Windows quick start

1. Extract the Windows ZIP and run **SkullgirlsModManager.exe**. Install the .NET 9 Desktop Runtime if prompted.
2. Choose your Skullgirls folder, import your mods, and enable the ones you want.
3. Review **Preview changes**, close Skullgirls, then **Apply enabled mods**.

Some mods require the one-time **Set up mod support** step. See the
[user guide](docs/USER_GUIDE.md) for setup, asset replacement, backups and recovery.
Keep the manager's **Data** folder on Windows, or `~/.local/share/SkullgirlsModManager` on Linux; it contains your restore backups.

[Linux desktop build](docs/LINUX.md) · [Build from source](docs/BUILDING.md) · [Contributing](CONTRIBUTING.md) · [Changelog](CHANGELOG.md) · [Sprite Workshop](https://github.com/Anonnie04/skullgirls-sprite-workshop)

Unofficial tool; no game files or compatibility loader are bundled.
Source uses [GPL-3.0](LICENSE); the copied GFS library retains MIT.
Upstream attribution and dependency licenses are in [Third-party notices](THIRD-PARTY-NOTICES.md).

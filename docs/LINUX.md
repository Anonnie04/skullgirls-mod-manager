# Linux desktop build

Install the .NET 9 SDK, then run from the checkout:

```sh
./build-linux.sh --test
./dist/linux-x64/SkullgirlsModManager
```

The build also detects an SDK installed in `~/.dotnet`. Output is self-contained
for Linux x64; the installed SDK is only needed to build. Avalonia uses the
Linux desktop's X11/XWayland, fontconfig, and graphics libraries.

The native graphical frontend is in `Desktop/`; the existing Windows Forms
frontend remains in `App/`. Both use the same archive engine and backup checks.
The Linux interface supports ZIP/folder import, enable/disable, load order,
change previews, apply, restore, recovery, and user-supplied compatibility setup.
The Windows asset editor, audio previews, mod updates and profile picker have
not yet been ported to this interface.

## Optional components

Mods with optional components have an **Options…** button on their library row.
Choose components, save, then preview and apply. Selections persist in the mod
library. The AOTC standalone ZIP is also supported: its manifest is converted
on import, with the optional Soviet announcer off by default. Installer scripts
and Windows DLLs are excluded from the converted mod.

## Importing AOTC

Choose **Import ZIP…** and select `AOTC-0.15.1-ModManager.zip` directly. The
package already contains `mod.json`, archive replacement folders and loose
artwork. No repacking, renaming, or editing the ZIP is needed. Import extracts
an independent library copy (including any sprite storage repairs); the
original ZIP is opened read-only. New imports are disabled until you enable them.

On startup the app reconnects to a valid saved folder or finds the game in
standard, Snap, or Flatpak Steam installations and their additional libraries.
`SKULLGIRLS_GAME_ROOT` can override discovery. Stale saved folders are ignored.
You can also connect manually to the Steam game directory containing `SkullGirls.exe` and `data01`,
enable AOTC, then **Preview changes**. Close the game before applying. Do not
also use the standalone AOTC installer on the same game installation.

Discovery and connection support both native Linux and Windows/Proton installs.
For native Linux, **Set up mod support…** offers a reversible, one-byte archive
verification patch for the audited executable SHA-256
`98c47645b247917a9976e8edc7eaf4425f245911c5236ab442325cd508077f73`.
The manager rejects unknown builds, backs up the original executable, verifies
the patched checksum, and preserves executable permissions. **Restore originals**
restores archives first, then the executable. Re-enable support after restoring
if you want to apply protected archive mods again. Steam updates may replace
the executable; a new build needs a fresh audit rather than a guessed patch.

AOTC's archive layout is shared with the installed Linux game. It replaces
81 existing archive assets and adds two Big Band textures referenced by its
replacement story script, plus 56 loose artwork files. This does not make
Windows DLL plugins compatible with Linux; those require separate native ports.
The original ZIP is never edited.

For the Windows game through Proton, supply the supported `CoreMessaging.dll`
using **Set up mod support…**. Configure its Steam launch options:

```text
WINEDLLOVERRIDES="CoreMessaging=n,b" %command%
```

Keep existing launch options when adding the override. Loader compatibility
with your particular game/Proton version must be verified in game; compiling
the manager does not prove game compatibility.

Library and backups live under the user's local application data directory
(normally `~/.local/share/SkullgirlsModManager`). Keep this directory to preserve
restore backups. The saved game location is reused on the next launch.

The Linux interface uses the Windows app’s burgundy-and-gold palette.

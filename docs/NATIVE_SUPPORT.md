# Native Linux archive support

The current native compatibility implementation is specific to this x86-64
Steam executable, not a general Windows DLL translator:

- Original SHA-256: `98c47645b247917a9976e8edc7eaf4425f245911c5236ab442325cd508077f73`
- Patched SHA-256: `d3214419d07bf755624a03a2092ce319b5c25e6d93a806ba5b49eadba0d07eb9`
- File offset: `0x19c3c1`, original byte `75`, replacement `EB`.

Offline disassembly of the installed ELF shows the shared package-open path at
VA `0x59c290`. It calls the archive verification routine at `0x59c3ba`, tests its
result at `0x59c3bf`, and branches to the normal package-loading path at
`0x59c3e0` when successful. Failure reaches the `Package %s.gfs not verified.`
message at `0x59c3d0`. The patch changes `jne +0x1d` to `jmp +0x1d`; archive
opening, indexing, and payload loading remain on the original path.

The full executable hash and adjacent instruction bytes must match before
setup. The original is preserved in the installation profile's `compatibility`
folder. The staged executable is hashed, its Unix permissions retained, and it
is renamed over the target. An interrupted setup can be retried; a patched
executable without its verified original backup is rejected. Restore refuses
to overwrite an executable changed by another tool or a game update.

AOTC ZIP import is separate from game installation. Its contents are extracted
to the manager library, enabled, reviewed, and then applied to `data01` with the
existing transactional GFS builder and verified original archive backups.
Windows and native Linux use the same GFS container format here. The supplied
AOTC package targets eight installed archives: 81 replacements and two new
textures referenced by its replacement Big Band script, plus 56 loose PNGs.
There is no ZIP repacking step. Windows executable plugins are not translated.

Validation includes the installed archive indexes, the exact supplied ZIP,
byte-identical source ZIP checks, patch/restore on disposable executable
copies, unknown-build rejection, and preservation of externally changed
executables. Boot and gameplay checks must be recorded separately from these
structural checks.

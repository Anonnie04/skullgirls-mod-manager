# Mod Manager — improvement plan

Date: 26 October 2026

Status: Core improvements implemented on 28 October 2026; optional editor links remain proposed.

## Design principle

Mod Manager is a standalone application for importing, organizing, previewing, applying and restoring supported mods, regardless of how those mods were created. Neither Sprite Workshop nor Palette Editor is required. Manager can optionally connect those tools for users who choose to install them; it is not a mandatory hub for their standalone workflows.

This plan is based on documentation and related-tool source inspection, not a completed Manager runtime or implementation audit. Effort estimates are relative.

## Implementation status — 28 October 2026

- Update mod and previous-version rollback are implemented, with staged package validation and complete-package replacement.
- Asset-focused conflict explanations and read-only installation recovery status are implemented.
- Named shared-library mod profiles are implemented, with review of missing mods and version changes before switching selections.
- Verification: 183 disposable-fixture checks, the full UI smoke suite, and a final focused review smoke pass. No mods were applied to the installed game.
- Optional editor links and package extensions remain separate future work. The sections below retain the original proposal and acceptance criteria.

## 1. Update an imported mod without removing it first

**Priority: High · Effort: Medium · Standalone**

The current guide requires removing an imported mod and importing its replacement. Add **Update mod** to preserve enabled status and load-order position while replacing library contents.

- Stage and validate the replacement before modifying the existing entry.
- Match the mod ID; require an explicit mapping for a different ID.
- Show old/new versions and added, changed and removed assets.
- Replace the complete package so deleted files do not remain accidentally.
- Retain the previous package and settings for rollback.
- Keep updating the library separate from applying changes to the game.

**Acceptance:** Updating preserves selection and order. Removed assets disappear from the package. Validation failure leaves the old import intact. Users can restore the previous version. These checks use ordinary supported packages with no external editor installed.

## 2. Add named mod profiles

**Priority: Medium · Effort: Medium · Standalone**

Save named sets of enabled mods and load order. Profiles should reference the shared library rather than duplicate archive backups.

Keep profiles separate from installation baselines and transaction history. Switching profiles previews the proposed selection; the usual Apply action performs installation. Identify missing mods and version changes before applying.

**Acceptance:** Switching between profiles restores saved selections and order. Missing mods are listed. Switching never adopts a new baseline or discards restore history.

## 3. Explain conflicts in terms of the resulting installation

**Priority: High · Effort: Medium · Standalone**

Build on the existing override rows with an asset-focused explanation showing all contributing mods, their order and the final winning replacement. Allow navigation from a conflict to the responsible library entry.

For recognized character assets, explain when a full metadata replacement can override palettes or affect associated art. Use package contents and supported format checks, not assumptions about which editor created a mod. Do not claim semantic compatibility solely because an archive can be repacked successfully.

**Acceptance:** With two conflicting ordinary packages, users can identify exactly which asset wins and why. Changing order updates the preview. Unsupported content retains a clear file-level explanation without speculative compatibility claims.

## 4. Make recovery easier to find and understand

**Priority: Medium · Effort: Medium · Standalone**

Build on the persistent journal, verified backups and baseline history with a concise status view for the selected installation: pending recovery, changed archives, backup verification problems and available recovery actions.

Explain the difference between restoring originals and adopting a changed archive as a baseline. Reuse existing recovery mechanisms rather than introducing a second installation path.

**Acceptance:** An interrupted transaction points to the existing recovery action. External archive changes identify the affected archive. Users can inspect available history without altering game files.

## Optional links to editing tools

**Priority: Low · Effort: Medium · Optional**

Offer configured shortcuts to open supported assets in Workshop or Palette Editor. Each shortcut is independent: installing one editor must not require the other. Preserve Manager's own asset browser, supported replacement workflow and ordinary file import.

An optional package handoff can route exported files through normal validation and disabled-by-default import. Missing editors should not produce startup warnings or interrupt normal mod management.

Future palette-operation support may be evaluated as an optional package capability. It must work from self-contained package data without launching an editor, retain conventional package support, and use the existing verification and rollback transaction. Arbitrary binary merging is outside this proposal.

**Acceptance:** Manager's main test configuration has neither editor installed. Additional checks cover each optional editor independently. Imports from third-party tools continue to work through the standard supported format.

## Suggested order

1. Implement Update mod with version rollback.
2. Improve conflict explanations and recovery visibility.
3. Add named profiles.
4. Evaluate optional editor links and package extensions separately from core releases.

## References

- [Current import, conflict and recovery workflows](USER_GUIDE.md)
- [Project overview](../README.md)
- [Build and verification instructions](BUILDING.md)

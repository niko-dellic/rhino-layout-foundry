# Changelog

## Coordinated clean break — unreleased

- Require automation protocol 2 and drawing-set specification 2 with explicit per-view visibility.
- Preserve unsupported schema-16 metadata without migration; retain document schema 17 and package format 6.
- Remove the obsolete AI batch-creation bridge and unused folder-duplication implementation; retain basic creation and active hierarchy duplication.
- Coordinate with AI checkpoint 2: typed display events, protected unsupported conversation records and Release exclusion of the demo driver.
- This source candidate requires fresh qualification and matched installation; it is not published.

## 0.1.0-beta.1 — candidate (unpublished)

- Avoid native hierarchy row reloads when displayed values are unchanged; invalidate cached paper/display-mode labels when source data changes.
- Reserve caption clearance below generated detail frames.
- Avoid repeated native object enumeration when refreshing large drawing sets.
- Include the shared UI Markdown runtime in distributable packages.
- Register linked captions through current native detail attributes after viewport commits, fixing batch creation rollback caused by stale detail wrappers.
- Honor PDF cancellation after capture and immediately before replacing the destination file.

## Earlier pre-release hardening

- Make the Canvas Inspector wider and resizable, constrain its scroll content to the pane, wrap help text, and stack paper dimensions when the pane is narrow.
- Keep Mac table cells transparent so native full-row selection remains visible throughout dragging in the hierarchy, appearance editor, and creation review.

- Resolve current native detail objects between sheet display-mode and named-view commits; preserve explicit sheet/detail display-mode precedence.
- Check detail restoration, attempt remaining recovery steps, and report incomplete recovery rather than claiming rollback succeeded unconditionally.
- Share table density, typography, selection, and populated-row striping across the hierarchy, appearance editor, and layout review.
- Let AppKit recognize row drags without a competing distance threshold; preserve multi-row drag selection and reserve property cells for editing.

- Persist per-folder and per-sheet creation/modification dates in document schema 17; migrate schema 16 with recoverable 3DM file-date baselines.
- Breaking pre-release cleanup: document schema 17 and package format 6; historical compatibility is limited to the schema-16 document migration.
- Templates now use live sheet/detail registration only. Removed stored recipes, capture UI, folder roles, capability links, tags, and unused display-rule machinery.
- Title blocks are None/Right/Bottom with project information and per-sheet revisions; ordinary block/page geometry remains transportable.
- Creation uses one specification collection and per-detail named views in UI and experimental automation. Removed obsolete preview session flags and the Observer command alias.

- Protect unsupported or invalid Foundry metadata from editing and preserve readable archive envelopes when saving.
- Register temporary preview pages before construction continues; attempt all cleanup and always restore Undo recording.
- Remove deferred modified-flag resets. Canceling a live preview may leave an unsaved-change indicator to protect real edits.
- Journal import dependencies and overwritten named resources; preserve a recovery package and report incomplete rollback.
- Separate mutation dispatch, operation families, application workflows, creation controls, draft state, and preview scheduling.
- Stop repeated settings-pane layout and repainting when its viewport width has not changed, improving creation/edit dialog scrolling on macOS.
- Limit canvas tree gesture interception to visible rows; allow panning below short trees and prevent tree scrolling from falling through to canvas zoom.
- Freeze staged automation plans and test expiry, revisions, and single-use approvals. The companion API remains experimental.
- Centralize versions and platform selection, lock dependencies, validate candidate bundles, and distinguish portable CI from native host verification.

No public v1 release has been approved by these changes. See docs/TESTING_AND_RELEASE.md for the remaining release gates.

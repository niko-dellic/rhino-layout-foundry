# Development Status

Last updated: 2026-08-26

## Current gate

Milestone 1 — Foundation has passed its first real macOS load check, while the first read-only Milestone 2 increment is underway. The remaining foundation gates are Windows loading plus in-Rhino rename/Undo, lifecycle, persistence, and package-install verification.

## Implemented

- Four-project solution: `Core`, `UI`, `Rhino`, and `Core.Tests`.
- .NET 8/AnyCPU defaults, centralized package versions, nullable analysis, warnings as errors, and deterministic builds.
- Versioned `DocumentState`, folder/sheet/title-block/display-rule records, diagnostics, and operation contracts.
- Validated hierarchy index with nested folder resolution and folder/sheet/detail selectors.
- Token-based naming preview with formatted sequences, missing-token warnings, and duplicate/empty-name blocking.
- Dynamic display-rule resolution with deterministic priority, live folder membership, deduplication, and unresolved-reference diagnostics.
- JSON document-state serializer plus Rhino plug-in archive envelope and safe fallback for invalid/unsupported metadata.
- `LayoutFoundry` Rhino command, Eto docked-panel registration, active-document overview, and sheet/detail counts.
- Pure `RenameSheetPlanner` with document/revision/before-value checks, empty and duplicate validation, frozen changes, and no-op rejection.
- Rhino UI-thread mutation service with active-document revalidation, one explicit modeless undo record, postcondition verification, and before-name restoration on failure.
- Folder → sheet → detail `TreeGridView` with multiselect, text/tag/detail filtering, stable selection keys across filtering/refresh, refresh coalescing, and single-sheet rename controls.
- Document/view/object event routing with per-document revision tracking and cleanup on document close.
- Initial Yak manifest, VS Code launch/tasks, and Windows/macOS GitHub Actions workflow.
- Thirty-nine passing core contract tests, including the deterministic 200-sheet/1,000-detail hierarchy and filter budgets. Normal CI uses xUnit; a build-flagged zero-dependency runner supports constrained local environments.

On 2026-08-26, the default `net8.0` solution built against stable RhinoCommon 8.34.26223.11001 with .NET SDK 8.0.424 on macOS with zero warnings and zero errors, and all 39 core tests passed. The development bundle then loaded in Rhino 8.34 (8.34.26223.11002): Rhino registered `LayoutFoundry`, ran the command, opened its panel container, and created the `LayoutFoundryPanel` Eto instance. Windows and the mutation/persistence checks remain open gates.

## Recommended milestones to hit next

### Gate 1A — Verify the first vertical mutation in Rhino

The intended sheet-rename pipeline is implemented. Verify it in licensed Rhino 8 hosts:

1. Load the `net8.0` development output on macOS and Windows.
2. Rename a selected sheet from the Foundry panel.
3. Confirm the tab and tree update and that one Undo/Redo step round-trips the name.
4. Exercise empty, duplicate, no-op, closed/switched-document, and external-rename conflicts.
5. Confirm failure never leaves a changed name or open undo record.

This gate is first because it validates the most consequential architectural boundary: UI → plan → Rhino mutation → undo → event refresh.

### Gate 1B — Prove lifecycle and persistence in Rhino 8

- Build the committed `net8.0` target in Windows and macOS CI.
- Load the development package in Rhino 8 on both platforms.
- Verify panel registration, active-document switching, page enumeration, and clean shutdown.
- Save/reopen empty and populated schema-1 state and verify Save As creates an independent runtime state.
- Confirm corrupt/newer metadata never prevents a 3DM from opening.
- Build and install the development Yak package from a clean profile.

### Gate 2A — Complete the read-only tree at production scale

The first folder → sheet → detail tree, stable selection model, same-turn refresh coalescing, and deterministic target-scale contract are implemented. Next add indexed tag/status filters, timed event-burst coalescing, diagnostics badges, and navigation before any additional property editors.

Create the deterministic 200-sheet/1,000-detail fixture at the start of this gate. Do not postpone scale testing until the UI is feature-complete.

### Gate 2B — Lazy previews and targeted invalidation

Add the thumbnail scheduler after the tree is responsive without images. Capture one Rhino preview at a time in idle slices, process images off-thread, prioritize visible rows, reject stale results, and enforce explicit cache limits.

### Gate 3A — First useful batch editor

Start existing-sheet management with page naming and sizing, because these exercise multiselect, mixed values, staging, validation, units, atomic Apply, and Undo without yet requiring display-rule or title-block complexity. Detail display/layer editing follows once the batch transaction model is proven.

## Tests to add next

### Priority 0 — Foundation gate

- Rename planner: valid rename, empty/duplicate name, missing sheet, stale revision, changed before-value, and no-op rename.
- Rhino rename integration: exactly one undo record, Undo/Redo equivalence, external rename conflict, closed/switched document rejection, and injected mutation failure restoration.
- Plug-in persistence integration: empty/populated round trip, Save As, corrupt payload, unsupported newer schema, and document close cleanup.
- Panel lifecycle: no active document, two open documents, active switch, close active/non-active document, repeated panel open, and event unsubscription on shutdown.
- Default-target builds: clean `net8.0` restore/build on Windows and macOS plus package-content validation.

### Priority 1 — Read-only management prototype

- Hierarchy invariants: missing parents, multiple roots, deep nesting, deterministic sibling order, sheet moves, tag normalization, and removal/reparenting policies.
- Selection semantics: folder expansion, overlapping scopes, filtered rows, range selection, row recycling, and active-document replacement.
- Event bridge: burst coalescing, targeted invalidation, undo/redo events, external page/detail creation/deletion/rename, and no cross-document leakage.
- Performance: hierarchy capture/flatten/filter percentiles on the benchmark fixture and retained memory after repeated document close.
- Thumbnail pipeline: priority, deduplication, cancellation, stale result rejection, eviction, failure placeholders, and document-close disposal.

### Priority 2 — Before batch mutation ships

- Operation atomicity for every property type, including injected failure at each mutation step.
- Metric/imperial page size and detail-scale equivalence, including explicit 25.4× regressions.
- Mixed-value batch fields, detail inclusion tiles, selection resolution at Apply time, and conflict refresh.
- Per-detail layer inheritance/set/clear behavior and object display-rule delta application.
- Keyboard-only, high-DPI, light/dark theme, and Windows/macOS visual checks.

## Foundation exit checklist

- [x] Solution boundaries and core contracts exist.
- [x] Naming, hierarchy, rules, and serialization have executable tests.
- [x] Rhino command and docked-panel shell compile against RhinoCommon.
- [x] CI and Yak skeletons exist.
- [x] One rename flows through snapshot, planner, mutation service, and explicit undo-record code.
- [x] The first folder/sheet/detail tree and filter model compile and have domain tests.
- [x] Default `net8.0` solution builds on macOS.
- [ ] Default `net8.0` solution builds on Windows.
- [x] Development package loads and constructs its Eto panel in Rhino 8 on macOS.
- [ ] Development package loads in Rhino 8 on Windows.
- [ ] The planned rename is verified atomic and creates exactly one Rhino undo record in Rhino.
- [ ] Empty and populated metadata survive save/reopen and Save As.
- [ ] Development Yak package installs from a clean Rhino profile.

# Development Status

Last updated: 2026-08-27

## Current gate

The foundation and core management shell have passed macOS build/load checks. A first document-local creation/template vertical slice is now implemented and live-verified on macOS ahead of the complete existing-sheet editor so it can be exercised in production-shaped documents. Remaining gates include broader recipe/title-block fixtures, Windows loading, shared asset import, and the mutation capabilities listed below.

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
- Folder → sheet → detail `TreeGridView` with a hidden persistence root, filesystem-style top-level folders/sheets, multiselect, text plus all/sheets/details filters, stable selection keys across filtering/refresh, refresh coalescing, and capability-aware contextual actions. Legacy tag values remain readable but tag controls are removed from the active UI.
- Native Eto management-shell visual system with semantic spacing and typography, a filename-free left-aligned product header, sheet/detail totals in the hierarchy heading, automatic synchronization with no manual Refresh control, a thin tooltip-backed icon toolbar above the hierarchy, actionable no-document/empty/no-results states, visible-result counts, and type-aware contextual selection actions. Windows retains compact/standard/wide multi-column thumbnail states; macOS uses a resize-safe single text column with inline metadata/diagnostics.
- Direct sheet/detail navigation through a shared adapter from double-click, Enter, or the contextual Open action; Escape clears selection.
- Document/view/object/command event routing with per-document revision tracking and cleanup on document close. A panel-lifecycle 500 ms identity check covers native Layouts-panel create/delete operations that do not raise RhinoCommon page events; it reads only active-document serial and sheet count until a change occurs.
- Typed event invalidations with 120 ms burst coalescing distinguish document identity, hierarchy, metadata, diagnostics, thumbnails, and active-view changes.
- Cooperative Windows page-preview pipeline with selected/early-row priority, one Rhino UI-thread capture at a time, PNG transport, cancellation, targeted invalidation, and a 96-entry/24 MiB LRU cache. Mac dock preview capture is disabled while using the resize-safe hierarchy path.
- Sheet diagnostics for missing folder references, duplicate names, and sheets without details, exposed through status badges without hiding unresolved rows.
- Functional batch-properties editor with a target table showing layout name, paper dimensions/units, detail count, and current display-mode summary; opt-in naming, paper, and detail cards; standard paper presets; explicit width × height and units; an autocomplete Rhino display-mode combobox; a complete review; and validated Apply with before-state restoration on failure.
- Searchable display-mode and title-block selectors now rebind to case-insensitive substring matches on every keystroke. Batch targets show the designated title block, and users can assign, replace, or remove it across any included sheets using a named page-space block instance as the source template.
- Undo-safe batch page rename remains capability-gated off: live Rhino testing and current SDK guidance show `PageName` is not restored by a modeless undo record, while custom undo callbacks cannot safely mutate Rhino document state. The page context menu still provides Rhino parity through an explicitly non-undoable in-place rename.
- Finder-style hierarchy editing: the `+` toolbar menu offers New Folder and New Layout at the selected destination; root/nested items use in-place draft rows; Duplicate and Delete accept any multiselection of folders, sheets, and detail rows; selected descendants are normalized under their selected folder; deletion reports complete folder/layout totals; and failed duplication removes every incomplete copy. Folder/sheet/detail rows move into folders through native drag sessions. Single-layout menus additionally provide Set Current, Rename, New Detail, Print, and Properties. Dragging or acting on a detail targets its containing sheet because folders organize sheets while details remain sheet children.
- Hierarchy-aware multipage PDF output: right-clicking a folder exposes `Print Folder…`, while right-clicking hierarchy whitespace exposes `Print All…`. Folder scopes include layouts in nested folders, use the same deterministic order as the visible tree, validate every Rhino page before capture, and replace the destination only after a complete temporary PDF succeeds. Empty scopes remain visibly disabled.
- Hierarchy moves use Eto's real native drag session with an internal data type, move-only effects, folder-only drop targets, and synchronous extraction of the target before asynchronous mutation. Folder-cycle and stale-document conflicts are still validated by the immutable planners.
- Folder creation/rename/delete and folder/sheet moves use trimmed/unique names, revision and before-value checks, immutable document-state updates, one Rhino custom Undo/Redo record per action, modified-document signaling, and 3DM persistence. Page creation uses the same validation and atomic rollback but is explicitly non-undoable because Rhino documents that most Layouts-panel changes cannot be undone and native page creation is outside the object Undo system.
- Schema v2 document state adds versioned local sheet recipes and migrates schema-v1 payloads without discarding hierarchy metadata.
- A selected layout can be captured as a document-local template. Capture records paper dimensions and units, detail rectangles, camera settings, projection, lock state, scale ratio, display mode, metadata, a default naming pattern, and an optional explicitly selected page-space title block.
- The batch-creation dialog supports quantity per template, mixed page sizes, folder destination, naming tokens and formatted indices, start/step controls, a complete proposed-name preview, and one optional named-view assignment across generated detail slots.
- Every `New Layout` entry point now opens the same review-first creation dialog instead of inserting a name-only row. The editor provides quantity, destination, naming pattern/start/step, A/ANSI paper presets, orientation and custom dimensions, built-in blank/1/2/4-detail arrangements or captured templates, searchable display-mode and title-block choices, optional named-view assignment, and a resulting-layout table with name, type, paper, detail count, display mode, and title block.
- Batch preflight blocks missing templates/folders, invalid paper/detail geometry, empty/duplicate/existing names, and unsupported recipe versions. Missing title-block definitions remain visible warnings and are skipped rather than aborting normal sheet creation.
- The Rhino adapter converts captured page units explicitly, recreates detail viewports and settings, reuses matching block definitions for page-space title blocks, and removes every page created by the operation if a later step fails. Rhino still does not supply native Undo for page creation, so the UI reports this limitation instead of promising a false undo record.
- Initial Yak manifest, VS Code launch/tasks, and Windows/macOS GitHub Actions workflow.
- One hundred twenty-eight passing core contract tests, including schema-v1 migration, template serialization, mixed-template expansion, batch target resolution, mixed hierarchy duplicate/delete normalization, combined naming/paper/display-mode plans, recursive folder plans, existing-name collisions, invalid detail and named-view recipes, visible-row indexing, hierarchy moves, filtering/navigation, diagnostics, invalidation, bounded thumbnail caching, responsive breakpoints, and the deterministic 200-sheet/1,000-detail hierarchy/filter budgets. Normal CI uses xUnit; a build-flagged zero-dependency runner supports constrained local environments.

On 2026-08-27, the `net8.0` Core, UI, and Rhino plug-in projects built against stable RhinoCommon 8.34.26223.11001 with .NET SDK 8.0.424 on macOS with zero warnings and zero errors, and all 111 core tests passed through the constrained local harness. A stack-sampled native recursion reproduced the Rhino/Eto dock freeze in `NSView` geometry validation when resizing the multi-column image TreeGridView. The installed Mac build now uses one safe hierarchy column, folds detail/tag metadata and diagnostics into row labels, skips preview capture, and prefixes folders and sheets with familiar folder/layout icons while keeping a distinct detail marker. The refined shell has one dark system-background token, a compact action row, and a native styled search/filter row beneath it; the live selection summary sits in the hierarchy header so toolbar geometry remains fixed. A later creation crash was traced from the macOS diagnostic report to Foundry passing `BeginEdit` row 27 to a tree with only 19 visible rows; collapsed detail descendants had incorrectly been counted. The corrected visible-row traversal is regression-tested. Live Rhino checks then created and named a folder in place, created a layout inside it without crashing, moved a page and a folder through real native drag sessions, and duplicated a layout from the page-specific parity menu. The same check confirmed Rhino does not undo native layout creation, so Foundry reports that limitation and avoids a misleading metadata-only Undo record. Windows retains the richer multi-column thumbnail table. Windows and broader lifecycle checks remain live verification gates.

The subsequent template, batch-management, Finder-style mixed-selection, batch title-block, hierarchy-PDF, and unified layout-creation increments also built all three projects with zero warnings and errors and raised the xUnit contract suite to 137 passing tests. On 2026-08-27, the unified creator was reloaded from the installed development bundle and live-tested in Rhino: its automatic next-page index, built-in blank/1/2/4-detail choices, immediate preview updates, full searchable display-mode list, A2 landscape dimensions, and atomic two-layout/four-detail creation path were verified. The resulting layouts reported 594 × 420 millimeters, four details, and Technical display mode in the existing-sheet batch inspector. The remaining template, title-block, PDF, mixed-selection, and cross-platform cases still require the full live checklist before release.

## Recommended milestones to hit next

### Gate 1A — Obtain a supported page-property Undo path

The undo-safe rename experiment is resolved as unsupported with the current public cross-platform RhinoCommon surface. `RhinoPageView.PageName` does not contribute restorable state to a modeless undo record, and McNeel's custom-undo guidance forbids changing Rhino document/application state from custom undo callbacks. Foundry exposes this as an explicit mutation capability for batch workflows; the context-menu parity action is permitted only as a clearly labeled non-undoable inline change.

Next, preserve the minimal reproduction on Windows, ask McNeel whether an undo-aware page-property API exists or is planned, and only unlock rename/batch Apply after one Undo/Redo step demonstrably round-trips the entire mutation. The planner, conflict checks, rollback behavior, and dialog shell remain ready behind this gate.

### Gate 1B — Prove lifecycle and persistence in Rhino 8

- Build the committed `net8.0` target in Windows and macOS CI.
- Load the development package in Rhino 8 on both platforms.
- Verify panel registration, active-document switching, page enumeration, and clean shutdown.
- Save/reopen empty and populated schema-1 state and verify Save As creates an independent runtime state.
- Confirm corrupt/newer metadata never prevents a 3DM from opening.
- Build and install the development Yak package from a clean profile.

### Gate 2A — Complete the read-only tree at production scale

The folder → sheet → detail tree, filesystem-style root flattening, stable selection model, combined row/text filters, direct Rhino navigation adapter, typed/coalesced invalidations, diagnostic badges, responsive widths, bounded lazy previews, live preview/dialog smoke checks, and deterministic target-scale contract are implemented. Next add true viewport-aware row prioritization, expand diagnostic sources, finish Windows screenshot baselines, and run the licensed 200-sheet/1,000-detail fixture in Rhino.

Create the deterministic 200-sheet/1,000-detail fixture at the start of this gate. Do not postpone scale testing until the UI is feature-complete.

Treat the panel's visual and interaction system as part of this gate. Add narrow/standard/floating width behavior, keyboard focus order, theme-aware contrast, consistent component states, and visual baselines for the empty, populated, filtered-empty, and selection states before multiplying the number of editors.

### Gate 2B — Harden lazy previews and targeted invalidation

The bounded one-at-a-time scheduler, cancellation, early/selected-row priority, stale-document rejection, and targeted cache invalidation are implemented. Next add true scroll-viewport awareness, failure placeholders and retry policy, off-thread decode/scale measurements, and document-close leak checks on the production fixture.

### Gate 3A — First useful batch editor

Start existing-sheet management with page naming and sizing, because these exercise multiselect, mixed values, staging, validation, units, atomic Apply, and Undo without yet requiring display-rule or title-block complexity. Detail display/layer editing follows once the batch transaction model is proven.

## Tests to add next

### Priority 0 — Foundation gate

- Rename planner: valid rename, empty/duplicate name, missing sheet, stale revision, changed before-value, and no-op rename.
- Rhino rename integration: exactly one undo record, Undo/Redo equivalence, external rename conflict, closed/switched document rejection, and injected mutation failure restoration.
- Page-name undo capability: inspect the undo stack before/after `RhinoPageView.PageName`, verify `EndUndoRecord`, compare native Layouts-panel rename behavior, and preserve a minimal macOS/Windows reproduction for McNeel if no supported undo-aware API exists.
- Plug-in persistence integration: empty/populated round trip, Save As, corrupt payload, unsupported newer schema, and document close cleanup.
- Panel lifecycle: no active document, two open documents, active switch, close active/non-active document, repeated panel open, and event unsubscription on shutdown.
- Default-target builds: clean `net8.0` restore/build on Windows and macOS plus package-content validation.

### Priority 1 — Read-only management prototype

- Hierarchy invariants: missing parents, multiple roots, deep nesting, deterministic sibling order, sheet moves, tag normalization, and removal/reparenting policies.
- Selection semantics: folder expansion, overlapping scopes, filtered rows, range selection, row recycling, and active-document replacement.
- Management-shell presentation: no-document, empty-document, filtered-empty, hierarchy, single-selection, and multiselection states; action visibility; clear-search behavior; narrow-width clipping; keyboard focus; light/dark theme; and high-DPI screenshot baselines.
- Root/filter/navigation contracts: hidden root, root-level sibling ordering, combined filter modes, visible-result counts, stable navigation IDs, sheet/detail activation, stale targets, Enter/double-click activation, and Escape clearing.
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
- [x] Native Layouts-panel sheet creation updates the open Foundry panel without manual refresh on macOS.
- [ ] Development package loads in Rhino 8 on Windows.
- [ ] The planned rename is verified atomic and creates exactly one Rhino undo record in Rhino.
- [ ] Empty and populated metadata survive save/reopen and Save As.
- [ ] Development Yak package installs from a clean Rhino profile.

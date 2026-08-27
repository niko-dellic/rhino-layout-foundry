# Development Roadmap

## 1. Delivery strategy

Development proceeds through seven gated milestones. A milestone is complete only when its exit criteria pass on Windows and macOS; unfinished cross-platform behavior does not move silently into the next milestone.

The sequence deliberately establishes one domain model and mutation pipeline before adding multiple interfaces. The tree-table, commands, template workflows, and observer canvas must reuse those foundations.

## 2. Milestone 1 — Foundation

### Objective

Create a loadable cross-platform Rhino 8 plug-in with enforceable architectural boundaries and a minimal end-to-end document edit.

### Work

- Create the .NET solution and `Core`, `Rhino`, `UI`, and test projects.
- Target `net8.0` and AnyCPU; reference RhinoCommon 8 and Eto.Forms through supported Rhino packages.
- Add formatting, analyzers, nullable reference types, deterministic builds, and warning policies.
- Implement plug-in identity, lifecycle, docked panel registration, and the `LayoutFoundry` command.
- Add active-document switching and read-only page enumeration.
- Implement the first snapshot, operation plan, mutation service, and one undoable sheet rename.
- Add the document-state envelope with schema version 1 and empty-state read/write round trip.
- Add Windows/macOS build CI, unit-test CI, MIT license, contribution basics, and a Yak packaging skeleton.

### Dependencies

- Rhino 8.20+ development installations on Windows and macOS
- .NET 8 SDK and Rhino project templates
- Stable plug-in and command GUID allocation

### Exit criteria

- A clean clone builds and tests on Windows and macOS.
- The Yak development package installs and loads in Rhino 8 on both platforms.
- `LayoutFoundry` opens one docked panel and follows the active document.
- The panel enumerates sheets and performs one rename that creates exactly one Rhino undo action.
- Empty and populated schema-1 metadata survive save, close, reopen, and Save As.

## 3. Milestone 2 — Read-only management prototype

### Objective

Prove that the hierarchy, synchronization, and preview architecture remain responsive at the target document size before expanding mutation scope.

### Work

- Implement folder, sheet, and detail view models and the flattened virtualized tree-table.
- Establish a native Eto visual system with semantic spacing, typography, surfaces, actionable empty states, contextual actions, and platform theme adaptation. Use shadcn-style component discipline as a quality reference while keeping controls native to Rhino.
- Add expansion, multiselection, keyboard navigation, text filtering, tag filtering, and status filters.
- Keep the persistence root invisible: root-level folders and sheets render as top-level siblings with no synthetic "Unorganized" row.
- Add document-event routing, targeted snapshot invalidation, refresh coalescing, and active-document cleanup.
- Implement lazy thumbnail scheduling, cache keys, memory bounds, cancellation, and stale-result rejection.
- Add diagnostics infrastructure and badges for missing or inconsistent references.
- Create the 200-sheet/1,000-detail benchmark fixture and performance harness.
- Add navigation from a tree row to the real Rhino sheet or detail.
- Keep the panel header document-agnostic because Rhino already displays the active filename; place refresh in the compact search/filter utility bar.

### Dependencies

- Milestone 1 lifecycle, panel, snapshot, and state envelope

### Exit criteria

- The benchmark hierarchy is usable within one second.
- Selection, expansion, and filtering normally acknowledge input within 50 ms.
- Scrolling does not wait for thumbnails and thumbnail memory remains bounded.
- Opening, switching, closing, undoing, and externally editing documents do not leave stale rows or leaked event handlers.
- Read-only behavior passes on current Rhino 8 Windows and macOS builds.
- The empty, populated, filtered-empty, single-selection, and multiselection panel states remain usable at narrow, standard, and floating widths in Rhino light and dark themes.

## 4. Milestone 3 — Existing-sheet management

### Objective

Deliver safe, useful batch mutation for existing drawing sets.

### Work

- Add folder creation, nesting, ordering, sheet moves, and tags with persistence.
- Add inline and modal staging models, mixed-value controls, operation summaries, and optimistic conflict detection.
- Implement atomic batch mutations, before-state restoration, progress, cancellation boundaries, and undo/redo synchronization.
- Add page naming, order, width, height, paper/orientation handling, and token-based rename preview.
- Add detail projection, scale, display mode, and named-view camera assignment.
- Add thumbnail detail tiles used only to include/exclude details from a pending operation.
- Add per-detail layer visibility editing and clear-override behavior.
- Add schema migrations and diagnostics for stale/missing sheet, detail, layer, or named-view IDs.

### Dependencies

- Milestone 2 virtualized selection, snapshots, events, diagnostics, and performance harness

### Exit criteria

- Folder and tag organization survives reopen and does not change Rhino's native flat tab model.
- Each inline edit creates one undo action; each batch Apply creates one undo action.
- Validation prevents known partial failures; injected unexpected failures restore the before-state.
- Duplicate/empty generated names cannot be applied.
- SI and imperial page sizes and detail scales pass physical-equivalence tests.
- Relevant external Rhino edits either merge safely or produce a clear stale-editor conflict.

## 5. Milestone 4 — Rules, title blocks, and PDF beta

### Objective

Complete the first broadly useful beta for managing and publishing existing sheets.

### Work

- Add `SetObjectLayoutsDisplayMode` using the current Rhino object selection.
- Implement display-rule creation, enable/disable, ordering, folder/sheet/detail selectors, live folder membership, delta reconciliation, and diagnostics.
- Add explicit title-block designation, allowed-definition registration, field mappings, preview, replacement, and removal.
- Add document and sheet metadata editors and standard Rhino Document/Layout User Text sources.
- Add PDF export presets, hierarchy-based ordering, output validation, temporary-file safety, progress, and cancellation.
- Add recovery UI for missing objects, details, display modes, blocks, and mappings.
- Publish beta documentation and a signed prerelease Yak package.

### Dependencies

- Milestone 3 mutation, hierarchy, layer, unit, persistence, and conflict infrastructure

### Exit criteria

- Rule precedence is deterministic and folder rules reconcile correctly after sheet moves.
- Missing rule references remain diagnosable and can recover after undo or relinking.
- Title-block replacement preserves mapped metadata and never removes an undesignated block.
- Selected folders export to a correctly ordered multipage PDF on Windows and macOS.
- Cancelling or failing PDF output leaves no misleading final file.
- The beta's full workflow and migration suites pass on both platforms.

## 6. Milestone 5 — Creation and templates

### Objective

Generate standardized sheets and details from captured, reusable recipes.

### Work

- Define and validate the shared JSON recipe schema and optional `.3dm` asset-library convention.
- Add document-local and shared template browsers, indexing, diagnostics, and refresh.
- Capture a selected sheet's page, details, title block, mappings, defaults, and profiles as a template.
- Add batch creation with mixed templates/sizes, quantities, folder destinations, tags, metadata, and naming preview.
- Import or reuse title-block definitions using content fingerprints and explicit conflict choices.
- Add one-sheet-per-named-view creation.
- Add multi-detail templates and assignment of named views to detail slots.
- Add optional assignment profiles for scale, display mode, and captured layer state.

### Dependencies

- Milestone 4 title-block, naming, rules, metadata, and diagnostics behavior

### Exit criteria

- A captured document-local template recreates an equivalent sheet in the source document.
- A shared template recreates equivalent sheets in compatible SI and imperial test documents.
- Missing assets are reported before creation and never prevent the source 3DM from opening.
- A multi-sheet batch is one undoable operation and produces no partial output after injected failure.
- Named-view assignments apply camera/projection consistently and only apply optional profile fields when selected.

## 7. Milestone 6 — Observer canvas

### Objective

Provide a Figma-like overview for navigation and assignment without building a second editing engine.

### Work

- Add a virtualized spatial canvas with cached sheet cards, zoom, pan, fit-all, and focus-selection.
- Synchronize canvas and tree-table selection by stable IDs.
- Add folder-aware grouping, sheet reordering, and moves between folders.
- Navigate from cards and detail slots to Rhino.
- Drag named views into detail slots and invoke the existing named-view assignment planner.
- Add keyboard and assistive-technology alternatives to drag-and-drop.
- Stress-test thumbnail prioritization and memory behavior at the benchmark size.

### Dependencies

- Milestone 2 virtualization/thumbnails
- Milestone 3 hierarchy and mutation pipeline
- Milestone 5 named-view assignment

### Exit criteria

- Observer navigation and selection remain within performance budgets on the benchmark document.
- Reorder, move, and assignment operations create the same plans and results as their tree-table equivalents.
- The observer does not directly mutate page-space geometry.
- Every pointer workflow has a keyboard-accessible alternative.

## 8. Milestone 7 — v1 hardening and release

### Objective

Turn the completed capability set into a stable, supportable cross-platform v1.

### Work

- Close accessibility, keyboard, high-DPI, theme, localization-readiness, and error-message gaps.
- Complete schema migration, corruption recovery, newer-schema read-only, and missing-asset workflows.
- Tune CPU, memory, event coalescing, cache bounds, and long-operation cancellation against release budgets.
- Complete user, contributor, architecture, template-authoring, troubleshooting, and release documentation.
- Add versioning, changelog, signing, SBOM/dependency review, reproducible package checks, and update/uninstall verification.
- Run release-candidate testing on supported Rhino 8 patch versions and both operating systems.
- Publish the stable Yak package and GitHub release.

### Dependencies

- All earlier milestone exit criteria

### Exit criteria

- Every acceptance criterion in the product specification passes.
- CI, licensed Rhino integration, visual regression, migration, benchmark, and manual release checklists are green.
- Clean install, prerelease-to-stable update, and uninstall preserve documents and settings as documented.
- There are no unresolved critical/high defects or undocumented destructive behaviors.

## 9. Explicitly deferred beyond v1

- Rhino 9 multi-targeting and migration
- Rhino 7 support
- Grasshopper components
- Direct physical-printer integrations
- Direct page-geometry editing on the observer canvas
- Multi-user/cloud collaboration and remote template services
- Arbitrary scripting in naming or mapping rules
- A native Rust component without an accepted profiling-based architecture decision

## 10. Definition of done for every feature

A feature is done only when:

1. Its behavior and failure modes match the product specification.
2. Core logic has deterministic unit coverage.
3. Rhino mutations have integration coverage including undo and failure restoration.
4. Windows and macOS UI paths are verified.
5. Performance and accessibility implications are measured.
6. Persistence changes include forward migration and newer-schema behavior.
7. User-facing documentation and diagnostics are updated.
8. No temporary feature flag, debug data, or generated artifact is included unintentionally in the Yak package.

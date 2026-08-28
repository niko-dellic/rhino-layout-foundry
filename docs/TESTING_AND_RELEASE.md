# Testing and Release Strategy

## 1. Quality model

Foundry changes live Rhino documents from modeless UI, so correctness requires more than conventional unit coverage. Testing is divided into five layers:

1. Rhino-independent domain tests
2. Adapter and serialization tests
3. Licensed in-Rhino integration tests
4. Cross-platform UI and visual verification
5. Performance, packaging, migration, and release gates

No mutation feature is complete until its success, validation failure, unexpected failure, undo, redo, document-close, and cross-platform paths have been considered.

## 2. Domain tests

`RhinoLayoutFoundry.Core.Tests` runs without Rhino and should contain the largest and fastest part of the suite.

### 2.1 Hierarchy and selection

- Create, rename, nest, reorder, and remove folders.
- Reject cycles, duplicate sibling names if prohibited by the UI policy, missing parents, and invalid root operations.
- Move sheets while preserving one canonical parent.
- Resolve folder, sheet, and detail selection scopes with deduplication.
- Keep tags independent from canonical folder location.
- Preserve selection by ID when visible rows are filtered or recycled.
- Verify the internal root is never rendered or searchable, while root-level folders and sheets remain ordered, selectable siblings.
- Verify all/sheets/details/tagged/untagged filters alone and in combination with text, including visible-result counts and ancestor retention.

### 2.2 Naming

- Evaluate every supported token and combinations of tokens.
- Test start, positive/negative step if allowed, zero-padding, and deterministic input order.
- Reject unknown tokens, invalid formats, empty output, and duplicate results.
- Cover Unicode, whitespace normalization policy, missing metadata, and values containing token-like text.
- Verify preview and apply use the identical frozen evaluation result.

### 2.3 Display rules

- Resolve folder, sheet, and detail selectors against changing hierarchies.
- Confirm folder rules gain/lose targets when membership changes.
- Confirm sheet rules follow current details.
- Deduplicate overlapping selectors.
- Normalize mixed folder/sheet/detail selections for Duplicate and Delete; cover multiple sheets, multiple folders, mixed types, selected descendants, stale rows, and all-or-nothing duplication rollback.
- Apply enabled state and deterministic later-rule-wins precedence.
- Produce the minimal override delta.
- Preserve unresolved objects, details, and display modes as diagnostics.

### 2.4 Title blocks and mappings

- Assign, replace, and remove a designated title block across one and many sheets from an existing page-space block instance.
- Preserve the source instance, copy its transform/attributes, remove only previously designated instances, reject stale source objects, and restore semantic before-state on partial failure.
- Verify display-mode and title-block combobox results narrow case-insensitively on every typed substring and restore all choices when cleared.

- Enforce zero-or-one active designation per sheet.
- Evaluate document, sheet, token, fixed, Document User Text, and Layout User Text sources.
- Detect required missing fields, ambiguous instances, missing definitions, and invalid anchors.
- Preserve mapped metadata through replacement.
- Ensure removal targets only explicitly designated instances.

### 2.5 Templates, migrations, and export ordering

- Round-trip every recipe and document-state field.
- Migrate every historical schema fixture to the current model.
- Expand mixed template quantities deterministically and reject names that collide within the batch or existing document.
- Reject invalid page dimensions and detail rectangles before Rhino mutation.
- Inject failure after each generated page and confirm the adapter removes the complete partial batch.
- Compare captured/recreated detail camera, projection, scale, lock, and display mode in millimeter and inch documents.
- Round-trip `.rlf` archives deterministically; reject future schemas, corrupt checksums, missing assets, unsafe archive paths, and stale-document commits. Cover merge wrapper placement, replace recovery, dependency decisions, ID remapping, unresolved object rules, and exact full-path layer-state mapping.
- Verify explicit page-space title-block selection, same-document definition reuse, missing-definition warnings, and zero unintended block deletion.
- Reject unsupported newer major schemas without mutation.
- Retain readable data and diagnostics from corrupt/partial fixtures.
- Resolve depth-first folder and explicit sheet ordering for PDF pages.
- Validate missing shared assets and block-definition conflicts before creation.

## 3. Rhino adapter and integration tests

Integration tests run inside licensed Rhino 8 hosts on Windows and macOS. Tests create disposable documents or copies of committed fixtures; they never overwrite source fixtures.

### 3.1 Document lifecycle

- New, open, save, Save As, close, reopen, and active-document switching.
- Multiple open documents with isolated state, caches, revisions, and event subscriptions.
- Plugin data read/write with empty, current, old, corrupt, and newer schemas.
- Undo/redo, externally invoked Rhino commands, and edits while a modal editor is staged.
- Native Layouts-panel create/delete while Foundry is visible, hidden, moved between containers, unloaded, and reloaded; verify the cheap identity fallback restarts with panel lifecycle and causes exactly one full hierarchy refresh per count change.
- Plugin unload/update behavior supported by Rhino.

### 3.2 Sheets and details

- Empty document, sheet without details, and many-sheet document.
- Mixed portrait/landscape and standard/custom paper sizes.
- Millimeters, inches, and other supported page unit systems.
- Parallel, perspective, and two-point perspective details.
- Locked/unlocked details and several detail scales.
- Layout rename, reorder, duplicate, and delete performed inside and outside Foundry.

### 3.3 Visibility and display

- Parent and child layers with global and per-viewport visibility.
- Built-in and custom display modes.
- Per-object override set, replace, clear, undo, and redo.
- Objects deleted, undeleted, replaced, copied, or moved between layers after rule creation.
- Rules containing overlapping folders, sheets, and detail selectors.
- Moving sheets into and out of live folder targets.

### 3.4 Blocks and named views

- Designated title blocks with direct, nested, linked, and missing definitions.
- Multiple page-space block instances where only one is designated.
- Replacement across page sizes and units.
- Document/Layout User Text fields and missing required mapping values.
- Named views added, renamed, changed, and deleted after assignment profiles are saved.
- Camera-only and optional scale/display/layer-state assignments.

### 3.5 Mutation atomicity

For every batch mutation category:

1. Capture the full affected before-state.
2. Apply the valid operation.
3. Verify every intended value and no unrelated value.
4. Verify exactly one expected Rhino undo record.
5. Undo and compare with before-state.
6. Redo and compare with applied-state.
7. Inject failure at each mutation step and verify restoration.
8. Close or switch the document before Apply and verify safe rejection.

## 4. Fixture matrix

Commit compact, redistributable fixtures plus generation instructions for large fixtures.

| Fixture | Purpose |
| --- | --- |
| `minimal.3dm` | One sheet, no detail, no Foundry metadata |
| `mixed-units.3dm` | Metric/imperial page and detail-scale equivalence scenarios |
| `hierarchy-rules.3dm` | Nested folders, tags, overlapping display rules, missing IDs |
| `title-blocks.3dm` | Direct/nested/linked definitions, mappings, ambiguous instances |
| `named-views.3dm` | Projection and optional assignment-profile cases |
| Generated benchmark | 200 sheets, 1,000 details, 500 layers, 100 rules, mixed thumbnails |

The benchmark fixture is generated deterministically to avoid storing an unnecessarily large binary. Its generator version and seed are recorded with every benchmark result.

## 5. Unit and scale correctness

DraftHorse's public discussion exposed millimeter/inch conversion failures, so unit correctness is a dedicated release gate rather than an incidental test.

- Store page dimensions with explicit page unit context.
- Never infer millimeters from a numeric value alone.
- Test equivalent A-series and US paper output through creation, title-block placement, detail scale, preview, and PDF.
- Compare physical dimensions with tolerances defined per Rhino API precision.
- Include regression cases for a 25.4× conversion error in both directions.
- Run metric and imperial PDF output checks on Windows and macOS.

Reference: [DraftHorse layout-management discussion](https://discourse.mcneel.com/t/drafthorse-layout-management-tools-for-grasshopper/168616).

## 6. UI, accessibility, and visual verification

Manual and automated UI coverage includes:

- Rhino light and dark themes;
- 100%, 150%, and 200% scaling/high-DPI configurations;
- small docked panel, large panel, floating panel, and restored workspace;
- mouse, trackpad, keyboard-only, and screen-reader/assistive labels where supported;
- platform-standard modifier selection behavior;
- focus order, visible focus, Escape/Cancel, Enter/Apply, and destructive confirmations;
- large mixed selections and batch detail-tile inclusion;
- progress, cancellation, stale-state conflicts, and diagnostic recovery;
- observer canvas alternatives to pointer-only drag-and-drop.

Visual regression captures stable panel/dialog states at controlled sizes. Platform-native text rasterization differences use platform-specific baselines. Tests prefer semantic assertions over pixel comparison for dynamic Rhino viewport thumbnails.

The management shell keeps reference captures for no-document, empty-document, populated, no-filter-results, single-sheet selection, and multiselection states. Each state is checked at narrow and standard dock widths in both Rhino themes. Semantic checks cover the filename-free left-aligned header, counts in the Layouts column heading, automatic synchronization, compact tooltip-backed action icons, action states, type-aware selection summaries, combined search/filter clearing, Enter/double-click navigation, Escape selection clearing, focus order, and keyboard activation; screenshots cover spacing, clipping, contrast, and accidental dead space.

The canonical state and viewport matrix is [UI_VISUAL_BASELINES.md](UI_VISUAL_BASELINES.md). Screenshot acceptance follows that contract rather than treating whichever panel size was last captured as the design baseline.

Additional executable contracts cover thumbnail request priority/deduplication, byte/count eviction, targeted invalidation, diagnostic severity badges, responsive thresholds, batch staging/conflicts, and mutation capability gates. Integration coverage must verify 120 ms event-burst merging, active-view changes without hierarchy rebuild, object-change all-preview eviction, cancellation on panel unload/document switch, and unsupported properties never enabling Apply.

Observer contracts additionally cover schema-3 → schema-4 migration, exclusion of camera/transient selection from persistence, deterministic non-overlapping mixed-paper placement, nested folder group movement, descendant Tidy, camera transform round trips at extreme zoom, normalized hit and paint bounds for selection windows dragged in all four directions, spatial visibility queries, stable shared selection, resolution-bucket hysteresis, named-view validation, and hierarchy-preserving reorder plans. Leftward crossing selection must use Rhino's configured crossing colors and must never change the camera or persisted board placement. Licensed Rhino fixtures must verify that captures visibly contain page-space text, dimensions, leaders, hatches, linework, and title blocks, as well as model-space annotations through details with matching display modes and per-viewport layers.

## 7. Performance and reliability

### 7.1 Budgets

On the documented reference machines and generated benchmark model:

- Hierarchy usable within 1 second of document readiness.
- Cached visible rows shown within 100 ms.
- Common selection, expansion, and filtering input acknowledged within 50 ms.
- No unexplained main-thread block over 100 ms.
- Thumbnail queue and cache memory remain bounded after repeated full-tree scrolling.
- The macOS text-only hierarchy survives at least ten alternating narrow/wide dock-splitter cycles without sustained CPU, lost selection, or an unresponsive Rhino main thread.
- Closing a document cancels pending work and returns document-specific cache memory.
- The observer produces an interactive placeholder board within 1 second at 200 sheets/1,000 details; offscreen sheets receive neither decoded bitmaps nor draw calls.
- Observer pan, zoom, lasso, and selection feedback target 50 ms or less, and only one Rhino page capture runs at a time across all panels.

### 7.2 Measurements

- Record cold and warm panel open time.
- Record snapshot/query, hierarchy flattening, filter, rule resolution, and operation planning separately.
- Record thumbnail queue latency, capture duration, encoding duration, hit rate, eviction, and stale discard count.
- Record mutation duration and UI-thread occupancy by operation type and target count.
- Record managed allocations and retained memory after document close.
- Treat results as distributions; gate on a documented percentile rather than one best run.

Performance regressions above the agreed tolerance block release unless the budget or fixture change is reviewed and documented.

### 7.3 Soak and fault tests

- Repeatedly open/switch/close fixture documents.
- Save an untitled document, then Save As under a second name; verify the project-root label follows each filename change while the document serial and sheet count remain unchanged.
- Scroll and filter the full benchmark while thumbnails render.
- Traverse, zoom, lasso, resize, tidy, and reopen the complete observer board while recording frame latency, decoded/encoded preview memory, capture concurrency, and memory after close.
- Reapply, reorder, enable, and disable display rules.
- Simulate missing shared folders and read-only output paths.
- Cancel PDF and long mutations at each safe boundary.
- Inject exceptions and invalid references through test adapters.
- Verify no unbounded event subscriptions, background tasks, images, temporary files, or stale document handles.

## 8. Continuous integration

Every pull request runs:

- restore with locked dependencies;
- formatting and analyzer checks;
- build with warnings treated according to repository policy;
- domain and adapter tests;
- schema and template serialization fixtures;
- package-content validation;
- Windows and macOS jobs.

Licensed in-Rhino suites run on dedicated Windows and macOS hosts for release candidates and on a scheduled cadence. A small smoke subset should run for changes touching Rhino adapters, persistence, mutation, or UI projects.

CI artifacts include test results, coverage, benchmark summaries when applicable, visual diffs, and an unsigned development Yak package. Secrets and signing material are available only to protected release workflows.

## 9. Versioning and migrations

- Use semantic versioning for public releases.
- Version plug-in data and shared-template schemas independently from the package version.
- Every schema change adds old-version input fixtures and expected current-version snapshots.
- Test several-step migrations, not only previous-to-current.
- Never write a newer schema merely by opening a document; write only after an intentional document save with successfully loaded/migrated state.
- Newer unsupported schemas remain unmodified and read-only in Foundry.
- Release notes identify schema changes and whether saving prevents older Foundry versions from editing the metadata.

## 10. Yak packaging and release process

### 10.1 Package validation

The Yak package contains only required plug-in binaries, manifest metadata, icons/resources, license, and user documentation. Validate:

- Rhino 8 and platform compatibility metadata;
- AnyCPU managed assemblies and absence of accidental native binaries;
- package version and assembly informational version;
- license and third-party notices;
- deterministic file list with no secrets, symbols unless intended, caches, fixtures, or local paths;
- clean install into a fresh Rhino profile.

### 10.2 Prerelease

1. Freeze scope and update documentation/changelog.
2. Pass all CI, integration, visual, migration, performance, and fault-injection gates.
3. Build the candidate from a signed tag in the protected workflow.
4. Install the candidate on clean Windows and macOS Rhino profiles.
5. Test install, panel load, sample workflow, save/reopen, PDF output, update from the previous public version, and uninstall.
6. Publish to Yak's prerelease channel and collect issue reports without telemetry.

### 10.3 Stable release

1. Resolve all critical/high candidate defects and rerun affected suites.
2. Confirm package hashes, signatures, SBOM/dependency review, and release notes.
3. Publish the immutable GitHub release and stable Yak package.
4. Verify discovery and installation from Rhino Package Manager on both platforms.
5. Preserve the package, symbols, test reports, migration fixtures, and build provenance.

### 10.4 Rollback and recovery

- Never reuse or overwrite a published version.
- If a release is defective, mark it appropriately, publish a fixed patch version, and document the affected data/schema behavior.
- Provide export/recovery guidance before asking users to remove metadata.
- Uninstalling Foundry removes plug-in files and optional user settings only; it does not alter existing 3DM contents or shared template directories.

## 11. Release gate checklist

A stable release requires all of the following:

- Supported Windows and macOS build/test jobs pass.
- Licensed Rhino smoke and full integration suites pass.
- Undo, restoration, migration, and newer-schema safety tests pass.
- Metric/imperial physical-equivalence tests pass.
- Benchmark budgets and memory bounds pass.
- Accessibility, high-DPI, theme, and keyboard checklists pass.
- PDF output is ordered, complete, cancelable, and temporary-file safe.
- Yak install, upgrade, and uninstall pass on clean profiles.
- Documentation, license, changelog, dependency notices, and recovery guidance are current.
- No unresolved critical/high defects remain.

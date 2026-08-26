# Product Specification

## 1. Product definition

Rhino Layout Foundry is a native Rhino 8 plug-in for organizing, inspecting, and mutating many page layouts at once. Its purpose is to turn repetitive sheet-by-sheet work into previewable, undoable batch operations while preserving normal Rhino documents and workflows.

The primary UI is a dockable, virtualized tree-table containing folders, sheets, and their detail viewports. A later observer canvas presents the same model spatially. Both interfaces use the same commands and mutation services.

### 1.1 Goals

- Make documents with dozens or hundreds of sheets easy to understand and navigate.
- Eliminate the need to activate each sheet for routine edits.
- Make bulk edits explicit, previewable, atomic, and reversible.
- Give title blocks, sheet names, detail settings, and outputs consistent data-driven behavior.
- Support individual users and offices with reusable drawing standards.
- Feel responsive at 200 sheets and 1,000 details.

### 1.2 Non-goals for the first release

- Replacing Rhino's native layout tabs or print subsystem.
- Editing model or page-space geometry directly on the observer canvas.
- Physical printer-driver integration.
- A Grasshopper component library.
- Cloud storage, telemetry, collaboration, or user accounts.
- Arbitrary user-authored scripts for naming or template evaluation.
- Rhino 7 or Rhino 9 compatibility in the first release.

## 2. Users and core jobs

The initial audience is architects, designers, fabricators, and documentation teams who maintain multi-sheet Rhino models.

Core jobs:

1. Understand the full drawing set without opening every layout.
2. Organize sheets into delivery or discipline folders and filter them with tags.
3. Apply the same property change to a deliberate subset of sheets or details.
4. Keep names, title blocks, and viewport presentation consistent.
5. Generate standardized sheets from known layouts and named views.
6. Export a correctly ordered subset of sheets without manual tab selection.

## 3. Information architecture

### 3.1 Hierarchy

The canonical Foundry hierarchy is:

```text
Document
└── Folder (nested)
    └── Sheet
        └── Detail
```

- Each sheet has exactly one parent folder.
- Folders may nest without an application-defined depth limit.
- Sheets may also have zero or more tags for cross-cutting filters.
- A default root contains every unorganized sheet.
- A detail always belongs to its Rhino sheet and cannot be moved independently to another folder.
- Folder organization is Foundry metadata. Rhino continues to show a flat ordered list of layout tabs.

### 3.2 Selection

- Folder selection includes descendant sheets and details appropriate to the current operation.
- Sheet selection includes the sheet and, for detail-oriented operations, all of its current details.
- Detail selection is precise and never implies sibling details.
- Shift-click selects a contiguous visible range; platform-standard modifier click toggles individual rows.
- Hidden filtered rows are not implicitly selected unless a selected folder is explicitly acting as a dynamic scope.
- The operation summary always reports the resolved counts before Apply.

## 4. Primary workflows

### 4.1 Inspect and navigate

1. Run `LayoutFoundry` or open the panel from Rhino.
2. The tree becomes usable before thumbnails finish rendering.
3. Expand folders and sheets, filter by text/tag/status, and inspect sheet/detail properties.
4. Double-click a sheet or detail to activate it in Rhino.
5. Diagnostics badges expose missing references, invalid templates, title-block ambiguity, or stale rules.

### 4.2 Batch-edit sheets and details

1. Select folders, sheets, or details.
2. Open the relevant batch editor.
3. Review sheet thumbnails containing clickable detail tiles.
4. Toggle detail tiles to include or exclude them from this operation; toggling does not delete or disable a detail.
5. Change one or more properties and inspect mixed, unchanged, and proposed values.
6. Review validation errors and the resolved target summary.
7. Apply once, producing one Rhino undo action.

Supported properties grow by milestone and include page name, order, paper dimensions, detail projection, scale, display mode, layer visibility, and named-view assignment.

### 4.3 Organize sheets

- Create, rename, reorder, nest, and remove folders.
- Drag sheets between folders or use a move command.
- Add and remove tags from one or many sheets.
- Removing a non-empty folder requires choosing a destination for its children; it never deletes Rhino sheets.
- Moving a sheet across a folder boundary previews any change caused by live folder-scoped display rules and commits the move and rule reconciliation atomically.

### 4.4 Automatic naming

Naming rules support:

- `{project}`
- `{discipline}`
- `{folder}`
- `{tag}`
- `{view}`
- `{index}` and `{index:format}`, such as `{index:000}`

The editor provides start and step values, deterministic sheet ordering, a before/after table, and warnings for missing token values. Empty names and duplicates block Apply. Naming does not execute arbitrary code.

### 4.5 Object display rules

1. Select objects in a Rhino modeling view.
2. Run `SetObjectLayoutsDisplayMode`.
3. Name the rule, select a display mode, and target folders, sheets, or details in a hierarchy picker.
4. Preview resolved object/detail pairs and conflicts with existing rules.
5. Apply and save the rule.

Rules retain stable Rhino object IDs, ordered hierarchy selectors, the display-mode ID, enabled state, and priority. Folder selectors follow current folder membership; sheet selectors follow the sheet's current details. Later rules win when enabled rules target the same object/detail pair. Missing objects, details, or modes remain in the rule and appear as unresolved diagnostics rather than being silently discarded.

### 4.6 Layer visibility

- Users select details or a higher hierarchy scope, then select layers and choose visible, hidden, or clear per-detail override.
- The editor shows inherited/global state separately from a per-detail override.
- Parent and child layers are listed with their full paths.
- The preview reports the number of changed layer/detail pairs.

### 4.7 Title blocks

- A user explicitly designates a page-space block instance as the active title block for a sheet.
- A sheet may have at most one active title block. Zero or multiple candidates are diagnostic states.
- A definition can be registered as an allowed title-block definition without automatically designating every instance.
- Batch replacement uses the selected template anchor and preserves mapped sheet metadata.
- Batch removal deletes only explicitly designated title-block instances and is undoable.

Title-block mappings may source values from:

- document metadata;
- per-sheet metadata;
- naming tokens;
- fixed values;
- Rhino Document User Text;
- Rhino Layout User Text.

Required fields, missing block definitions, ambiguous instances, and unresolved mappings are validated before mutation. A mapping preview shows the resulting value for every selected sheet.

### 4.8 Named views

Dropping or assigning a named view always applies its camera and projection. An optional assignment profile may additionally apply detail scale, display mode, and captured layer state. The preview identifies unavailable named views or profile components before Apply.

### 4.9 PDF export

- Export a selected folder, sheets, or filtered selection to one multipage PDF.
- Page order follows depth-first tree order and explicit sheet order inside each folder.
- An export preset records output quality, color mode, margins/crop behavior, and other supported Rhino capture settings.
- The export UI shows page count, order, output path, validation, progress, and cancellation.
- Cancelling or failing an export must not leave a misleading final file.
- Direct physical printing is deferred until after v1.

### 4.10 Templates and batch creation

A sheet template is a versioned recipe, not a block instance. It records:

- page size, orientation, paper name, and page units;
- ordered detail rectangles and default viewport settings;
- title-block definition, transform/anchor, and field mapping;
- default metadata, folder, tags, and naming pattern;
- optional named-view assignment profiles.

Users create a template by capturing an existing sheet. Templates may be document-local or stored in an optional shared folder. Batch creation accepts a quantity per template, supports mixed sizes, previews names and dependencies, imports required block definitions safely, creates all sheets in one undoable operation, and reports partial incompatibilities before mutation.

Creation modes include one sheet per named view and multiple template-based sheets whose detail slots are filled by selected or dragged named views.

### 4.11 Observer canvas

The observer canvas provides:

- cached sheet cards with progressive thumbnail loading;
- zoom, pan, fit-all, and focus-selection;
- hierarchy-aware multiselection;
- sheet reordering and moving between folders;
- navigation to the real Rhino sheet/detail;
- named-view drag-and-drop into detail slots.

Its first release does not move, resize, create, or delete page-space geometry directly. Any supported action invokes the same command and mutation pipeline as the tree-table.

## 5. Editing, validation, and recovery

### 5.1 Hybrid commit model

- Simple inline edits commit immediately and create one Rhino undo record per accepted edit.
- Modal and batch edits are staged locally until Apply.
- A staged editor records the source document revision. Apply re-resolves IDs and checks for relevant external changes.
- Conflicting changes block Apply and offer refresh/review; unrelated changes do not discard the user's staged values.

### 5.2 Atomicity

Every mutation follows: resolve → validate → capture before-state → begin undo → mutate → verify → end undo → refresh.

If an unexpected failure occurs after mutation starts, Foundry restores the captured before-state. The operation reports the failed item and never claims success for a partially applied batch.

### 5.3 Diagnostics

Diagnostics have stable codes, severity, affected entity IDs, and a suggested recovery action. They cover at least:

- missing Rhino sheets, details, objects, layers, named views, block definitions, or display modes;
- ambiguous or absent designated title blocks;
- unsupported/newer metadata schemas;
- shared templates that are missing or incompatible;
- naming collisions and empty results;
- display-rule conflicts and unresolved targets;
- unit-conversion and page-capture failures.

## 6. Scope by release

### First useful beta

- Docked tree-table, navigation, filtering, and thumbnails
- Nested folders and tags
- Multiselect and hybrid editing
- Naming rules
- Existing-sheet/detail property changes
- Per-detail layer visibility
- Saved object display rules
- Title-block designation, mapping, replacement, and removal
- Ordered folder-to-PDF export
- Versioned document persistence, migrations, undo, and diagnostics

### Later beta milestones

- Document and shared template libraries
- Batch sheet creation
- Named-view creation/assignment workflows
- Observer canvas

### v1

All planned beta capabilities hardened for Windows and macOS, with accessibility, migration, performance, packaging, contributor, and recovery requirements satisfied.

## 7. Performance and usability requirements

The benchmark document contains 200 sheets, 1,000 details, 500 layers, 100 saved display rules, mixed title blocks, and both cached and uncached thumbnails.

- The hierarchy is usable within 1 second after the active document is ready.
- Cached visible rows appear within 100 ms.
- Selection, expansion, filtering, and scrolling normally acknowledge input within 50 ms.
- No task blocks the UI for more than 100 ms without visible progress or a busy state.
- Thumbnail capture is lazy, prioritized by visibility, bounded, cancelable, and never required before editing.
- Long mutations and PDF generation show progress and support safe cancellation.
- The panel remains useful when shared templates are offline or missing.

## 8. Acceptance criteria

The product is ready for v1 when:

1. All first-beta and later-beta workflows pass their documented integration scenarios on current Rhino 8 for Windows and macOS.
2. The benchmark document meets the performance budgets without UI hangs or unbounded memory growth.
3. Every mutation is represented by the expected single Rhino undo action and failed batches restore their before-state.
4. A 3DM containing Foundry data remains usable when the plug-in or shared template directory is absent.
5. Existing supported metadata migrates forward with no silent data loss; newer unknown schemas open in a safe read-only diagnostic state.
6. SI and imperial sheets produce equivalent physical results in previews and PDFs.
7. Keyboard navigation, high-DPI rendering, and Rhino light/dark themes are verified on both platforms.
8. A clean Yak install, update, and uninstall succeeds and leaves user documents intact.

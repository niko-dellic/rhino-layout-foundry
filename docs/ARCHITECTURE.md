# Architecture

## 1. Technical direction

Rhino Layout Foundry will be a C# RhinoCommon plug-in targeting .NET 8 and AnyCPU. It will use Eto.Forms for a cross-platform docked panel and Rhino Package Manager/Yak for distribution.

This is the shortest supported path to Rhino's document, page-layout, viewport, undo, event, and UI APIs:

- [RhinoCommon](https://developer.rhino3d.com/guides/rhinocommon/) is Rhino's cross-platform .NET plug-in SDK.
- Rhino 8.20 and later default to .NET 8; McNeel's [.NET runtime guidance](https://developer.rhino3d.com/en/guides/rhinocommon/moving-to-dotnet-core/) recommends `net8.0` for Rhino 8 on Windows and macOS.
- Rhino's native [C/C++ SDK is Windows-only](https://developer.rhino3d.com/en/guides/cpp/what-is-the-cpp-sdk/), so it is not an appropriate primary foundation for a cross-platform panel.

### 1.1 C# rather than Rust

Rust is not used in the initial architecture. A Rust core would still require a C ABI and a C# Rhino plug-in shell, creating duplicate models, marshalling, platform-specific packaging, and a harder debugging path for work dominated by Rhino API calls and UI coordination.

A native Rust library may be proposed later only when all of the following are true:

1. A repeatable profile identifies an isolated CPU-bound operation that misses an approved budget.
2. Algorithmic, allocation, caching, and batching improvements in managed code have been exhausted.
3. The boundary can be expressed using owned primitive buffers with no RhinoCommon objects crossing it.
4. Windows x64, macOS arm64, crash isolation, signing, and Yak packaging costs are documented.
5. A managed fallback and equivalence tests exist.

The decision must be recorded as a new architecture decision before code is introduced.

## 2. Solution boundaries

The initial solution should separate Rhino-independent behavior from host integration:

```text
RhinoLayoutFoundry.Core
├── Domain entities and value objects
├── Naming, hierarchy, rule, template, and mapping engines
├── Validation, diagnostics, migrations, and operation plans
└── Ports/interfaces with no RhinoCommon or Eto references

RhinoLayoutFoundry.Rhino
├── Plug-in lifecycle and commands
├── Rhino document/query adapters
├── Mutations, undo, event bridge, persistence, previews, and PDF
└── Rhino ↔ domain identity mapping

RhinoLayoutFoundry.UI
├── Eto panel, dialogs, tree-table, observer canvas, and view models
├── Selection, staging, progress, and diagnostics presentation
└── UI-thread dispatch and image presentation

RhinoLayoutFoundry.Core.Tests
RhinoLayoutFoundry.Rhino.Tests
```

`Core` cannot reference RhinoCommon, Eto, operating-system UI libraries, or file-system globals. `UI` does not directly mutate `RhinoDoc`; it submits operations through application services. `Rhino` never stores control or view-model instances in document state.

## 3. Domain model

The names below define the initial public contracts between the core, Rhino adapter, and UI. Implementations may add private fields but should not change their semantics without updating this document and migrations.

```csharp
public sealed record DocumentState(
    int SchemaVersion,
    Guid RootFolderId,
    IReadOnlyList<FolderRecord> Folders,
    IReadOnlyDictionary<Guid, SheetRecord> Sheets,
    IReadOnlyList<DisplayRule> DisplayRules,
    IReadOnlyList<SheetTemplate> LocalTemplates,
    IReadOnlyList<ExportPreset> ExportPresets,
    DocumentMetadata Metadata,
    ObserverCanvasState? ObserverCanvas);

public sealed record FolderRecord(
    Guid Id,
    Guid? ParentId,
    string Name,
    int Order);

public sealed record SheetRecord(
    Guid PageViewId,
    Guid FolderId,
    int Order,
    IReadOnlySet<string> Tags,
    IReadOnlyDictionary<string, string> Metadata,
    TitleBlockRole? TitleBlock);

public sealed record DisplayRule(
    Guid Id,
    string Name,
    bool Enabled,
    int Priority,
    IReadOnlyList<Guid> ObjectIds,
    IReadOnlyList<HierarchySelector> Targets,
    Guid DisplayModeId);
```

Important identity rules:

- Rhino sheet, detail, object, layer, named-view, block-definition, and instance GUIDs are external references, not ownership.
- Foundry folders, templates, rules, presets, and mappings use generated Foundry GUIDs.
- Runtime serial numbers and list indices are never persisted as identity.
- Missing external IDs remain preserved and diagnosable so references can recover after undo, import, or relinking.

### 3.1 Hierarchy selectors

`HierarchySelector` is a discriminated value with `Folder`, `Sheet`, or `Detail` kind and one stable ID. Resolution happens against the latest document snapshot:

- A folder resolves recursively to all descendant sheets and their current details.
- A sheet resolves to all of its current details.
- A detail resolves only to itself.

Duplicate resolved targets are removed. Selector order does not affect membership. Rule priority controls conflicts; a later/higher-priority enabled rule wins for the same object/detail pair.

### 3.2 Operation contracts

UI actions compile into immutable plans before touching Rhino:

```csharp
public interface IDocumentSnapshotProvider
{
    DocumentSnapshot Capture();
}

public interface IOperationPlanner<in TRequest>
{
    OperationPlan Plan(TRequest request, DocumentSnapshot snapshot);
}

public interface IDocumentMutationService
{
    Task<OperationResult> ApplyAsync(
        OperationPlan plan,
        CancellationToken cancellationToken);
}
```

An `OperationPlan` contains a source revision, resolved targets, proposed changes, validation results, expected before-values, human-readable summary, and undo description. It never contains live Rhino objects.

## 4. Rhino adapter

### 4.1 Document snapshots

The adapter reads `RhinoDoc` on Rhino's UI thread and emits immutable DTOs. Snapshot capture includes only data required by the current view or operation. Large object/layer tables are indexed once per revision and reused.

Adapters cover:

- pages from `doc.Views.GetPageViews()`;
- details from each page;
- page dimensions, order, name, and units;
- detail viewport IDs, projection, scale, display mode, and layer overrides;
- named views and optional captured profiles;
- page-space block instances and definitions;
- object IDs and per-viewport display overrides;
- display modes, layers, document/layout user text, and metadata.

### 4.2 Commands

The first public Rhino commands are:

- `LayoutFoundry`: register if necessary, open, and focus the Foundry panel for the active document.
- `SetObjectLayoutsDisplayMode`: capture the current selected object IDs, open the rule editor, and create or update a saved rule after preview and validation.

Commands remain thin entry points. Business behavior belongs in planners and mutation services so the panel and observer canvas can invoke identical operations.

### 4.3 Mutations and undo

Modeless UI changes use Rhino's [`BeginUndoRecord`](https://developer.rhino3d.com/api/rhinocommon/rhino.rhinodoc/beginundorecord) and matching end call. Every apply pipeline is:

1. Reopen the active document context and reject a closed or switched document.
2. Compare the plan revision and expected before-values with a fresh targeted snapshot.
3. Resolve all external references and validate the entire operation.
4. Capture the minimal restorable before-state.
5. Begin one Rhino undo record with a user-facing description.
6. Apply changes in deterministic order on the Rhino UI thread.
7. Verify changed values and redraw only affected views.
8. End the undo record and publish one refresh event.
9. On failure, restore before-state, end the record safely, and return structured diagnostics.

Inline edits create one plan and one undo record. Modal edits may contain many field changes but still produce one plan and one undo record. Cancellation is honored before mutation and at documented safe boundaries; it never intentionally stops halfway through an atomic operation.

The mutation gate must prove that the specific Rhino subsystem contributes restorable state to that record. A successful `BeginUndoRecord`/`EndUndoRecord` pair is not sufficient by itself. The 2026-08-27 macOS smoke test showed that assigning `RhinoPageView.PageName` applies the rename but does not create a restorable Rhino undo step; the next Undo affected the preceding native layout operation instead. Foundry must not claim undo support for page naming until a supported Rhino mechanism is identified and verified with Undo and Redo on both platforms. Custom undo events are not a workaround for document mutation: McNeel documents them for private plug-in data and warns handlers not to change Rhino document/application settings.

The same gate applies to native page creation. Rhino's Layouts documentation states that most panel changes cannot be undone, and a live `ViewTable.AddPageView` test confirmed that a modeless undo record does not remove the native page. Page creation therefore performs preflight plus immediate rollback on failure, persists the matching Foundry folder record only after Rhino returns a page ID, and exposes an explicit non-undoable warning. It must not register a custom metadata Undo event, because that would detach the still-existing Rhino page from its Foundry folder and present a partial undo as success.

### 4.4 View-specific presentation

- Per-detail layer visibility uses RhinoCommon's [`Layer.SetPerViewportVisible`](https://developer.rhino3d.com/api/rhinocommon/rhino.docobjects.layer/setperviewportvisible) with the detail viewport ID.
- Per-object detail modes use [`ObjectAttributes.SetDisplayModeOverride`](https://developer.rhino3d.com/api/rhinocommon/rhino.docobjects.objectattributes/setdisplaymodeoverride) with the display mode and detail viewport ID, followed by a document attribute modification.
- A clear operation removes only the relevant per-viewport override, preserving unrelated global and viewport state.
- Rule reconciliation computes the desired final override matrix first, then applies the delta rather than replaying every rule independently.

## 5. Persistence and migration

### 5.1 Document state

Foundry persists its namespaced document state using Rhino plug-in document data. Rhino calls `ShouldCallWriteDocument`, `WriteDocument`, and `ReadDocument`; the mechanism and lifecycle are described in McNeel's [Plug-in User Data guide](https://developer.rhino3d.com/en/guides/rhinocommon/plugin-user-data/).

The serialized payload uses an `ArchivableDictionary` envelope containing:

- schema major and minor version;
- payload format and checksum;
- serialized `DocumentState`;
- writer plug-in version;
- optional migration provenance.

The payload contains metadata and stable IDs only, never thumbnails or duplicate Rhino geometry. Title-block source values remain available through standard Rhino Document User Text and Layout User Text where the mapping requests them.

Schema 4 adds optional observer board organization: folder spatial origins, manual sheet placements keyed by Rhino page-view ID, and the placement-algorithm version. Camera, hover, lasso, transient selection, preview bytes, and decoded bitmaps are deliberately session-only. Reading a schema-3 document produces an empty in-memory board, but opening or panning the observer does not advance the stored schema. Schema 4 is written only after the user intentionally changes persistent board organization and later saves the document.

### 5.2 Compatibility behavior

- Minor additive changes migrate automatically and preserve unknown extension fields where feasible.
- Major migrations are explicit, tested, and retain an in-memory copy of the source payload until the next successful save.
- Corrupt payloads do not block document opening; Foundry opens with diagnostics and offers recovery/export of readable data.
- A payload from a newer unsupported major schema opens read-only in Foundry. Rhino document operations outside Foundry remain unaffected.
- Removing the plug-in or losing a shared template folder does not remove native Rhino sheets, details, blocks, or user text.

### 5.3 Shared templates

Shared templates are UTF-8, versioned JSON recipe files in a user-selected directory. They contain no executable code. Block-definition dependencies are identified by stable recipe keys, expected names, and content fingerprints; referenced `.3dm` asset libraries are relative to the recipe when possible.

Template loading is indexed and asynchronous. Missing, incompatible, or conflicting assets produce diagnostics. Document-local templates remain available independently.

## 6. Editing and conflict model

Each Rhino event that can alter relevant state advances a monotonic document revision maintained by the adapter. A staged editor records:

- the document runtime identity;
- source revision;
- IDs and before-values it read;
- user-edited fields;
- selection scopes.

Apply performs field-level optimistic concurrency checks. An unrelated revision does not force the user to restart. A changed target field, deleted target, changed folder membership relevant to a live selector, or changed dependency creates a conflict and blocks Apply until refreshed.

Moving a sheet across folders is one operation plan containing the hierarchy change and resulting display-rule delta. The preview reports affected rule and object/detail counts.

## 7. Naming, title blocks, and templates

### 7.1 Naming

The naming engine is pure `Core` code. It tokenizes approved placeholders, formats indices with .NET numeric formats restricted to integer output, and evaluates against a frozen ordered input set. Unknown tokens, empty results, invalid formats, and duplicates are errors. Missing optional metadata is a warning only when a user-specified fallback makes the result non-empty.

### 7.2 Title blocks

Title-block designation stores the sheet ID, instance-object ID, definition ID, and template anchor. Designation never depends solely on a block name or geometric guess. Replacement planning validates the destination definition and mappings, calculates transforms in page units, and preserves per-sheet metadata before deleting the previous designated instance.

Field mappings are typed source expressions rather than scripts. Supported sources are document metadata, sheet metadata, approved naming tokens, fixed strings, Document User Text, and Layout User Text.

### 7.3 Template creation

Capturing a template reads page properties, detail rectangles/settings, designated title block and mapping, metadata defaults, and assignment profiles into a recipe. Actual details remain page-owned Rhino objects; a block instance is not treated as an entire sheet template.

Batch creation validates names, units, definition dependencies, named views, and folder targets before adding pages. Page dimensions are interpreted in the document's page unit system and normalized explicitly to avoid implicit millimeter/inch assumptions.

## 8. Thumbnails and UI responsiveness

### 8.1 Tree-table

The UI presents a flattened window over the expanded hierarchy rather than materializing one control per row. Selection is stored by stable IDs, independent of row recycling. Filtering and hierarchy expansion operate on immutable indexes in `Core`.

### 8.2 Thumbnail pipeline

1. Request a thumbnail by sheet ID, document revision, size, theme, and capture settings.
2. Return a memory/disk cache hit immediately.
3. Prioritize visible rows, then nearby rows, then explicit batch-preview requests.
4. Capture one Rhino page preview at a time on Rhino's UI/idle thread.
5. Scale and encode the resulting bitmap off-thread.
6. Publish only if the request is still current; otherwise discard it.

The cache is size-bounded and uses least-recently-used eviction. It never enters document plug-in data. Closing a document cancels queued captures and releases images.

### 8.3 Observer canvas

The main panel exposes List, Thumbnail, and Canvas modes while preserving one per-document selection coordinator. Thumbnail mode is one `Eto.Forms.Drawable` inside a vertical `Scrollable`; `ThumbnailGridLayout` computes deterministic columns and visible/overscan row ranges as the user changes card size. It never creates one native control per page. Canvas is a separate `Drawable` with its own Cartesian world coordinates; it is neither Rhino model geometry nor one Eto control per sheet. `LayoutFoundryObserver` is a command shortcut into Canvas mode, not a second registered Rhino panel. `ObserverCamera` owns reversible world/screen transforms. `ObserverPlacementPlanner` creates deterministic folder-aware placement from physical paper dimensions, and `ObserverSpatialIndex` performs visible-card queries, lasso selection, hit-testing, and drag-target resolution. Only cards intersecting the visible region plus overscan are drawn in either image mode.

Observer captures reuse the Rhino page-preview adapter, so page-space and model-space annotations are represented exactly as Rhino renders them. Resolution-aware keys use 256, 512, 1024, and 2048 pixel buckets. Selected, visible, overscan, and background requests have descending priority; duplicate requests coalesce; only one Rhino capture may run at a time. Encoded previews use a byte/count-bounded LRU, while decoded Eto bitmaps exist only for visible/overscan cards and are disposed on replacement, eviction, document switch, or panel close.

A per-document `DocumentSelectionState` synchronizes the management table, observer canvas, and Navigator by stable folder/sheet/detail IDs. Canvas hierarchy moves, reorder operations, print changes, named-view assignments, duplicate/delete actions, and batch properties all reuse existing planners and mutation services. Pan and zoom are session-local. Persisted board movement is validated against the current revision and participates in one Foundry metadata Undo record.

### 8.4 Event invalidation

The event bridge subscribes once per plug-in and routes by document runtime identity. Relevant document, page, layer, object-attribute, instance-definition, undo/redo, save/open/close, and active-document events invalidate only affected indexes and thumbnails. Bursts are coalesced before the UI refreshes.

Invalidations are typed as document identity, hierarchy, metadata, diagnostics, thumbnails, and active-view changes. The Eto panel merges bursts for 120 ms. Active-view-only events do not rebuild the hierarchy; object changes invalidate previews; view/page structure and metadata changes rebuild the affected presentation state. Entity IDs are retained when Rhino exposes a stable target, allowing sheet-specific preview eviction.

Sheet previews use a cooperative UI-idle scheduler. Requests are deduplicated and prioritized, with selected and early/visible rows first. Rhino captures at most one page at a time on its UI thread; encoded PNG results enter an LRU cache limited by both entry count and bytes. Eto bitmaps are disposed on eviction, invalidation, document switch, and panel unload. Text rows remain usable before or during capture.

Tree-row navigation crosses a small read-only adapter boundary. Core rows carry stable page/detail IDs, the Eto shell requests navigation, and the Rhino adapter resolves the target again against the active document before setting `ViewTable.ActiveView` and the active page/detail. Missing or stale targets return visible diagnostics and never mutate Foundry state.

The persisted root folder is intentionally absent from presentation models. It remains the canonical parent for metadata and orphan recovery, while its immediate folders and sheets are returned as top-level sibling rows. The root's stored name is neither displayed nor included in search matching.

## Mutation capability gates

The UI never infers that a setter is undo-safe merely because `RhinoDoc.BeginUndoRecord` returned a serial number. Capabilities are explicit runtime inputs to inline and batch editors. Unsupported or unverified properties keep Apply disabled and expose the reason.

The Rhino 8 page-name spike established that assigning `RhinoPageView.PageName` changes the sheet but does not create restorable page-name state in the document undo record. Rhino's [`BeginUndoRecord`](https://developer.rhino3d.com/api/rhinocommon/rhino.rhinodoc/beginundorecord) only records document changes for which Rhino supplies undo state. McNeel's [Custom Undo sample](https://developer.rhino3d.com/samples/rhinocommon/custom-undo/) explicitly says custom undo handlers must never change Rhino document or application settings, so a callback that reassigns `PageName` is not a supported fallback. Consequently page rename and batch Apply remain capability-gated until McNeel exposes or confirms a supported cross-platform mechanism.

Foundry-only hierarchy metadata uses the supported private-data case for `RhinoDoc.AddCustomUndoEvent`. Folder mutations register the prior immutable `DocumentState`, change only the plug-in state store, and register the inverse state from the callback for Redo. The callback never changes Rhino document or application settings. The initial modeless operation marks the document modified so the plug-in payload is included on save.

## 9. PDF export

The export service resolves the ordered sheet list, validates every page, and creates a multipage PDF with RhinoCommon's [`FilePdf.AddPage`](https://developer.rhino3d.com/api/rhinocommon/rhino.fileio.filepdf/addpage?version=8.x) and per-sheet capture settings.

Foundry-owned print actions open Rhino's native Print dialog. Before opening it, the Rhino host maps Foundry's enabled page IDs to Rhino page-view runtime serial numbers and seeds the same selected-page bridge used by Rhino's Layouts panel. The bridge is isolated behind `RhinoLayoutPrintDialogService`, checked at runtime, and restored after the modal dialog closes so Foundry does not overwrite the user's native Layouts-panel selection. Rhino therefore owns printer choice, PDF options, preview, print colors and widths, vector/raster output, backgrounds, and mixed layout page sizes.

The programmatic `RhinoLayoutPdfExportService` remains available for direct PDF generation. It uses an explicit print profile: Rhino print colors and print widths, vector-preferred output, and no viewport background color, background bitmap, wallpaper, grid, axes, or margin guides. Raster-only display content retains the requested export resolution (300 DPI by default). During each `FilePdf.AddPage` call, Foundry supplies the white print-media framebuffer fill that Rhino's native Print pipeline applies; this prevents the macOS layout viewport's gray preview surface from leaking into page and detail backgrounds. This matches Rhino's [print color and background controls](https://docs.mcneel.com/rhino/8/help/en-us/commands/print.htm) without changing the document's display or Print Preview state.

Output is written to a temporary sibling path when possible. Only a complete successful export replaces or creates the requested final path. Cancellation or failure cleans up the temporary file and returns page-specific diagnostics.

Physical printer integration is outside v1 because driver capabilities and dialogs vary by operating system.

## 10. Packaging and observability

- Build the Rhino plug-in AnyCPU for `net8.0`.
- Package binaries, manifest, icons, and documentation as a Yak package for Rhino 8 on Windows and macOS.
- Do not bundle a native binary in the initial package.
- Log structured local diagnostics through Rhino's command history and an optional Foundry diagnostics panel.
- Do not collect telemetry or send document data over the network.
- Include plug-in, Rhino, OS, schema, and operation versions in user-copyable diagnostic reports without including model contents.

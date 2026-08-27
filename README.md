# Rhino Layout Foundry

Rhino Layout Foundry is a planned open-source layout manager for Rhino 8. It is designed for fast, pleasant batch work across many drawing sheets without forcing users to activate and edit each layout individually.

The main interface is a dockable tree-table:

```text
Folder
└── Sheet (Rhino page layout)
    ├── Detail viewport
    └── Detail viewport
```

Development now includes a functional existing-sheet batch editor and the first Milestone 5 creation slice. The plug-in provides a folder → sheet → detail tree-table, Finder-style hierarchy editing, mixed-selection folder/layout duplication and confirmed deletion, table-driven batch renaming, unit-aware page sizing, live-filtered detail display-mode assignment, batch title-block assignment/replacement/removal from document instances, hierarchy-ordered folder/all multipage PDF output, and a review-first layout creator with quantities, built-in detail arrangements, captured templates, paper presets, title blocks, display modes, and named views. Windows validation, shared-template asset import, title-block field mappings, richer PDF presets/progress, and observer mode remain release gates. See the [development status](docs/DEVELOPMENT_STATUS.md) for exact capability and verification status.

## What it will do

- Organize Rhino sheets in nested folders while leaving Rhino's native layout list intact.
- Select folders, sheets, or individual details and change their properties in one operation.
- Rename sheets from previewable token and sequence rules.
- Create and maintain saved per-detail layer visibility and per-object display-mode rules.
- Designate, map, replace, or remove title-block instances across many sheets.
- Export an ordered folder or sheet selection to a multipage PDF.
- Capture layouts as reusable document-local or shared sheet templates.
- Create sheets in batches and assign named views to detail slots.
- Present every sheet on an observer canvas for navigation, reordering, and named-view drag-and-drop.

## Terminology

Rhino documentation and users often use “page,” “layout,” and “sheet” interchangeably. Foundry uses the following terms consistently:

| Term | Meaning |
| --- | --- |
| **Sheet** | A Rhino page layout: the printable paper page. |
| **Detail** | A viewport embedded on a sheet and looking into model space. |
| **Folder** | Foundry metadata that organizes sheets; Rhino's native layout tabs remain flat. |
| **Template** | A captured sheet recipe containing paper, details, title-block, and metadata defaults. |
| **Display rule** | A saved mapping from Rhino objects and a hierarchy target to a per-detail display mode. |

## Platform and stack

- Rhino 8 for Windows and macOS
- C# on .NET 8, compiled AnyCPU
- Stable RhinoCommon 8.34 for document and viewport integration
- Eto.Forms for the cross-platform docked panel
- Rhino Package Manager/Yak for distribution

RhinoCommon is Rhino's supported [cross-platform .NET plug-in SDK](https://developer.rhino3d.com/guides/rhinocommon/). Rhino 8.20 and later default to .NET 8, which is the project's initial runtime target. Rust is not part of the initial implementation; it would be considered only for an isolated workload after profiling demonstrates a material need.

The hierarchy is a fixed-column property table on both platforms: Name, Print, Paper size, Details, Display mode, and Status. The active 3DM/project is an explicit top-level row that can collapse its entire hierarchy and provides a future boundary for multi-document management. Every property header sorts sibling rows ascending or descending while retaining hierarchy, including Finder-style numeric name ordering. Print inclusion, standard paper sizes, and display modes can be changed directly from the table at folder, sheet, or detail scope. The macOS implementation deliberately remains text-only and never reconfigures columns during splitter tracking; inline previews remain disabled there to avoid the Rhino/Eto multi-column image-table recursion previously isolated during dock resizing.

## Documentation

- [Product specification](docs/PRODUCT_SPEC.md) — user workflows, requirements, scope, performance targets, and acceptance criteria
- [Architecture](docs/ARCHITECTURE.md) — system boundaries, data model, persistence, mutation pipeline, caching, and technical decisions
- [Roadmap](docs/ROADMAP.md) — start-to-finish milestones with dependencies and exit criteria
- [Testing and release](docs/TESTING_AND_RELEASE.md) — test strategy, compatibility matrix, performance gates, CI, and Yak releases
- [Development status](docs/DEVELOPMENT_STATUS.md) — completed foundation work, immediate milestones, and prioritized test backlog
- [Rhino smoke test](docs/RHINO_SMOKE_TEST.md) — build, development-load, hierarchy, previews, batch-shell, and lifecycle verification

## Planned development prerequisites

Implementation contributors will need:

- Rhino 8.20 or later for Windows or macOS
- The .NET 8 SDK
- Visual Studio 2022 on Windows or Visual Studio Code on either platform
- Rhino's project templates, installed with `dotnet new install Rhino.Templates`

Standard development commands are:

```bash
dotnet restore RhinoLayoutFoundry.sln
dotnet build RhinoLayoutFoundry.sln
dotnet test RhinoLayoutFoundry.sln
```

The test project also contains a zero-dependency local runner for constrained/offline environments. It is selected with `-p:UseLocalTestHarness=true`; normal CI and contributor builds use xUnit.

## Product principles

1. Batch operations must be understandable before they are committed.
2. Every document mutation must participate in Rhino Undo.
3. Foundry metadata must never prevent a 3DM from opening or behaving normally without the plug-in.
4. Large drawing sets must remain responsive through virtualization, targeted invalidation, and lazy rendering.
5. The tree-table, commands, and observer canvas must share one domain and mutation layer.
6. Cross-platform behavior is a release requirement, not a later port.

## License

Rhino Layout Foundry is licensed under the [MIT License](LICENSE).

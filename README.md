# Rhino Layout Foundry

Rhino Layout Foundry is a planned open-source layout manager for Rhino 8. It is designed for fast, pleasant batch work across many drawing sheets without forcing users to activate and edit each layout individually.

The main interface is a dockable tree-table:

```text
Folder
└── Sheet (Rhino page layout)
    ├── Detail viewport
    └── Detail viewport
```

The project is currently in its documentation and architecture phase. The product specification, technical boundaries, delivery sequence, and release gates are defined; implementation has not started.

## What it will do

- Organize Rhino sheets in nested folders with optional tags while leaving Rhino's native layout list intact.
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
- RhinoCommon for document and viewport integration
- Eto.Forms for the cross-platform docked panel
- Rhino Package Manager/Yak for distribution

RhinoCommon is Rhino's supported [cross-platform .NET plug-in SDK](https://developer.rhino3d.com/guides/rhinocommon/). Rhino 8.20 and later default to .NET 8, which is the project's initial runtime target. Rust is not part of the initial implementation; it would be considered only for an isolated workload after profiling demonstrates a material need.

## Documentation

- [Product specification](docs/PRODUCT_SPEC.md) — user workflows, requirements, scope, performance targets, and acceptance criteria
- [Architecture](docs/ARCHITECTURE.md) — system boundaries, data model, persistence, mutation pipeline, caching, and technical decisions
- [Roadmap](docs/ROADMAP.md) — start-to-finish milestones with dependencies and exit criteria
- [Testing and release](docs/TESTING_AND_RELEASE.md) — test strategy, compatibility matrix, performance gates, CI, and Yak releases

## Planned development prerequisites

Implementation contributors will need:

- Rhino 8.20 or later for Windows or macOS
- The .NET 8 SDK
- Visual Studio 2022 on Windows or Visual Studio Code on either platform
- Rhino's project templates, installed with `dotnet new install Rhino.Templates`

The solution, local launch profiles, test commands, and package scripts will be added in the foundation milestone described in the [roadmap](docs/ROADMAP.md).

## Product principles

1. Batch operations must be understandable before they are committed.
2. Every document mutation must participate in Rhino Undo.
3. Foundry metadata must never prevent a 3DM from opening or behaving normally without the plug-in.
4. Large drawing sets must remain responsive through virtualization, targeted invalidation, and lazy rendering.
5. The tree-table, commands, and observer canvas must share one domain and mutation layer.
6. Cross-platform behavior is a release requirement, not a later port.

## License

Rhino Layout Foundry is licensed under the [MIT License](LICENSE).

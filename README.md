# Rhino Layout Foundry

Rhino Layout Foundry is an open-source layout manager under active development for Rhino 8. It is designed for fast, pleasant batch work across many drawing sheets without forcing users to activate and edit each layout individually.

The main interface is a dockable tree-table:

```text
Folder
└── Sheet (Rhino page layout)
    ├── Detail viewport
    └── Detail viewport
```

Development now includes a functional existing-sheet batch editor, the first Milestone 5 creation slice, and an implementation-ready Milestone 6 observer. The plug-in provides a folder → sheet → detail tree-table, Finder-style hierarchy editing, mixed-selection folder/layout duplication and confirmed deletion, unit-aware batch properties, title-block assignment, hierarchy-ordered PDF output, and review-first batch layout creation. The same panel now provides List, Thumbnail, and Canvas modes. Thumbnail mode is a virtualized, resizable page-image grid; Canvas adds a Figma-like spatial board with physical paper proportions, progressive Rhino-rendered previews, pan/zoom/lasso, persisted spatial organization, shared selection, hierarchy/order operations, and named-view assignment. `LayoutFoundryObserver` remains a shortcut that opens the main panel directly in Canvas view. Licensed Rhino soak testing, Windows validation, shared-template asset import, title-block field mappings, and richer PDF presets/progress remain release gates. See the [development status](docs/DEVELOPMENT_STATUS.md) for exact capability and verification status.

## What it will do

- Organize Rhino sheets in nested folders while leaving Rhino's native layout list intact.
- Select folders, sheets, or individual details and change their properties in one operation.
- Rename sheets from previewable token and sequence rules.
- Create and maintain saved per-detail layer visibility and per-object display-mode rules.
- Designate, map, replace, or remove title-block instances across many sheets.
- Export an ordered folder or sheet selection to a multipage PDF.
- Capture layouts as reusable document-local or shared sheet templates.
- Create sheets in batches and assign named views to detail slots.
- Present every sheet on an observer canvas for navigation, spatial organization, reordering, and named-view assignment.
- Expose an approval-gated automation SDK for trusted companion tools without exposing arbitrary Rhino commands or raw model geometry.

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
- [Automation SDK](docs/AUTOMATION_SDK.md) — companion integration, capture consent, staged plans, and trust boundaries

## Developer setup and build

### Prerequisites

- Rhino 8.20 or later for Windows or macOS.
- The .NET 8 SDK and runtime. [`global.json`](global.json) may select a compatible newer SDK when 8 is unavailable, but the .NET 8 runtime is still required to execute the normal test suite. Verify both with `dotnet --version` and `dotnet --list-runtimes`.
- Git and, optionally, Visual Studio 2022 on Windows or Visual Studio Code with C# debugging support on either platform.
- Network access for the first NuGet restore.

Rhino's `dotnet new` templates are not required to build this repository. They are only useful when creating a new Rhino plug-in project.

### First build

From the repository root:

```bash
dotnet --version
dotnet restore RhinoLayoutFoundry.sln
dotnet build RhinoLayoutFoundry.sln --configuration Debug --no-restore -p:UseSharedCompilation=false
dotnet test tests/RhinoLayoutFoundry.Core.Tests/RhinoLayoutFoundry.Core.Tests.csproj --no-restore -p:RunAnalyzers=false --no-build
```

To rebuild only the loadable plug-in and its project dependencies after a code change:

```bash
dotnet build src/RhinoLayoutFoundry.Rhino/RhinoLayoutFoundry.Rhino.csproj \
  --configuration Debug --no-restore -p:UseSharedCompilation=false
```

That command is incremental; there is normally no reason to clean first. If generated output is stale, fully quit Rhino and run:

```bash
dotnet clean RhinoLayoutFoundry.sln --configuration Debug
dotnet build RhinoLayoutFoundry.sln --configuration Debug --no-restore -p:UseSharedCompilation=false
```

The development bundle is written to `src/RhinoLayoutFoundry.Rhino/bin/Debug/net8.0/` and must contain all of these files:

```text
RhinoLayoutFoundry.rhp
RhinoLayoutFoundry.Core.dll
RhinoLayoutFoundry.Extensibility.dll
RhinoLayoutFoundry.UI.dll
RhinoLayoutFoundry.deps.json
RhinoLayoutFoundry.runtimeconfig.json
```

Do not install or load a `net10.0` diagnostics build into Rhino 8.

### Rebuild and run on macOS

Rhino loads a copied plug-in bundle from the user plug-in directory. The install script copies the complete `Debug` or `Release` output; it does not build it.

1. Fully quit every Rhino process. A running Rhino process can retain the previous assemblies even after files are replaced.
2. Build, install, and reopen Rhino:

   ```bash
   dotnet build src/RhinoLayoutFoundry.Rhino/RhinoLayoutFoundry.Rhino.csproj \
     --configuration Debug --no-restore -p:UseSharedCompilation=false
   ./scripts/install-dev-macos.sh Debug
   open -a "Rhino 8"
   ```

3. In Rhino, run `PlugInManager` and confirm that Rhino Layout Foundry reports `Loaded: Yes`, then run `LayoutFoundry`.

By default the script installs to:

```text
~/Library/Application Support/McNeel/Rhinoceros/8.0/MacPlugIns/RhinoLayoutFoundry.rhp
```

Set `RHINO_LAYOUT_FOUNDRY_MAC_PLUGIN_DIR` before running the script to use a different bundle location. If the companion AI bundle is installed, the script also synchronizes its shared Foundry assemblies.

### Rebuild and run on Windows

Close every Rhino process before rebuilding because Rhino loads directly from the build output in this development workflow. In PowerShell from the repository root:

```powershell
dotnet build src/RhinoLayoutFoundry.Rhino/RhinoLayoutFoundry.Rhino.csproj `
  --configuration Debug --no-restore -p:UseSharedCompilation=false
$env:RHINO_PACKAGE_DIRS = "$PWD\src\RhinoLayoutFoundry.Rhino\bin\Debug\net8.0"
& "C:\Program Files\Rhino 8\System\Rhino.exe" /netcore
```

Run `PlugInManager` and `LayoutFoundry` as described for macOS. Repeat the close, build, and launch sequence after every plug-in change.

### VS Code shortcut

Open the repository folder and choose the `Rhino 8 (macOS)` or `Rhino 8 (Windows)` launch configuration, then press F5. The macOS configuration builds and installs the bundle before launching Rhino; the Windows configuration builds it and sets `RHINO_PACKAGE_DIRS`. Quit an already-running Rhino instance first.

### Common build problems

- **`NETSDK` or target-framework error:** run `dotnet --info` and confirm that the SDK selected through `global.json` is installed and that the build still targets `net8.0`.
- **The build stalls without diagnostics:** run `dotnet build-server shutdown`, then retry the build with `--disable-build-servers -m:1` to bypass a stuck compiler or parallel MSBuild node.
- **`Missing build output` from the macOS installer:** build the Rhino project first and pass the same configuration name to both commands, such as `Debug` or `Release`.
- **The old UI still appears:** fully quit and reopen Rhino; closing the model or panel is not enough. Confirm the configuration and bundle path reported by the commands.
- **Restore fails offline:** restore once with network access so the centrally pinned NuGet packages are available locally; normal development and CI use xUnit.

For the full in-Rhino acceptance checklist, continue with the [Rhino smoke test](docs/RHINO_SMOKE_TEST.md).

## Product principles

1. Batch operations must be understandable before they are committed.
2. Every document mutation must participate in Rhino Undo.
3. Foundry metadata must never prevent a 3DM from opening or behaving normally without the plug-in.
4. Large drawing sets must remain responsive through virtualization, targeted invalidation, and lazy rendering.
5. The tree-table, commands, and observer canvas must share one domain and mutation layer.
6. Cross-platform behavior is a release requirement, not a later port.

## License

Rhino Layout Foundry is licensed under the [MIT License](LICENSE).

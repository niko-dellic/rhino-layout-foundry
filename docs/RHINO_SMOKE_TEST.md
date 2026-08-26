# Rhino 8 Development Smoke Test

Use this checklist for the first macOS and Windows development loads. Record the Rhino version, operating system, .NET SDK version, and any command-history errors with the result.

## 1. Build the Rhino 8 target

The build machine must have a .NET 8 SDK. A .NET 10-only build is useful for compile diagnostics but must not be loaded into Rhino 8.

```bash
dotnet --version
dotnet restore RhinoLayoutFoundry.sln
dotnet build RhinoLayoutFoundry.sln
dotnet test RhinoLayoutFoundry.sln --no-build
```

Confirm that the output directory contains the plug-in and both project dependencies:

```text
src/RhinoLayoutFoundry.Rhino/bin/Debug/net8.0/
├── RhinoLayoutFoundry.rhp
├── RhinoLayoutFoundry.Core.dll
└── RhinoLayoutFoundry.UI.dll
```

## 2. Install and launch the development build on macOS

Quit every running Rhino instance. Rhino for Mac expects a plug-in bundle directory whose name ends in `.rhp`. From the repository root, install the current development output into the user plug-in folder:

```bash
plugin_dir="$HOME/Library/Application Support/McNeel/Rhinoceros/8.0/MacPlugIns/RhinoLayoutFoundry.rhp"
mkdir -p "$plugin_dir"
cp src/RhinoLayoutFoundry.Rhino/bin/Debug/net8.0/RhinoLayoutFoundry.rhp \
  src/RhinoLayoutFoundry.Rhino/bin/Debug/net8.0/RhinoLayoutFoundry.Core.dll \
  src/RhinoLayoutFoundry.Rhino/bin/Debug/net8.0/RhinoLayoutFoundry.UI.dll \
  src/RhinoLayoutFoundry.Rhino/bin/Debug/net8.0/RhinoLayoutFoundry.deps.json \
  src/RhinoLayoutFoundry.Rhino/bin/Debug/net8.0/RhinoLayoutFoundry.runtimeconfig.json \
  "$plugin_dir/"
open -a "Rhino 8"
```

The same install can be repeated with `scripts/install-dev-macos.sh Debug`, or by launching the `Rhino 8 (macOS)` configuration from VS Code. After replacing a loaded build, fully quit and restart Rhino. Confirm the plug-in under `PlugInManager`; it should report **Loaded: Yes** and list `LayoutFoundry` as a registered command.

## 3. Launch the development build on Windows

Close every running Rhino instance. From PowerShell in the repository root, run:

```powershell
$env:RHINO_PACKAGE_DIRS = "$PWD\src\RhinoLayoutFoundry.Rhino\bin\Debug\net8.0"
& "C:\Program Files\Rhino 8\System\Rhino.exe" /netcore
```

Alternatively, launch the `Rhino 8 (Windows)` configuration from VS Code.

## 4. Panel and hierarchy checks

1. Create a new model and run `LayoutFoundry`.
2. Confirm Rhino prints `Layout Foundry panel opened; Eto instance created.` and that exactly one docked panel opens. An empty document should report no layout sheets.
3. Create three layouts. Put two detail viewports on one layout and one detail on another.
4. Run `LayoutFoundry` again if the panel is not already visible.
5. Confirm the tree reads `Unorganized → Sheet → Detail viewport` and reports the correct totals.
6. Expand and collapse folders and sheets.
7. Select multiple rows using the platform-standard modifier key.
8. Filter by a sheet or detail name. Confirm matching rows retain their folder and sheet ancestors.
9. Clear the filter and confirm the complete tree returns.

## 5. Rename and Undo checks

1. Select exactly one sheet row.
2. Enter a unique name and click **Rename**.
3. Confirm the Rhino layout tab and Foundry tree both change.
4. Run Rhino `Undo` once. Confirm the original name returns.
5. Run Rhino `Redo` once. Confirm the new name returns.
6. Try an empty name, a duplicate name with different letter casing, and the current name. Confirm each is rejected without changing Rhino.
7. Select a folder, detail, or multiple rows. Confirm the single-sheet rename control cannot apply.

Passing requires the successful rename to occupy exactly one Rhino undo step.

## 6. Document lifecycle checks

1. Open a second document and switch between both documents. Confirm the panel follows the active document.
2. Close the inactive document. Confirm the active tree remains unchanged.
3. Close the active document. Confirm the panel safely reports no active document or follows Rhino's replacement active document.
4. Repeatedly run `LayoutFoundry`. Confirm it focuses the existing panel instead of creating duplicates.
5. Save, close, and reopen a document. Confirm empty Foundry schema-1 data does not produce an error.
6. Quit Rhino normally and confirm no shutdown exception appears.

## 7. Reporting a failure

Include:

- Rhino `SystemInfo` output;
- `dotnet --info` output;
- the failed checklist step;
- Rhino command history and startup error text;
- whether the failure reproduces after fully quitting Rhino;
- the contents of the `bin/Debug/net8.0` directory.

Do not substitute or install the `bin/Debug/net10.0` verification artifact into Rhino 8.

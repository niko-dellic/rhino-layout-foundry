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
5. Confirm root-level sheets appear directly in the tree (with no `Unorganized` row), details expand beneath their sheets, and the totals are correct.
6. Confirm `Layout Foundry` is white/primary-color and left aligned, the Rhino filename is not repeated, and `Layouts · n sheets · n details` appears in the hierarchy header.
7. Create, rename, or delete a layout in Rhino and confirm the panel updates automatically without a Refresh control.
8. Confirm Add Folder, Open, Manage, and Clear occupy a thin first toolbar row. Confirm the native styled Search control, including its magnifier, and row filter occupy the second row; unavailable actions are visibly disabled.
9. Exercise All rows, Sheets, Details, Tagged, and Untagged filters, then clear them with `×`.
10. Double-click a sheet and a detail, then repeat with Enter; confirm Rhino activates the corresponding page/detail. Press Escape and confirm selection clears.
11. Expand and collapse folders and sheets.
12. Select multiple rows using the platform-standard modifier key.
    Confirm the selection summary appears in the hierarchy header and the toolbar buttons do not move.
13. Filter by a sheet or detail name. Confirm matching rows retain their folder and sheet ancestors.
14. Clear the filter and confirm the complete tree returns.
15. With no folder selected, create a folder. Confirm a `New Folder` row appears at the hierarchy root with its name selected for in-place editing and no modal opens. Name it and press Return.
16. With that folder selected, create another folder and confirm its editable draft row appears nested inside the selected folder. Press Escape once and confirm the draft disappears without changing the document; create it again and commit a name.
17. Run Rhino Undo and Redo; confirm the nested folder disappears and returns as one named undo action.
18. Save As, close, and reopen the 3DM; confirm both folders and their nesting survive.
19. Right-click root whitespace, a folder, a sheet, and a detail. Confirm New Folder and New Layout target the root, selected folder, or containing folder as appropriate. Folder-only Rename/Delete actions must only appear for a folder target. Sheet/detail targets must expose Set Current, New Layout, Duplicate Layout, Delete, Rename, New Detail, Print, and Properties.
20. Choose Rename on a sheet, enter a distinct name directly in its hierarchy row, and press Return. Confirm both Foundry and Rhino's native Layouts panel update and Foundry reports that the page rename is not undoable.
20. Right-click an empty folder, delete it after the confirmation, then Undo/Redo. Confirm each state updates automatically and the Rhino Edit menu names the folder action.
21. Drag a sheet row onto a folder. Confirm the sheet becomes a child of that folder and one Undo returns it to its original location.
22. Expand a sheet and drag its detail row onto a different folder. Confirm the containing sheet moves, the detail remains beneath that sheet, and no detail is deleted or detached.
23. Select multiple sheet/detail rows and drag one selected row onto a folder. Confirm each distinct containing sheet moves once in one Undo action.
24. Drag a sheet to hierarchy whitespace/root. Confirm it returns to the top level.
25. Drag a folder onto another folder and confirm its complete subtree moves. Attempt to drag a folder onto itself and onto one of its descendants; confirm both are rejected without changing Rhino.
26. Right-click a folder and choose New Page. Confirm an inline `New Page` draft appears in that folder and Return creates one native Rhino layout in that folder. Confirm Foundry clearly reports that Rhino does not support Undo for layout creation and that pressing Undo does not partially detach the page from its Foundry folder.
27. Repeat sheet and folder drags several times, including after panel resize. Confirm Rhino remains responsive, native move feedback appears, and no AppKit exception or crash occurs.

## 5. Preview, diagnostics, and responsive checks

1. Confirm sheet text appears before previews and that at most one preview is captured at a time.
2. On Windows, wait for visible sheet previews to populate. On macOS, confirm the resize-safe text-only hierarchy does not start preview capture.
3. Create or open a diagnostic fixture with a duplicate sheet name, missing Foundry folder reference, or sheet without details. Confirm the Status column reports `Info · n`, `Warning · n`, or `Error · n` without hiding the row.
4. On Windows, resize across compact, standard, and wide breakpoints; confirm filters and selection survive. On macOS, close and reopen the panel at the intended dock width to select its density, then confirm native splitter resizing remains responsive.
5. Confirm both toolbar rows remain compact and no separate gray surface bands appear behind them. On macOS, confirm metadata and diagnostics appear inline in the single hierarchy column.

## 6. Batch-properties and mutation-capability checks

1. Select exactly one sheet row.
2. Confirm unsafe inline rename controls are absent while page-name Undo is unsupported.
3. Use the Manage icon and confirm its tooltip is present and Targets, Properties, and Review tabs open in a native modal.
4. Toggle target inclusion and stage values. Confirm Rhino does not change before Apply.
5. Confirm Apply remains disabled and exposes the page-property Undo capability reason.
6. Close the modal and confirm selection remains intact.

Do not enable page rename or batch Apply until a supported cross-platform Rhino Undo path has passed the integration criterion.

## 7. Document lifecycle checks

1. Open a second document and switch between both documents. Confirm the panel follows the active document.
2. Close the inactive document. Confirm the active tree remains unchanged.
3. Close the active document. Confirm the panel safely reports no active document or follows Rhino's replacement active document.
4. Repeatedly run `LayoutFoundry`. Confirm it focuses the existing panel instead of creating duplicates.
5. Save, close, and reopen a document. Confirm empty Foundry schema-1 data does not produce an error.
6. Quit Rhino normally and confirm no shutdown exception appears.

## 8. Reporting a failure

Include:

- Rhino `SystemInfo` output;
- `dotnet --info` output;
- the failed checklist step;
- Rhino command history and startup error text;
- whether the failure reproduces after fully quitting Rhino;
- the contents of the `bin/Debug/net8.0` directory.

Do not substitute or install the `bin/Debug/net10.0` verification artifact into Rhino 8.

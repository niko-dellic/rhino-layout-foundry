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

## Template and batch-creation smoke test

1. Open a document with at least one layout containing a detail; optionally place one block instance in page space.
2. Select that layout in Foundry and click the diamond toolbar action. Name the template, choose the page-space block explicitly if it is the title block, and capture it.
3. Click the boxed-plus toolbar action. Set quantities on at least two templates with different paper sizes, choose a destination folder, and verify the before/after list contains unique expected names.
4. Optionally choose one named view, create the layouts, and confirm each generated page has the expected dimensions, detail rectangles/cameras, metadata, and title block.
5. Save, close, and reopen the 3DM; confirm the template library remains available and the hierarchy is intact, including for a document first saved with schema v1.
6. Test one missing block-definition case and confirm preflight warns while sheets remain creatable without the block.
7. Record that Rhino page creation is currently non-undoable; verify an injected failed batch leaves no generated pages rather than relying on Undo.

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
9. Exercise All rows, Sheets, and Details filters, then clear them with `×`.
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
20. Right-click a populated folder and confirm `Print Folder…` is available; confirm it is disabled for an empty folder. Export the populated folder and verify the PDF contains only that folder's layouts, including nested folders, in visible tree order. Right-click hierarchy whitespace, choose `Print All…`, and verify the PDF contains every layout in tree order.
20. Choose Rename on a sheet, enter a distinct name directly in its hierarchy row, and press Return. Confirm both Foundry and Rhino's native Layouts panel update and Foundry reports that the page rename is not undoable.
20. Right-click an empty folder, delete it after the confirmation, then Undo/Redo. Confirm each state updates automatically and the Rhino Edit menu names the folder action.
21. Drag a sheet row onto a folder. Confirm the sheet becomes a child of that folder and one Undo returns it to its original location.
22. Expand a sheet and drag its detail row onto a different folder. Confirm the containing sheet moves, the detail remains beneath that sheet, and no detail is deleted or detached.
23. Select two layouts, right-click either selected row, and confirm Duplicate and Delete remain available. Duplicate once and verify both copies retain folder metadata.
24. Repeat with two folders and then with one folder plus one layout outside it. Confirm the full selection is duplicated once, and cancel the Delete warning after verifying its folder/layout totals.
25. Select a folder and one of its contained layouts. Duplicate and confirm the contained layout is copied only once as part of the folder subtree.
26. Open Batch Properties, enable display mode, type a distinctive substring, and confirm the open list contains only matching modes; clear the text and confirm the full list returns.
27. Add page-space block instances to two source sheets. Confirm the Title block selector distinguishes them by definition, source sheet, and short object ID. Assign one to two other sheets, then replace and remove it; confirm only the designated instances change.
23. Select multiple sheet/detail rows and drag one selected row onto a folder. Confirm each distinct containing sheet moves once in one Undo action.
24. Drag a sheet to hierarchy whitespace/root. Confirm it returns to the top level.
25. Drag a folder onto another folder and confirm its complete subtree moves. Attempt to drag a folder onto itself and onto one of its descendants; confirm both are rejected without changing Rhino.
26. Choose New Layout from the `+` menu, folder menu, and root context menu. Confirm each opens the review-first creation dialog with the correct destination. Set a quantity greater than one and verify the result table shows every proposed name, layout type, paper size, detail count, display mode, and title block before Apply.
27. Exercise Blank, 1 Detail, both 2 Detail arrangements, 4 Detail Grid, and one captured template. Change A/ANSI paper size, orientation, custom dimensions/units, display mode, title block, and named view. Confirm the preview updates immediately and the created Rhino layouts match it. Confirm Foundry reports that Rhino does not support Undo for layout creation and rolls back the whole batch if one layout fails.
27. Repeat sheet and folder drags several times, including after panel resize. Confirm Rhino remains responsive, native move feedback appears, and no AppKit exception or crash occurs.

## 5. Preview, diagnostics, and responsive checks

1. Confirm sheet text appears before previews and that at most one preview is captured at a time.
2. On Windows, wait for visible sheet previews to populate. On macOS, confirm the resize-safe text-only hierarchy does not start preview capture.
3. Create or open a diagnostic fixture with a duplicate sheet name, missing Foundry folder reference, or sheet without details. Confirm the Status column reports `Info · n`, `Warning · n`, or `Error · n` without hiding the row.
4. On Windows, resize across compact, standard, and wide breakpoints; confirm filters and selection survive. On macOS, close and reopen the panel at the intended dock width to select its density, then confirm native splitter resizing remains responsive.
5. Confirm both toolbar rows remain compact and no separate gray surface bands appear behind them. Confirm the hierarchy shows fixed Name, Print, Paper size, Details, Display mode, and Status columns.
6. Confirm the first column header remains exactly `Layouts`; sheet/detail totals, filtered results, and selection counts must update in the bottom bar without shifting any column header.
7. Confirm the active 3DM filename without its extension appears as the top project-root row. Collapse it, trigger an automatic refresh, and confirm it remains collapsed. Confirm its context menu offers root creation and Print All but not rename, drag, duplicate, or delete.
8. Click every property header twice. Confirm each sibling group sorts ascending and then descending without flattening folders; verify `Page 2` sorts before `Page 10` by name.
9. Resize the dock repeatedly on macOS, including narrow and wide extremes. Confirm Rhino remains responsive, columns keep fixed widths, and no thumbnail or column-visibility work occurs during splitter tracking.
10. Toggle the Print light on one layout and confirm Print All omits it. Toggle a populated folder and confirm every descendant layout follows; a mixed folder shows the mixed indicator.
11. Change a folder paper preset and confirm every descendant layout changes without activation. Change a sheet preset and confirm siblings do not change.
12. Change display mode on a folder, a sheet, and a single detail. Confirm the scopes are respectively all descendant details, all details on the sheet, and only the selected detail.

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

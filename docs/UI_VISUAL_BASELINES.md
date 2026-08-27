# UI Visual Baselines

These baselines define the management shell's required states before platform screenshots are accepted. Native Eto controls may differ slightly between Windows and macOS, but hierarchy, spacing, visibility, and interaction states must remain equivalent.

## Viewports

| Baseline | Width | Expected layout |
| --- | ---: | --- |
| Compact dock | 320 px | Compact action row above a native search/filter row; 48 × 32 previews; secondary column collapses on Windows and remains horizontally accessible on Mac |
| Standard dock | 420 px | Compact action row above a native search/filter row; secondary/status columns visible; 56 × 38 previews |
| Wide/floating | 700 px | Compact action row above a native search/filter row; 72 × 48 previews |

On macOS, the docked hierarchy uses one text-only column. Detail/tag metadata and diagnostic badges are folded into the row label, and inline thumbnails are disabled. This avoids a Rhino/Eto native recursion observed while resizing docked multi-column/image TreeGridViews. Windows retains the full multi-column thumbnail table and live breakpoint transitions.

## Required states

1. No active document
   - Filename is never repeated.
   - `Layout Foundry` is left aligned and uses the primary foreground color.
   - Empty-state copy is centered and actionable.
   - No disabled mutation controls occupy space.
2. Empty document
   - The hierarchy header reads `Layouts · 0 sheets · 0 details` whenever the empty hierarchy is shown.
   - The internal root folder is absent.
3. Populated hierarchy while previews load
   - Text appears immediately.
   - Folder and sheet rows use familiar folder (`📁`) and folded-page (`📄`) icons matching Rhino's Layouts-panel semantics; details retain a distinct `⌗` marker.
   - Preview placeholders do not block selection or scrolling.
   - At most one Rhino preview is captured at a time.
4. Populated hierarchy with previews
   - Sheet previews align with names without obscuring disclosure triangles.
   - Detail and folder rows do not display misleading page previews.
5. Diagnostics
   - Status cells use `Info · n`, `Warning · n`, or `Error · n`.
   - Missing references remain visible and selectable.
6. Filtered hierarchy
   - Clear remains a compact utility; there is no manual refresh control.
   - Visible-result counts match the rendered tree.
7. Single and multiple selection
   - The summary names row kinds.
   - The summary is appended to the hierarchy header; it never changes toolbar geometry.
   - Add Folder, Open, Manage, and Clear occupy the first compact row; native Search with a magnifier and the filter occupy the row below.
   - Add Folder creates at the root with no folder selected and inside the one selected folder otherwise.
   - Folder creation, Undo/Redo, and saved 3DM restoration update the hierarchy automatically.
   - Context menus expose New Folder and New Page at the clicked hierarchy location; folder-only Rename and guarded Delete actions only appear on folder rows.
   - New folders and pages appear as editable draft rows in the destination hierarchy; no creation modal is shown.
   - Sheet, detail, and folder rows use the internal hierarchy move gesture; invalid file-row targets and folder cycles are rejected without starting an OS drag session.
   - Unsafe inline rename controls are absent while page-name Undo is unsupported.
8. Batch properties dialog
   - Targets, Properties, and Review are separate tabs.
   - Inclusion checkboxes never imply deletion.
   - Apply remains visibly disabled with the Undo capability reason.

## Interaction checks

- Enter and double-click activate sheets/details.
- Escape clears selection.
- Right-clicking a row targets that row without discarding an existing multiselection when the row is already selected.
- Dragging a sheet or detail into a folder moves the containing sheet atomically; dropping in root whitespace returns it to the top level.
- Detail selection and its ancestor expansion survive active-view events.
- Windows: resizing across compact and wide thresholds does not lose filters, selection, or cached previews.
- macOS: the single-column hierarchy remains responsive through repeated narrow/wide splitter cycles; selection and contextual actions survive resizing.
- Light/dark theme colors come from Rhino/Eto system colors.
- Rhino document/view events update the hierarchy automatically without a manual refresh action.
- Toolbar and hierarchy gutters use one Rhino/Eto system background token; only native input and button controls have their own state surfaces.
- Keyboard focus reaches search, filter, hierarchy actions, hierarchy, dialog tabs, staged fields, Close, and Apply in that order.

Platform screenshot files will be added after this state contract passes on dedicated Windows and macOS Rhino hosts.

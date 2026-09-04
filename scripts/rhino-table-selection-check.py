"""Mac regression check using the three actual installed Foundry controls.

Start with a NEW, empty, unsaved Rhino model. Run through RunPythonScript once
per SURFACE (edit the value below). The first run adds six disposable pages and
layers. Close the main test window; use Cancel in the other two dialogs. Never
save/create/apply from the diagnostic dialogs. Discard the fixture afterwards.

Drag down and up through multiple rows (use Details in the main table so a name
cell does not initiate reordering). Check full-width native gray selection while
held. The JSON report records native selection notifications before mouse-up;
selected cells must be transparent and native striping enabled. This is a native
manual regression check, not a replacement for visual inspection or core tests.
"""
import clr
import json
import os
import tempfile
import traceback
import System
import Rhino
import scriptcontext
from System.Reflection import BindingFlags

clr.AddReference('Eto')
clr.AddReference('Eto.macOS')
clr.AddReference('Microsoft.macOS')
from Eto.Forms import Form, MacOSHelpers
from Eto.Drawing import Size
from AppKit import NSTableView, NSOutlineView, NSApplication
from Foundation import NSNotificationCenter

SURFACE = 'table view'  # 'appearance state' or 'sheet creation'
REPORT = os.path.join(tempfile.gettempdir(), 'foundry-three-tables.json')
INSTANCE = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
STATIC = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
FIXTURE_KEY = 'foundry_table_selection_fixture'
WINDOW_KEY = 'foundry_table_selection_window'
keep = []
try:
    with open(REPORT) as source:
        results = json.load(source)
except (IOError, ValueError):
    results = []


def record(value):
    results.append(value)
    with open(REPORT, 'w') as output:
        json.dump(results, output, indent=2)


def field(obj, name):
    return obj.GetType().GetField(name, INSTANCE).GetValue(obj)


def find_table(view):
    if isinstance(view, NSTableView):
        return view
    for child in view.Subviews:
        found = find_table(child)
        if found is not None:
            return found
    return None


def instrument(grid):
    colors = {}

    def formatted(sender, event):
        colors[(event.Row, str(event.Column.HeaderText))] = event
    grid.CellFormatting += formatted

    def loaded(sender, event):
        native = find_table(MacOSHelpers.ToNative(grid, False))

        def changed(notification):
            try:
                selected = [int(i.ToUInt64()) for i in native.SelectedRows]
                record({
                    'surface': SURFACE,
                    'event': str(NSApplication.SharedApplication.CurrentEvent.Type),
                    'selected': selected,
                    'formatted_cell_count': len(colors),
                    'opaque_selected_cells': [str(key) for key, value in colors.items()
                                              if key[0] in selected and value.BackgroundColor.A > 0],
                    'native_striping': bool(native.UsesAlternatingRowBackgroundColors),
                })
            except:
                record({'error': traceback.format_exc()})

        # Outline views publish their own notification, not NSTableView's.
        notification = (NSOutlineView.SelectionIsChangingNotification
                        if isinstance(native, NSOutlineView)
                        else NSTableView.SelectionIsChangingNotification)
        observer = NSNotificationCenter.DefaultCenter.AddObserver(notification, changed, native)
        keep.append(observer)

        def unloaded(sender, event):
            NSNotificationCenter.DefaultCenter.RemoveObserver(observer)
            observer.Dispose()
        grid.UnLoad += unloaded
        if isinstance(native, NSOutlineView):
            grid.ReloadData()
    grid.Load += loaded


def run(sender, event):
    Rhino.RhinoApp.Idle -= run
    try:
        assert SURFACE in ['table view', 'appearance state', 'sheet creation']
        previous = scriptcontext.sticky.get(WINDOW_KEY)
        assert previous is None or not previous.Visible, 'Close the previous diagnostic first'
        doc = Rhino.RhinoDoc.ActiveDoc
        assert doc and not doc.Path, 'Use a NEW unsaved model'
        if scriptcontext.sticky.get(FIXTURE_KEY) != doc.RuntimeSerialNumber:
            assert not doc.Views.GetPageViews() and doc.Objects.Count == 0, 'Use a NEW empty model'
            for i in range(6):
                doc.Views.AddPageView('Selection check ' + str(i + 1), 420, 297)
                layer = Rhino.DocObjects.Layer()
                layer.Name = 'Selection layer ' + str(i + 1)
                doc.Layers.Add(layer)
            scriptcontext.sticky[FIXTURE_KEY] = doc.RuntimeSerialNumber
        ui = next(a for a in System.AppDomain.CurrentDomain.GetAssemblies()
                  if a.GetName().Name == 'RhinoLayoutFoundry.UI' and a.Location)
        host = ui.GetType('RhinoLayoutFoundry.UI.LayoutFoundryUiHost')
        snapshot = host.GetMethod('CaptureSnapshot', STATIC).Invoke(None, None)

        def make(name, *values):
            return System.Activator.CreateInstance(
                ui.GetType('RhinoLayoutFoundry.UI.' + name), INSTANCE, None,
                System.Array[System.Object](values), None)

        if SURFACE == 'table view':
            panel = make('LayoutFoundryPanel')
            window = Form(Title='Foundry table view selection check',
                          ClientSize=Size(1200, 570), Content=panel)
            instrument(field(panel, '_treeGrid'))
        elif SURFACE == 'appearance state':
            window = make('AppearanceStateEditorDialog', snapshot, snapshot.RootFolderId, 'Selection check')
            instrument(field(field(window, '_rules'), '_tree'))
        else:
            window = make('BatchCreateLayoutsDialog', snapshot, snapshot.RootFolderId)
            field(window, '_quantityStepper').Value = 6
            instrument(field(window, '_previewGrid'))
        keep.append(window)
        scriptcontext.sticky[WINDOW_KEY] = window
        scriptcontext.sticky['foundry_table_selection_lifetime'] = keep
        if SURFACE == 'table view':
            window.Show()
        else:
            window.ShowModal()
            if SURFACE == 'sheet creation':
                cleanup = window.GetType().GetProperty('PreviewCleanup', INSTANCE).GetValue(window)

                def cleaned(sender, event):
                    if not cleanup.IsCompleted:
                        return
                    Rhino.RhinoApp.Idle -= cleaned
                    record({'surface': SURFACE, 'preview_cleanup': str(cleanup.Status)})
                Rhino.RhinoApp.Idle += cleaned
    except:
        record({'error': traceback.format_exc()})
        Rhino.RhinoApp.WriteLine(traceback.format_exc())


Rhino.RhinoApp.Idle += run

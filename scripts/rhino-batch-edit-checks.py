"""Native regression checks for combined detail edits and compensation.

Run in a new, unsaved diagnostic model (never in a user's project). Schedules on
Idle so the script command has completed. Set CANDIDATE_HOST to an isolated RHP
for testing without replacing the loaded plugin. Reports to the OS temp folder.
"""
import Rhino
import System
import json
import os
import tempfile
import traceback
from System.Reflection import BindingFlags

CANDIDATE_HOST = None
FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
STATIC = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic

def run(sender, event):
    Rhino.RhinoApp.Idle -= run
    results = []
    try:
        import clr
        clr.AddReference('RhinoLayoutFoundry.Core')
        from System.Collections.Generic import List, Dictionary
        from RhinoLayoutFoundry.Core.Domain import DocumentState
        from RhinoLayoutFoundry.Core.Persistence import DocumentStateSerializer
        from RhinoLayoutFoundry.Core.Operations import BatchUpdateSheetsRequest, BatchUpdateSheetsPlanner, BatchDetailUpdate, BatchUpdateSheetsChange
        from RhinoLayoutFoundry.Core.Overview import OverviewInvalidation
        from Rhino.Geometry import Point2d, Point3d
        from Rhino.Display import DisplayModeDescription, DefinedViewportProjection
        from Rhino.DocObjects import ViewportInfo
        doc = Rhino.RhinoDoc.ActiveDoc
        assert doc and not doc.Path, 'Use a new unsaved diagnostic model'
        assert all(p.PageName in ['Cached', 'Fresh'] or p.PageName.startswith('Batch-check-') for p in doc.Views.GetPageViews()), 'Unexpected existing layouts'
        host = System.Reflection.Assembly.Load(System.IO.File.ReadAllBytes(CANDIDATE_HOST)) if CANDIDATE_HOST else next(a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name == 'RhinoLayoutFoundry')
        def args(*values): return System.Array[System.Object](values)
        def make(name, *values): return System.Activator.CreateInstance(host.GetType('RhinoLayoutFoundry.Rhino.' + name), FLAGS, None, args(*values), None)
        def call(target, name, *values): return target.GetType().GetMethod(name, FLAGS).Invoke(target, args(*values))
        store = make('DocumentStateStore')
        tracker = make('DocumentRevisionTracker')
        provider = make('RhinoDocumentSnapshotProvider', store, tracker)
        executor = make('RhinoMutationExecutor', tracker, store, System.Action[OverviewInvalidation](lambda _: None))
        wire = DisplayModeDescription.WireframeId
        shaded = DisplayModeDescription.ShadedId
        target = Point3d(30, 40, 50)
        source = doc.Views.GetStandardRhinoViews()[0].ActiveViewport
        named = 'Batch-check-camera-' + str(System.Guid.NewGuid())
        original_source = ViewportInfo(source)
        try:
            source.SetCameraLocations(Point3d.Origin, target)
            assert doc.NamedViews.Add(named, source.Id) >= 0
        finally:
            source.SetViewProjection(original_source, True)
            original_source.Dispose()
        saved_view = doc.NamedViews[doc.NamedViews.FindByName(named)].Viewport
        target = saved_view.CameraLocation
        direction = saved_view.CameraDirection

        def check(active, fail):
            page = doc.Views.AddPageView('Batch-check-' + str(System.Guid.NewGuid()), 420, 297)
            details = [page.AddDetailView('D' + str(i), Point2d(10 + i * 200, 10), Point2d(200 + i * 200, 287), DefinedViewportProjection.Top) for i in range(2)]
            ids = [d.Id for d in details]
            viewport_ids = [d.Viewport.Id for d in details]
            cameras = [str(d.Viewport.CameraLocation) for d in details]
            assert all(d.Viewport.CameraLocation.DistanceTo(target) > 1 for d in details), 'Fixture must exercise an actual camera change'
            if active:
                doc.Views.ActiveView = page
                page.SetActiveDetail(ids[0])
            else:
                page.SetPageAsActive()
            state = executor.GetType().GetMethod('WithCurrentPageRecords', STATIC).Invoke(None, args(doc, call(store, 'Get', doc)))
            call(store, 'Set', doc, state)
            before = DocumentStateSerializer.Serialize(state)
            snapshot = call(provider, 'Capture')
            updates = List[BatchDetailUpdate]()
            # A named-view-only row may still carry its previous display-mode ID.
            # It must preserve the newly requested sheet display mode.
            updates.Add(BatchDetailUpdate(viewport_ids[0], True, named, False, wire))
            request = BatchUpdateSheetsRequest(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision, System.Array[System.Guid]([page.MainViewport.Id]), None, 1, 1, None, None, None, shaded, DetailUpdates=updates)
            plan = BatchUpdateSheetsPlanner().Plan(request, snapshot)
            assert plan.CanApply, str(list(plan.Diagnostics))
            if fail:
                updates.Add(BatchDetailUpdate(viewport_ids[1], True, '__missing_batch_check_view__', False, wire))
                change = BatchUpdateSheetsChange(System.Array[System.Guid]([page.MainViewport.Id]), Dictionary[System.Guid, System.String](), None, None, None, shaded, DetailUpdates=updates)
                result = call(executor, 'ApplyBatchUpdate', doc, plan, change)
                assert not result.Succeeded
                assert 'Recovery is incomplete' not in ' '.join(d.Message for d in result.Diagnostics), 'Unexpected incomplete recovery'
                for i, oid in enumerate(ids):
                    current = doc.Objects.FindId(oid)
                    assert current.Viewport.DisplayMode.Id == wire, 'Display mode not restored'
                    assert str(current.Viewport.CameraLocation) == cameras[i], 'Camera not restored'
                assert DocumentStateSerializer.Serialize(call(store, 'Get', doc)) == before, 'Metadata not restored'
            else:
                result = call(executor, 'Apply', doc, plan)
                assert result.Succeeded, ' '.join(d.Message for d in result.Diagnostics)
                current = doc.Objects.FindId(ids[0])
                assert current.Viewport.DisplayMode.Id == shaded, 'Sheet mode lost to stale detail mode'
                assert current.Viewport.CameraLocation.DistanceTo(target) < 0.0001, 'Named-view camera not applied: current=' + str(current.Viewport.CameraLocation) + ', saved=' + str(target)
                assert current.Viewport.CameraDirection.IsParallelTo(direction) == 1, 'Named-view direction not applied'
                assert call(store, 'Get', doc).Sheets[page.MainViewport.Id].DetailNamedViews[viewport_ids[0]] == named
        for active in [False, True]:
            for fail in [False, True]:
                name = ('active' if active else 'inactive') + ' detail: ' + ('failure restores camera/mode/metadata' if fail else 'combined sheet mode and named view')
                try:
                    check(active, fail)
                    results.append({'name': name, 'passed': True})
                except:
                    results.append({'name': name, 'passed': False, 'error': traceback.format_exc()})
        doc.Views.Redraw()
    except:
        results.append({'name': 'setup', 'passed': False, 'error': traceback.format_exc()})
    with open(os.path.join(tempfile.gettempdir(), 'foundry-batch-edit-checks.json'), 'w') as output:
        json.dump(results, output, indent=2)
    for result in results: print(('PASS ' if result['passed'] else 'FAIL ') + result['name'])

Rhino.RhinoApp.Idle += run
print('Batch edit checks scheduled on Idle.')

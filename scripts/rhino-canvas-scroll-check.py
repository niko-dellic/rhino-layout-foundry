"""RunPythonScript boundary checks for canvas/tree scroll routing.

Uses a separate, undisplayed canvas with synthetic rows. No Rhino document
changes or input events are dispatched. CANDIDATE_UI may name an isolated build;
None tests the installed assembly. A candidate run also records the old baseline.
Physical trackpad pan/pinch and focus transitions still require manual checks.
"""
import clr
import json
import os
import tempfile
import traceback
import System
from System.Reflection import BindingFlags, Assembly

clr.AddReference('Eto')
clr.AddReference('RhinoLayoutFoundry.Core')
from Eto.Drawing import PointF, Size, SizeF
from Eto.Forms import MouseEventArgs, MouseButtons, Keys, UITimer
from RhinoLayoutFoundry.Core.Overview import OverviewNodeKey, OverviewNodeKind

CANDIDATE_UI = None
FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
loaded = next(a for a in System.AppDomain.CurrentDomain.GetAssemblies()
              if a.GetName().Name == 'RhinoLayoutFoundry.UI' and a.Location)
paths = [('installed', loaded.Location)]
if CANDIDATE_UI:
    paths.append(('candidate', CANDIDATE_UI))
report = []


def args(*values):
    return System.Array[System.Object](values)


def run(label, path):
    assembly = Assembly.Load(System.IO.File.ReadAllBytes(path))
    canvas_type = assembly.GetType('RhinoLayoutFoundry.UI.ObserverCanvasDrawable')
    row_type = canvas_type.GetNestedType('CanvasNavigatorRow', BindingFlags.NonPublic)
    canvas = System.Activator.CreateInstance(canvas_type, FLAGS, None, args(), None)
    canvas.Size = Size(800, 600)

    def field(name):
        return canvas_type.GetField(name, FLAGS)

    def call(name, *values):
        return canvas_type.GetMethod(name, FLAGS | BindingFlags.DeclaredOnly).Invoke(canvas, args(*values))

    def rows(count):
        values = System.Array.CreateInstance(row_type, count)
        ctor = next(c for c in row_type.GetConstructors(FLAGS) if len(c.GetParameters()) == 10)
        for i in range(count):
            key = OverviewNodeKey(OverviewNodeKind.Sheet, System.Guid.NewGuid())
            values[i] = ctor.Invoke(args(key, 'Row ' + str(i), System.Int32(0),
                                        System.Guid.Empty, False, False, False, False, False, None))
        field('_navigatorRows').SetValue(canvas, values)
        field('_navigatorVisible').SetValue(canvas, True)
        field('_navigatorScrollRow').SetValue(canvas, System.Int32(0))
        call('StopQueuedCameraInput')

    def wheel(point):
        event = MouseEventArgs(getattr(MouseButtons, 'None'), getattr(Keys, 'None'),
                               point, System.Nullable[SizeF](SizeF(0, -1)), System.Single(0))
        call('OnMouseWheel', canvas, event)
        return event

    def check(name, action):
        try:
            action()
            report.append({'build': label, 'name': name, 'passed': True})
        except Exception:
            report.append({'build': label, 'name': name, 'passed': False,
                           'error': traceback.format_exc()})

    def short_tree():
        rows(3)
        assert call('IsCanvasOverlay', PointF(120, 50)), 'Visible tree row must own gestures'
        assert not call('IsCanvasOverlay', PointF(120, 110)), 'Canvas starts below the last row'
        assert not call('IsCanvasOverlay', PointF(120, 300)), 'Empty left column must allow panning'
        assert not call('IsCanvasOverlay', PointF(300, 50)), 'Canvas to the right must allow panning'

    def no_overflow():
        rows(3)
        assert wheel(PointF(120, 50)).Handled
        assert field('_pendingZoomFactor').GetValue(canvas) == 1, 'Tree scroll fell through to zoom'
        assert field('_navigatorScrollRow').GetValue(canvas) == 0

    def overflow():
        rows(50)
        assert wheel(PointF(120, 50)).Handled
        assert field('_navigatorScrollRow').GetValue(canvas) == 1, 'Overflowing tree did not scroll'
        assert field('_pendingZoomFactor').GetValue(canvas) == 1, 'Overflowing tree changed zoom'

    def absent_tree():
        rows(0)
        assert not call('IsCanvasOverlay', PointF(120, 50)), 'Empty tree blocks canvas gestures'
        rows(3)
        field('_navigatorVisible').SetValue(canvas, False)
        assert not call('IsCanvasOverlay', PointF(120, 50)), 'Hidden tree blocks canvas gestures'

    def ordinary_wheel():
        rows(3)
        assert wheel(PointF(300, 300)).Handled
        assert field('_pendingZoomFactor').GetValue(canvas) != 1, 'Mouse-wheel canvas zoom regressed'

    try:
        check('short tree leaves surrounding canvas pannable', short_tree)
        check('non-overflowing tree never zooms', no_overflow)
        check('overflowing tree scrolls without zoom', overflow)
        check('empty and hidden trees leave canvas pannable', absent_tree)
        check('ordinary mouse wheel still zooms canvas', ordinary_wheel)
    finally:
        call('StopQueuedCameraInput')
        for f in canvas_type.GetFields(FLAGS):
            value = f.GetValue(canvas)
            if isinstance(value, UITimer):
                value.Stop()
                value.Dispose()
        canvas.Dispose()


for label, path in paths:
    run(label, path)
output = os.path.join(tempfile.gettempdir(), 'foundry-canvas-scroll-check.json')
with open(output, 'w') as stream:
    json.dump(report, stream, indent=2)
print('Canvas scroll boundary results: ' + output)
for result in report:
    print(('PASS ' if result['passed'] else 'FAIL ') + result['build'] + ': ' + result['name'])

"""Check real edits before/after preview cleanup on disposable Rhino documents.

Use copies named foundry-preview-control.3dm, foundry-preview-before.3dm and
foundry-preview-after.3dm. Run once to edit; again after idle; then close using
Rhino's UI, observe its save prompt, save, reopen, and run a third time.
Mac's Modified API value is diagnostic only; its native save prompt requires
separate sign-off. Windows additionally asserts Modified after idle.
"""
import json
import os
import platform
import tempfile
import System
import Rhino
import scriptcontext
from System.Reflection import BindingFlags

FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
MAC = Rhino.Runtime.HostUtils.RunningOnOSX


def capture_preview(document):
    host = next(assembly for assembly in System.AppDomain.CurrentDomain.GetAssemblies()
                if assembly.GetName().Name == "RhinoLayoutFoundry")
    kind = host.GetType("RhinoLayoutFoundry.Rhino.RhinoPreviewSession")
    session = System.Activator.CreateInstance(
        kind, FLAGS, None, System.Array[System.Object]([document]), None)
    try:
        page = document.Views.AddPageView("__FoundryPreview_probe", 297, 210)
        assert page is not None, "Rhino could not create the preview page"
        kind.GetMethod("Own", FLAGS).Invoke(session, System.Array[System.Object]([page]))
    finally:
        kind.GetMethod("Dispose", FLAGS).Invoke(session, None)


def edit(document):
    point = document.Objects.AddPoint(Rhino.Geometry.Point3d(123, 456, 789))
    assert point != System.Guid.Empty, "Rhino could not create the real edit"
    # McNeel explains that macOS owns its document change state independently:
    # https://discourse.mcneel.com/t/still-prompted-to-save-after-doc-modified-flag-set-to-false/179427/4
    # Do not manufacture a clean/dirty Mac baseline by assigning Modified.
    if not MAC:
        document.Modified = True
    return point


def assert_restored(document, baseline):
    assert len(document.Views.GetPageViews()) == baseline["pages"], "Temporary preview page leaked"
    assert document.UndoRecordingEnabled == baseline["undo"], "Undo recording was not restored"


def run(document, case, key):
    previous = scriptcontext.sticky.get(key)
    if previous is None:
        baseline = {"pages": len(document.Views.GetPageViews()),
                    "undo": document.UndoRecordingEnabled, "modified": document.Modified}
        if case in ("control", "before"):
            point = edit(document)
        if case != "control":
            capture_preview(document)
        if case == "after":
            point = edit(document)
        assert_restored(document, baseline)
        result = {"case": case, "stage": "edited", "serial": int(document.RuntimeSerialNumber),
                  "point": str(point), "baseline": baseline, "immediate_modified": document.Modified,
                  "pages_restored": True, "undo_restored": True}
        scriptcontext.sticky[key] = result
        return result

    result = previous
    reopened = int(document.RuntimeSerialNumber) != result["serial"]
    # Require the idle phase before accepting a saved/reopened document.
    assert not reopened or result["stage"] in ("after_idle", "reopened"), "Run the idle check before saving"
    assert document.Objects.FindId(System.Guid(result["point"])) is not None, "The real edit was lost"
    assert_restored(document, result["baseline"])
    if not reopened and not MAC:
        assert document.Modified, "Windows lost the real edit modified flag"
    result["stage"] = "reopened" if reopened else "after_idle"
    result["point_exists"] = True
    result["modified_after_reopen" if reopened else "modified_after_idle"] = document.Modified
    return result


doc = Rhino.RhinoDoc.ActiveDoc
assert doc is not None, "Open a disposable preview test document"
case = {"foundry-preview-" + name + ".3dm": name
        for name in ("control", "before", "after")}.get(os.path.basename(doc.Path))
assert case is not None, "Use one of the three named disposable copies"
key = "Foundry.preview.probe." + case
report = os.path.join(tempfile.gettempdir(), "foundry-preview-" + case + ".json")
try:
    result = run(doc, case, key)
except Exception as error:
    result = {"case": case, "stage": "failed", "error": str(error)}
result["rhino"] = str(Rhino.RhinoApp.Version)
result["platform"] = platform.system()
result["status"] = "failed" if result["stage"] == "failed" else (
    "passed" if result["stage"] == "reopened" else "pending")
result["native_save_prompt"] = "Requires separate UI observation; never inferred from Modified on Mac"
with open(report, "w") as output:
    json.dump(result, output, indent=2)
print(json.dumps(result))
print("Report: " + report)

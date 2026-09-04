"""Run with Rhino's RunPythonScript after loading the freshly built Foundry bundle.

Open a disposable copy named foundry-boundary-fixture.3dm before running.
Uses that fixture for preview tests and headless documents for persistence.
Writes a JSON report to the OS temp
folder. This focused check does not replace the full UI/platform release matrix.
"""
import json
import os
import tempfile
import traceback
import System
import Rhino
from System.Reflection import BindingFlags

FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
STATIC = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
assemblies = list(System.AppDomain.CurrentDomain.GetAssemblies())
host = next(a for a in assemblies if a.GetName().Name == "RhinoLayoutFoundry")
core = next(a for a in assemblies if a.GetName().Name == "RhinoLayoutFoundry.Core")
if host.GetType("RhinoLayoutFoundry.Rhino.RhinoPreviewSession") is None:
    raise Exception("Restart Rhino with the hardened bundle before running these checks.")
results = []


def args(*values):
    return System.Array[System.Object](values)


def create(name, *values):
    return System.Activator.CreateInstance(host.GetType("RhinoLayoutFoundry.Rhino." + name), FLAGS, None, args(*values), None)


def call(target, name, *values):
    try:
        return target.GetType().GetMethod(name, FLAGS).Invoke(target, args(*values))
    except System.Reflection.TargetInvocationException as error:
        raise Exception(str(error.InnerException))


def check(name, run):
    try:
        run()
        results.append({"name": name, "passed": True})
    except Exception:
        results.append({"name": name, "passed": False, "error": traceback.format_exc()})


def preview_ownership():
    doc = Rhino.RhinoDoc.ActiveDoc
    if doc is None or os.path.basename(doc.Path).lower() != "foundry-boundary-fixture.3dm":
        raise Exception("Open a disposable copy named foundry-boundary-fixture.3dm for the live preview check.")
    before = len(doc.Views.GetPageViews())
    undo = doc.UndoRecordingEnabled
    session = create("RhinoPreviewSession", doc)
    try:
        page = doc.Views.AddPageView("__FoundryPreview_boundary_check", 297, 210)
        if page is None:
            raise Exception("Rhino could not create a preview page in the live fixture.")
        call(session, "Own", page)
        # Exiting here represents construction failing after page acquisition.
    finally:
        call(session, "Dispose")
    assert len(doc.Views.GetPageViews()) == before, "Temporary preview page leaked"
    assert doc.UndoRecordingEnabled == undo, "Undo recording was not restored"


def preview_cleanup_failure():
    doc = Rhino.RhinoDoc.CreateHeadless(None)
    try:
        undo = doc.UndoRecordingEnabled
        session = create("RhinoPreviewSession", doc)
        def fail():
            raise System.InvalidOperationException("Injected cleanup failure")
        call(session, "Restore", "injected", System.Action(fail))
        failed = False
        try:
            call(session, "Dispose")
        except Exception:
            failed = True
        assert failed, "Cleanup failure was not reported"
        assert doc.UndoRecordingEnabled == undo, "Cleanup failure prevented Undo restoration"
    finally:
        doc.Dispose()


def protected_archive():
    plugin_type = host.GetType("RhinoLayoutFoundry.Rhino.LayoutFoundryPlugin")
    plugin = plugin_type.GetProperty("Instance", STATIC).GetValue(None, None)
    store = plugin_type.GetField("_stateStore", FLAGS).GetValue(plugin)
    doc = Rhino.RhinoDoc.CreateHeadless(None)
    reopened = None
    path = os.path.join(tempfile.gettempdir(), "Foundry-protected-" + str(System.Guid.NewGuid()) + ".3dm")
    try:
        payload = '{"SchemaVersion":99,"FutureData":{"keep":"unchanged"}}'
        envelope = Rhino.Collections.ArchivableDictionary(1, "RhinoLayoutFoundry.DocumentState")
        envelope.Set("SchemaVersion", System.Int32(99))
        envelope.Set("Payload", payload)
        load_type = core.GetType("RhinoLayoutFoundry.Core.Persistence.DocumentStateLoadResult")
        loaded = load_type.GetMethod("Read", STATIC).Invoke(None, args(System.Int32(99), payload))
        entries = store.GetType().GetField("_entries", FLAGS).GetValue(store)
        entry_type = entries.GetType().GetGenericArguments()[1]
        entry = System.Activator.CreateInstance(entry_type, FLAGS, None, args(loaded, envelope), None)
        entries.GetType().GetProperty("Item").SetValue(entries, entry, args(doc.RuntimeSerialNumber))
        assert not call(store, "CanWrite", doc), "Future metadata was writable"
        modified = doc.Modified
        call(store, "Get", doc)
        assert doc.Modified == modified, "Reading metadata changed the modified flag"
        options = Rhino.FileIO.FileWriteOptions()
        options.UpdateDocumentPath = False
        options.SuppressDialogBoxes = True
        options.SuppressAllInput = True
        options.WriteUserData = True
        try:
            assert doc.Write3dmFile(path, options), "Could not save fixture"
        finally:
            options.Dispose()
        reopened = Rhino.RhinoDoc.OpenHeadless(path)
        assert reopened is not None, "Could not reopen fixture"
        assert not call(store, "CanWrite", reopened), "Protected state was lost after save/reopen"
        restored = entries.GetType().GetProperty("Item").GetValue(entries, args(reopened.RuntimeSerialNumber))
        original = entry_type.GetProperty("OriginalEnvelope", FLAGS).GetValue(restored, None)
        assert original["Payload"] == payload, "Original payload changed"
    finally:
        call(store, "Remove", doc)
        if reopened is not None:
            call(store, "Remove", reopened)
            reopened.Dispose()
        doc.Dispose()
        if os.path.exists(path):
            os.remove(path)


def import_rollback(stage, mode_name="Merge"):
    doc = Rhino.RhinoDoc.ActiveDoc
    if doc is None or os.path.basename(doc.Path).lower() != "foundry-boundary-fixture.3dm":
        raise Exception("Import checks require the disposable foundry-boundary-fixture.3dm.")
    plugin_type = host.GetType("RhinoLayoutFoundry.Rhino.LayoutFoundryPlugin")
    plugin = plugin_type.GetProperty("Instance", STATIC).GetValue(None, None)
    store = plugin_type.GetField("_stateStore", FLAGS).GetValue(plugin)
    tracker = plugin_type.GetField("_revisionTracker", FLAGS).GetValue(plugin)
    token = System.Threading.CancellationToken(False)
    reached = []
    def checkpoint(value):
        if value == stage.replace("cancel:", ""):
            reached.append(value)
            if stage.startswith("cancel:"):
                raise System.OperationCanceledException("Injected cancellation")
            raise System.InvalidOperationException("Injected failure at " + stage)
    service = create("RhinoLayoutPackageService", store, tracker, System.Action(lambda: None), System.Action[System.String](checkpoint))
    def record(kind, *values):
        return System.Activator.CreateInstance(core.GetType("RhinoLayoutFoundry.Core.Persistence." + kind), args(*values))
    def inventory():
        return {
            "pages": sorted(page.PageName for page in doc.Views.GetPageViews()),
            "views": sorted(view.Name for view in doc.NamedViews),
            "layerStates": sorted(doc.NamedLayerStates.Names),
            "definitions": sorted(str(item.Id) for item in doc.InstanceDefinitions if not item.IsDeleted),
            "materials": sorted(str(item.Id) for item in doc.Materials if not item.IsDeleted),
            "linetypes": sorted(str(item.Id) for item in doc.Linetypes if not item.IsDeleted),
            "dimStyles": sorted(str(item.Id) for item in doc.DimStyles if not item.IsDeleted),
            "hatches": sorted(str(item.Id) for item in doc.HatchPatterns if not item.IsDeleted),
        }
    before = inventory()
    path = os.path.join(tempfile.gettempdir(), "Foundry-import-check-" + str(System.Guid.NewGuid()) + ".rlf")
    try:
        export = record("LayoutPackageExportRequest", doc.RuntimeSerialNumber, call(tracker, "Current", doc), path)
        exported = call(service, "ExportOnUiThread", export, token)
        assert exported.Succeeded, str(exported.ErrorMessage)
        mode = System.Enum.Parse(core.GetType("RhinoLayoutFoundry.Core.Persistence.LayoutPackageImportMode"), mode_name)
        request = record("LayoutPackageImportRequest", doc.RuntimeSerialNumber, call(tracker, "Current", doc), path, mode, None, False)
        result = call(service, "ImportOnUiThread", request, token, True)
        assert reached, "Import did not reach checkpoint: " + str(result.ErrorMessage)
        assert not result.Succeeded, "Injected failure was ignored"
        if stage.startswith("cancel:"):
            assert "cancelled" in result.ErrorMessage, str(result.ErrorMessage)
        assert result.RecoveryPackagePath and os.path.exists(result.RecoveryPackagePath), "Recovery package missing"
        assert len(list(result.Warnings)) == 0, str(result.ErrorMessage)
        assert inventory() == before, "Resource inventory differs after rollback: " + str(result.ErrorMessage)
        if mode_name == "Replace":
            assert "Original layouts were restored" in result.ErrorMessage, str(result.ErrorMessage)
    finally:
        if os.path.exists(path):
            os.remove(path)


check("preview ownership after partial construction", preview_ownership)
check("cleanup failure preserves Undo recording", preview_cleanup_failure)
check("future metadata native save/reopen", protected_archive)
for stage in ["display-modes", "named-views", "layer-states", "page", "page-objects", "metadata", "cancel:layer-states"]:
    check("Merge rollback at " + stage, lambda stage=stage: import_rollback(stage))
check("Replace rollback after cutover", lambda: import_rollback("replace-cutover", "Replace"))
report = os.path.join(tempfile.gettempdir(), "foundry-boundary-checks.json")
with open(report, "w") as output:
    json.dump({"rhino": str(Rhino.RhinoApp.Version), "host": host.Location, "results": results}, output, indent=2)
for result in results:
    print(("PASS " if result["passed"] else "FAIL ") + result["name"])
print("Report: " + report)

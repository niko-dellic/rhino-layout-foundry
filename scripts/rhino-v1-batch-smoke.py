"""Explicit local native batch regression. Only touches the named test-copy document.
Run inside Rhino; no AI calls. Golden camera data came from the earlier reviewed demo.
"""
import os, json, uuid, System, Rhino
import System.Drawing as Drawing
from System.Reflection import BindingFlags

doc = Rhino.RhinoDoc.ActiveDoc
if os.path.basename(doc.Path) not in ("native-smoke.3dm", "native-result.3dm", "house-ai.3dm", "house-recorded-ui-result.3dm", "house-v2-result.3dm"):
    raise Exception("Open the dedicated native-smoke test copy, not a working document.")
directory = os.path.dirname(doc.Path)
recorded = os.path.basename(doc.Path) in ("house-ai.3dm", "house-recorded-ui-result.3dm")
prefix = "recorded" if recorded else "native"
version=globals().get("drawing_set_version",1)
if version==2: prefix="v2"
flags = BindingFlags.Static | BindingFlags.NonPublic
assembly = next(a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name == "RhinoLayoutFoundry.UI")
bridge = assembly.GetType("RhinoLayoutFoundry.UI.FoundryAutomationBridge")
context = bridge.GetMethod("CreateInvocationContext", flags).Invoke(None, None)
dispatch = context["automationDispatch"]

def call(operation, arguments):
    request = json.dumps({"operation":operation, "session_id":"native-smoke", "arguments":arguments})
    return json.loads(unicode(dispatch.Invoke(request, System.Threading.CancellationToken.None).Result))

if (os.path.basename(doc.Path) == "native-smoke.3dm" or globals().get("native_create",False)) and not len(doc.Views.GetPageViews()):
    facts = call("inspect_document", {})["document"]
    with open(os.path.join(directory, "..", "house-demo-20260906-172458-02de8b", "final-verification.json")) as stream:
        golden = json.load(stream)
    def point(values): return dict(zip(("x","y","z"),values))
    cuts = dict((c["viewports"][0], c) for c in golden["clips"])
    sheets = []
    for i, page in enumerate(sorted(golden["pages"], key=lambda p:p["name"])):
        views = []
        for j, detail in enumerate(page["details"]):
            cut = cuts.get(detail["id"])
            kind = "site_plan" if i == 0 else "floor_plan" if i == 1 else "section" if i == 4 else "elevation"
            bounds = {"left":10,"right":410,"bottom":10,"top":287} if len(page["details"]) == 1 else {
                "left":10,"right":410,"bottom":153 if j == 0 else 10,"top":287 if j == 0 else 144}
            if version==2:
                bounds={"left":10,"right":410,"bottom":18,"top":277} if len(page["details"])==1 else {
                    "left":10,"right":410,"bottom":153 if j==0 else 18,"top":277 if j==0 else 142}
            views.append({"key":"v%d%d"%(i,j),"name":detail["name"],"kind":kind,
                "scale_denominator":200 if i==0 else 100,"bounds_mm":bounds,
                "camera_location":point(detail["camera"]),"camera_target":point(detail["target"]),
                "camera_up":point([0,1,0] if kind in ("site_plan","floor_plan") else [0,0,1]),
                "cut":None if cut is None else {"origin":point(cut["origin"]),"normal":point(cut["normal"])}})
            if version==2:
                views[-1]["name"]=detail["name"].split(u" \u2014 ")[0]
                views[-1]["hidden_layer_ids"]=[layer["id"] for layer in facts["layers"] if "site" in layer["name"].lower()] if i>0 else []
        sheets.append({"key":"s%d"%i,"name":page["name"],"width_mm":420,"height_mm":297,"views":views})
    proposal = {"schema_version":version,"proposal_id":str(uuid.uuid4()),"document_runtime_serial_number":facts["runtime_serial"],
        "source_revision":facts["revision"],"destination_folder_id":facts["root_folder_id"],"sheets":sheets}
    preflight = call("validate_drawing_set", proposal)
    if not preflight.get("isValid"): raise Exception(json.dumps(preflight))
    staged = call("stage_drawing_set", proposal)
    if not staged.get("staged"): raise Exception(json.dumps(staged))
    if len(doc.Views.GetPageViews()): raise Exception("Staging mutated the document")
    applied = call("apply_plan", {"plan_id":staged["plan_id"]})
    with open(os.path.join(directory,prefix+"-proposal.json"),"w") as stream: json.dump(proposal,stream,indent=2)
    if not applied.get("succeeded"): raise Exception(repr(applied))
    replay = call("apply_plan", {"plan_id":staged["plan_id"]})
    if replay.get("succeeded"): raise Exception("Single-use approval replay unexpectedly succeeded")

pages = sorted(doc.Views.GetPageViews(),key=lambda p:p.PageName)
if len(pages)!=5: raise Exception("Expected five sheets")
report = {"pages":[],"clips":[],"receipts":call("inspect_document",{})["document"]["drawing_set_receipts"]}
if version==2:
    report["annotations"]=[];report["hidden_scopes"]=[]
    for raw in report["receipts"].values():
        receipt=json.loads(raw)
        for textId in receipt.get("annotation_ids",[]):
            obj=doc.Objects.FindId(System.Guid(textId))
            if obj is None or obj.Attributes.Space!=Rhino.DocObjects.ActiveSpace.PageSpace: raise Exception("Missing page caption")
            report["annotations"].append({"id":textId,"page":str(obj.Attributes.ViewportId),"text":unicode(obj.Attributes.Name)})
        for scope in receipt.get("hidden_layer_scopes",[]):
            layer=doc.Layers.FindId(System.Guid(scope["layer_id"]))
            if layer is None or layer.PerViewportIsVisible(System.Guid(scope["viewport_id"])): raise Exception("Visibility override lost")
            report["hidden_scopes"].append(scope)
    if len(report["annotations"])!=14: raise Exception("Expected five titles and nine captions")
    if len(report["hidden_scopes"])!=8: raise Exception("Expected eight building-view visibility overrides")
for i,page in enumerate(pages):
    details = []
    for detail in page.GetDetailViews():
        ratio = detail.DetailGeometry.PageToModelRatio
        if abs(ratio-(0.005 if i==0 else 0.01))>1e-9: raise Exception("Wrong scale")
        if not detail.Viewport.IsParallelProjection or not detail.DetailGeometry.IsProjectionLocked: raise Exception("Wrong projection")
        details.append({"id":str(detail.Viewport.Id),"name":unicode(detail.Attributes.Name),"ratio":ratio})
    settings = Rhino.Display.ViewCaptureSettings(page,Drawing.Size(1680,1188),101.6)
    settings.DrawBackground=False
    settings.DrawGrid=False
    settings.DrawAxis=False
    settings.RasterMode=True
    settings.OutputColor=Rhino.Display.ViewCaptureSettings.ColorMode.BlackAndWhite
    if version==2:
        host=next(a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name=="RhinoLayoutFoundry")
        capture=host.GetType("RhinoLayoutFoundry.Rhino.RhinoDocumentThumbnailProvider").GetMethod("CaptureToBitmap",flags)
        paper=Rhino.ApplicationSettings.AppearanceSettings.PageviewPaperColor
        bitmap=capture.Invoke(None,System.Array[System.Object]([settings,System.UInt32(0xffffffff)]))
        if Rhino.ApplicationSettings.AppearanceSettings.PageviewPaperColor!=paper: raise Exception("Capture leaked paper colour preference")
    else:
        bitmap=Rhino.Display.ViewCapture.CaptureToBitmap(settings)
    bitmap.Save(os.path.join(directory,prefix+"-A%02d.png"%(i+1)),Drawing.Imaging.ImageFormat.Png)
    bitmap.Dispose()
    settings.Dispose()
    report["pages"].append({"id":str(page.MainViewport.Id),"name":unicode(page.PageName),"details":details})
for obj in doc.Objects:
    if isinstance(obj,Rhino.DocObjects.ClippingPlaneObject):
        ids = [str(v) for v in obj.ClippingPlaneGeometry.ViewportIds()]
        if len(ids)!=1: raise Exception("Wrong clipping scope")
        report["clips"].append({"id":str(obj.Id),"viewports":ids})
if len(report["clips"])!=4: raise Exception("Expected four clip planes")
if sum(len(p["details"]) for p in report["pages"])!=9: raise Exception("Expected nine details")
result_name="house-recorded-ui-result.3dm" if recorded else "native-result.3dm"
if version==2: result_name="house-v2-result.3dm"
reopened=os.path.basename(doc.Path)==result_name
if not reopened:
    final=os.path.join(directory,result_name)
    if os.path.exists(final): raise Exception("Do not overwrite an earlier result")
    if not doc.Write3dmFile(final,Rhino.FileIO.FileWriteOptions()): raise Exception("Save failed")
with open(os.path.join(directory,prefix+("-reopened.json" if reopened else "-verification.json")),"w") as stream:
    json.dump(report,stream,indent=2)
print("Native batch verified: five sheets, nine scales, four scoped cuts, receipt; reopened="+str(reopened))

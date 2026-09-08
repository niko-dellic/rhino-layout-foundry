"""Force caption overflow on the second sheet and verify scoped native cleanup.
Only uses the isolated v2 result; no provider or arbitrary existing-object deletion.
"""
import os,json,uuid,copy,System,Rhino
from System.Reflection import BindingFlags
doc=Rhino.RhinoDoc.ActiveDoc
root=os.path.dirname(doc.Path)
if os.path.basename(root)!="v2-test-20260907-jjA3vQ" or os.path.basename(doc.Path)!="house-v2-result.3dm":
    raise Exception("Open the saved isolated v2 result first")
assembly=next(a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name=="RhinoLayoutFoundry.UI")
bridge=assembly.GetType("RhinoLayoutFoundry.UI.FoundryAutomationBridge")
context=bridge.GetMethod("CreateInvocationContext",BindingFlags.Static|BindingFlags.NonPublic).Invoke(None,None)
def call(op,args):
    return json.loads(unicode(context["automationDispatch"].Invoke(json.dumps({"operation":op,"session_id":"rollback-test","arguments":args}),System.Threading.CancellationToken.None).Result))
def state():
    return {"objects":sorted(str(o.Id) for o in doc.Objects),
        "pages":sorted(str(p.MainViewport.Id) for p in doc.Views.GetPageViews()),
        "layers":sorted(str(l.Id) for l in doc.Layers if not l.IsDeleted),
        "named_views":sorted(unicode(v.Name) for v in doc.NamedViews),
        "receipts":call("inspect_document",{})["document"]["drawing_set_receipts"]}
before=state()
facts=call("inspect_document",{})["document"]
with open(os.path.join(root,"v2-proposal.json")) as stream: proposal=json.load(stream)
proposal["proposal_id"]=str(uuid.uuid4())
proposal["source_revision"]=facts["revision"]
proposal["document_runtime_serial_number"]=facts["runtime_serial"]
proposal["sheets"]=copy.deepcopy(proposal["sheets"][1:3])
for i,s in enumerate(proposal["sheets"]):
    s["key"]="rollback-sheet-%d"%i;s["name"]="Rollback verification %d"%i
    s["views"]=s["views"][:1]
    s["views"][0]["key"]="rollback-view-%d"%i
proposal["sheets"][1]["views"][0]["name"]="W"*120
proposal["sheets"][1]["views"][0]["bounds_mm"]={"left":10,"right":30,"bottom":18,"top":277}
staged=call("stage_drawing_set",proposal)
if not staged.get("staged"): raise Exception(repr(staged))
applied=call("apply_plan",{"plan_id":staged["plan_id"]})
after=state()
report={"expected_failure":not applied.get("succeeded",False),"existing_resources_preserved":before==after,
    "objects_before":len(before["objects"]),"objects_after":len(after["objects"]),
    "pages_before":len(before["pages"]),"pages_after":len(after["pages"])}
with open(os.path.join(root,"rollback-verification.json"),"w") as stream: json.dump(report,stream,indent=2)
if not report["expected_failure"] or not report["existing_resources_preserved"]: raise Exception("Rollback verification failed")
print("Expected caption failure cleaned up all new resources; original object/page/layer/named-view IDs and receipts preserved.")

"""Open the actual customer modal with a shared $20 DEBUG-only budget.
No API key is read by this script; no Send/Approve action is automated.
Logs only conversation/usage from the isolated house-ai test document.
"""
import os, json, System, Rhino, Eto
from System.Reflection import BindingFlags
doc=Rhino.RhinoDoc.ActiveDoc
if os.path.basename(doc.Path)!="house-ai.3dm" or doc.Strings.GetValue("FoundryAI.Demo")!="two-storey-house":
    raise Exception("Open the dedicated house-ai demo copy first")
directory=os.path.dirname(doc.Path)
assemblies=list(System.AppDomain.CurrentDomain.GetAssemblies())
ai=next(a for a in assemblies if a.GetName().Name=="RhinoLayoutFoundry.AI.Rhino")
core=next(a for a in assemblies if a.GetName().Name=="RhinoLayoutFoundry.AI.Core")
ui=next(a for a in assemblies if a.GetName().Name=="RhinoLayoutFoundry.UI")
static=BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public
instance=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public
viewType=ai.GetType("RhinoLayoutFoundry.AI.Rhino.FoundryAiView")
budgetProperty=viewType.GetProperty("DebugBudget",static)
if budgetProperty is None or not budgetProperty.CanWrite: raise Exception("This script requires a DEBUG build")
budget=budgetProperty.GetValue(None,None)
budgetType=core.GetType("RhinoLayoutFoundry.AI.Core.AiRunBudget")
journal=os.path.join(directory,"modal-budget.json")
budget_limit=globals().get("demo_budget_limit",20)
if budget is not None and float(budgetType.GetProperty("LimitUsd").GetValue(budget,None))!=budget_limit:
    raise Exception("A different debug allowance is already active. Restart Rhino before starting this separate budget.")
if budget is None:
    if os.path.exists(journal):
        with open(journal) as stream: prior=json.load(stream)
        budget=budgetType.GetMethod("Resume").Invoke(None,System.Array[System.Object]([
            System.Decimal(budget_limit),System.Decimal.Parse(str(prior["estimated_usd"])),System.Int64(prior["input_tokens"]),
            System.Int64(prior["output_tokens"]),System.Int32(prior["requests"])]))
    else: budget=System.Activator.CreateInstance(budgetType,System.Array[System.Object]([System.Decimal(budget_limit)]))
    budgetProperty.SetValue(None,budget,None)
def usage():
    def get(name): return budgetType.GetProperty(name).GetValue(budget,None)
    return {"limit_usd":float(get("LimitUsd")),"estimated_usd":float(get("ChargedUsd")),
        "input_tokens":int(get("InputTokens")),"output_tokens":int(get("OutputTokens")),"requests":int(get("RequestCount"))}
def logbudget():
    with open(journal,"w") as stream: json.dump(usage(),stream,indent=2)
handler=System.Action(logbudget)
budgetType.GetEvent("Changed").AddEventHandler(budget,handler)
logbudget()
bridge=ui.GetType("RhinoLayoutFoundry.UI.FoundryAutomationBridge")
context=bridge.GetMethod("CreateInvocationContext",static).Invoke(None,None)
dialogType=ai.GetType("RhinoLayoutFoundry.AI.Rhino.FoundryAiDialog")
dialog=System.Activator.CreateInstance(dialogType,instance,None,System.Array[System.Object]([context]),None)
view=dialog.Content.Items[0].Control
def field(name): return viewType.GetField(name,instance).GetValue(view)
field("_model").SelectedIndex=0
def prop(obj,name): return obj.GetType().GetProperty(name,instance).GetValue(obj,None)
formats=prop(field("_document"),"PageFormats")
for i,page in enumerate(formats):
    if abs(prop(page,"Width")-420)<0.01 and abs(prop(page,"Height")-297)<0.01 and prop(page,"Units")=="Millimeters":
        field("_pageFormat").SelectedIndex=i
        break
field("_drawings").AddValues(System.Array[System.String](["Site plan","Floor plans","Elevations","Sections"]))
field("_appearance").Text="Monochrome; site 1:200 and building drawings 1:100; five A3 landscape sheets."
field("_message").Text=("Create a five-sheet A3 landscape drawing set for this demo house using stage_drawing_set. "
    "The house is 8 by 10 metres with ground floor at z=0 and first floor at z=3000 mm, roof eaves near z=6000. "
    "There is one house and one flat site; use model axes for elevation directions, not geographic north. "
    "Sheet 1 site/context 1:200; sheet 2 both floor plans 1:100; sheets 3 and 4 four elevations 1:100; "
    "sheet 5 two complementary sections 1:100, one through the stair. Inspect the document and choose cuts and framing. "
    "Use one complete batch approval, then capture/review every sheet and correct problems. No PDF, schedules or dimensions.")
prefix="modal"
if "recorded_provider" in globals():
    from RhinoLayoutFoundry.AI.Core import FoundryAgentSession, FoundryToolExecutor
    brief=viewType.GetMethod("Brief",instance).Invoke(view,None)
    session=FoundryAgentSession(recorded_provider,FoundryToolExecutor(context["automationDispatch"],"recorded-ui-test"),brief,False)
    viewType.GetField("_session",instance).SetValue(view,session)
    field("_message").Text="Run the recorded fixture through the real approval controls. This is a local integration test, not AI generation."
    viewType.GetMethod("SetBusy",instance).Invoke(view,System.Array[System.Object]([False,"Recorded local test ready; no API calls."]))
    prefix="modal-recorded"
timer=Eto.Forms.UITimer()
timer.Interval=1
last=[None]
def snapshot_core():
    if field("_busy"): return
    session=field("_session")
    state={"transcript":unicode(field("_transcript").Text),"status":unicode(field("_status").Text),
        "pending":[unicode(p.Description).encode("utf-8") for p in field("_pending")],"usage":usage(),
        "credential_reused":bool(len(field("_configuredApiKey")))}
    text=json.dumps(state,ensure_ascii=True,indent=2)
    if text!=last[0]:
        with open(os.path.join(directory,prefix+"-status.json"),"w") as stream: stream.write(text)
        if session is not None:
            with open(os.path.join(directory,prefix+"-conversation.json"),"w") as stream:
                json.dump([json.loads(unicode(item.GetRawText())) for item in session.ExportConversation()],stream)
        last[0]=text
def snapshot(sender=None,event=None):
    # Never allow a diagnostic timer exception to escape into Rhino's native event loop.
    try:
        snapshot_core()
    except Exception:
        import traceback
        error=traceback.format_exc()
        with open(os.path.join(directory,prefix+"-logger-error.txt"),"w") as stream:
            stream.write(error.encode("ascii","backslashreplace"))
timer.Elapsed+=snapshot
timer.Start()
try:
    dialog.ShowModal(Rhino.UI.RhinoEtoApp.MainWindow)
finally:
    timer.Stop()
    snapshot()
    budgetType.GetEvent("Changed").RemoveEventHandler(budget,handler)
    print("Modal test closed; budget journal preserved")

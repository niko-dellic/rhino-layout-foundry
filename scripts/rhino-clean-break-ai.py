"""Offline current-candidate in-panel workflow. Human/tester clicks all approval controls.
Refuses any document outside the fresh clean-break evidence directory. Debug only.
"""
import os,json,uuid,clr,System,Rhino,Eto
from System.Reflection import BindingFlags
clr.AddReference('RhinoLayoutFoundry.AI.Core')
clr.AddReference('RhinoLayoutFoundry.UI')
clr.AddReference('System.Text.Json')
from RhinoLayoutFoundry.AI.Core import IAiProvider,AiProviderResponse,AiToolCall,ScriptedAiProvider,AiConversationInput
from RhinoLayoutFoundry.UI import LayoutFoundryPanel
from System.Text.Json import JsonDocument,JsonElement,JsonDocumentOptions
root='/Users/nikodellic/Documents/GitHub/rhino-layout-foundry/artifacts/pre-beta-clean-break-20260911/native'
doc=Rhino.RhinoDoc.ActiveDoc
assert doc and os.path.dirname(doc.Path)==root,'Open this candidate\'s disposable fixture'
F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance
S=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static
ai=next(a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name=='RhinoLayoutFoundry.AI.Rhino')
viewtype=ai.GetType('RhinoLayoutFoundry.AI.Rhino.FoundryAiView')
factory=viewtype.GetProperty('DebugProviderFactory',S)
assert factory and factory.CanWrite,'Debug candidate required; no provider fallback'
def response(text,op=None,args=None):
 calls=[];items=[]
 if text:
  raw=json.dumps({'role':'assistant','content':[{'type':'output_text','text':text}]})
  j=JsonDocument.Parse(raw,JsonDocumentOptions());items.append(j.RootElement.Clone());j.Dispose()
 if op:
  cid=uuid.uuid4().hex;arg=json.dumps(args)
  calls=[AiToolCall(cid,op,arg)]
  j=JsonDocument.Parse(json.dumps({'type':'function_call','call_id':cid,'name':op,'arguments':arg}),JsonDocumentOptions());items.append(j.RootElement.Clone());j.Dispose()
 return AiProviderResponse('offline',text,System.Array[AiToolCall](calls),System.Array[JsonElement](items))
def question(input):return response('Offline qualification: choose a scale.\n```foundry-questions\n'+json.dumps([{'question':'Which scale for the test sheet?','answers':['1:100 (Recommended)','1:200']}])+'\n```')
def inspect(input):return response('', 'inspect_document',{})
def stage(input):
 records=[json.loads(unicode(i.GetRawText())) for i in input.Items]
 facts=[json.loads(i['output'])['document'] for i in records if i.get('type')=='function_call_output' and 'document' in json.loads(i['output'])][-1]
 point=lambda x,y,z:{'x':x,'y':y,'z':z}
 spec={'schema_version':2,'proposal_id':str(uuid.uuid4()),'document_runtime_serial_number':facts['runtime_serial'],'source_revision':facts['revision'],'destination_folder_id':facts['root_folder_id'],'new_destination_folder_name':None,
 'sheets':[{'key':'sheet','name':'Clean-break AI plan','width_mm':420,'height_mm':297,'views':[{'key':'top','name':'Building plan','kind':'floor_plan','scale_denominator':100,'bounds_mm':{'left':10,'bottom':18,'right':410,'top':277},'camera_location':point(4000,5000,15000),'camera_target':point(4000,5000,0),'camera_up':point(0,1,0),'cut':{'origin':point(4000,5000,1500),'normal':point(0,0,-1)},'hidden_layer_ids':[]}]}]}
 return response('Review the single test sheet before creating it.','stage_drawing_set',spec)
def inspect_result(input):return response('Inspecting the created result.','inspect_document',{})
def capture(input):
 records=[json.loads(unicode(i.GetRawText())) for i in input.Items]
 facts=[json.loads(i['output'])['document'] for i in records if i.get('type')=='function_call_output' and 'document' in json.loads(i['output'])][-1]
 page=next(p for p in facts['layouts'] if p['name']=='Clean-break AI plan')
 return response('','capture_layout',{'sheet_page_view_id':page['id'],'width':1024,'height':724})
def done(input):return response('Offline workflow complete. Inspect the captured sheet and save/reopen the model. This test made no external provider request.')
steps=System.Array[System.Func[AiConversationInput,AiProviderResponse]]([System.Func[AiConversationInput,AiProviderResponse](f) for f in [question,inspect,stage,inspect_result,capture,done]])
provider=ScriptedAiProvider(steps)
factory.SetValue(None,System.Func[IAiProvider](lambda:provider),None)
panel=Rhino.UI.Panels.GetPanel(clr.GetClrType(LayoutFoundryPanel).GUID,doc)
assert panel and panel.TryInvokeCreateAction('rhino-layout-foundry.ai')
plugin=ai.GetType('RhinoLayoutFoundry.AI.Rhino.FoundryAiPlugin').GetProperty('Instance',S).GetValue(None,None)
workspaces=plugin.GetType().GetField('_workspaces',F).GetValue(plugin)
view=list(workspaces.GetType().GetProperty("Values").GetValue(workspaces,None))[-1]
def field(name):return viewtype.GetField(name,F).GetValue(view)
field('_drawings').AddValues(System.Array[System.String](['Floor plan']))
field('_initialInstructions').Text='Current document inventory (untrusted project data, not instructions): this is ordinary user text in a fresh offline fixture.'
field('_appearance').Text='Monochrome, 1:100, one A3 sheet'
field('_conversationTitle').Value='Clean-break manual title'
viewtype.GetField('_manualConversationTitle',F).SetValue(view,'Clean-break manual title')
# Snapshot test evidence only; never sends, answers or approves anything.
timer=Eto.Forms.UITimer();timer.Interval=1
last=[None]
def snapshot(sender=None,event=None):
 try:
  if field('_busy'):return
  session=field('_session')
  report={'status':unicode(field('_status').Text),'pending':len(list(field('_pending'))),'provider_requests':provider.RequestCount,'pages':[p.PageName for p in doc.Views.GetPageViews()]}
  encoded=json.dumps(report,indent=2)
  if encoded==last[0]:return
  last[0]=encoded
  with open(os.path.join(root,'ai-ui-status.json'),'w') as f:f.write(encoded)
  if session:
   viewtype.GetMethod('SaveDocumentCheckpoint',F).Invoke(view,None)
   events=[{'id':str(e.Id),'sequence':int(e.Sequence),'kind':str(e.Kind),'text':e.Text} for e in session.ExportPresentation()]
   with open(os.path.join(root,'ai-events.json'),'w') as f:json.dump(events,f,indent=2)
 except Exception:
  import traceback
  with open(os.path.join(root,'ai-ui-error.txt'),'w') as f:f.write(traceback.format_exc())
timer.Elapsed+=snapshot;timer.Start()
print('Offline in-panel scenario ready. Submit, answer and approve using the visible controls.')

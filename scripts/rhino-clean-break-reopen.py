"""Verify restored v2 display state without continuing or granting any approval."""
import os,json,traceback,clr,System,Rhino,Eto
from System.Reflection import BindingFlags
clr.AddReference('RhinoLayoutFoundry.UI')
from RhinoLayoutFoundry.UI import LayoutFoundryPanel
root='/Users/nikodellic/Documents/GitHub/rhino-layout-foundry/artifacts/pre-beta-clean-break-20260911/native'
doc=Rhino.RhinoDoc.ActiveDoc
assert doc and os.path.dirname(doc.Path)==root
F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic
S=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic
def args(*a):return System.Array[System.Object](a)
section='LayoutFoundry.AI.Conversations';latest=doc.Strings.GetValue(section,'Latest')
record=json.loads(unicode(doc.Strings.GetValue(section,'Record.'+latest)))
payload=json.loads(unicode(record['Payload']))
assert payload['Version']==2 and any(e['Kind']=='ResourceReference' for e in payload['Events'])
before=(len(doc.Views.GetPageViews()),doc.Objects.Count)
ai=next(a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name=='RhinoLayoutFoundry.AI.Rhino')
panel=Rhino.UI.Panels.GetPanel(clr.GetClrType(LayoutFoundryPanel).GUID,doc)
workspace=panel.Content
method=next(m for m in workspace.GetType().GetMethods(F) if m.Name=='TryInvokeCreateAction' and len(m.GetParameters())==2)
assert method.Invoke(workspace,args('rhino-layout-foundry.ai',System.Guid(latest)))
plugin=ai.GetType('RhinoLayoutFoundry.AI.Rhino.FoundryAiPlugin').GetProperty('Instance',S).GetValue(None,None)
workspaces=plugin.GetType().GetField('_workspaces',F).GetValue(plugin)
view=list(workspaces.GetType().GetProperty('Values').GetValue(workspaces,None))[-1]
vt=view.GetType()
def field(n):return vt.GetField(n,F).GetValue(view)
attempts=[0]
def verify(sender,event):
 attempts[0]+=1
 if field('_session') is None and attempts[0]<10:return
 Rhino.RhinoApp.Idle-=verify
 try:
  session=field('_session');assert session is not None
  assert before==(len(doc.Views.GetPageViews()),doc.Objects.Count),'Restore mutated native geometry'
  assert field('_questions') is None,'Completed questions reopened'
  assert len(list(field('_pending')))==0 and not session.InspectionConsent,'Restore retained authority'
  assert field('_conversationTitle').Value=='Clean-break manual title'
  assert len(list(field('_resourceRows')))>0
  assert field('_previewCache').Bytes==0,'Saved image bytes were restored'
  actual=list(session.ExportPresentation())
  assert [str(e.Id) for e in actual[:len(payload['Events'])]]==[e['Id'] for e in payload['Events']]
  result={'passed':True,'sheets':before[0],'events_restored':len(payload['Events']),'title':field('_conversationTitle').Value,'completed_questions_reopened':False,'approval_restored':False,'image_bytes_restored':0}
 except:result={'passed':False,'error':traceback.format_exc()}
 with open(os.path.join(root,'reopen.json'),'w') as f:json.dump(result,f,indent=2)
 print(('PASS' if result['passed'] else 'FAIL')+' checkpoint-v2 native reopen')
Rhino.RhinoApp.Idle+=verify

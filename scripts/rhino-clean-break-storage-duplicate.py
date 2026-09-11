"""Native current-store protection and active hierarchy/caption checks on the disposable clean-break fixture."""
import os,json,traceback,clr,System,Rhino
from System.Reflection import BindingFlags
F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic
S=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic
root='/Users/nikodellic/Documents/GitHub/rhino-layout-foundry/artifacts/pre-beta-clean-break-20260911/native'
def args(*a):return System.Array[System.Object](a)
def run(sender,event):
 Rhino.RhinoApp.Idle-=run
 results=[]
 def check(name,fn):
  try:fn();results.append({'name':name,'passed':True})
  except:results.append({'name':name,'passed':False,'error':traceback.format_exc()})
 try:
  doc=Rhino.RhinoDoc.ActiveDoc
  assert doc and os.path.dirname(doc.Path)==root
  assemblies=list(System.AppDomain.CurrentDomain.GetAssemblies())
  host=next(a for a in assemblies if a.GetName().Name=='RhinoLayoutFoundry')
  ai=next(a for a in assemblies if a.GetName().Name=='RhinoLayoutFoundry.AI.Rhino')
  plugin=host.GetType('RhinoLayoutFoundry.Rhino.LayoutFoundryPlugin').GetProperty('Instance',S).GetValue(None,None)
  store=plugin.GetType().GetField('_stateStore',F).GetValue(plugin)
  tracker=plugin.GetType().GetField('_revisionTracker',F).GetValue(plugin)
  def make(n,*v):return System.Activator.CreateInstance(host.GetType('RhinoLayoutFoundry.Rhino.'+n),F,None,args(*v),None)
  def call(o,n,*v):return o.GetType().GetMethod(n,F).Invoke(o,args(*v))
  def storage():
   section='LayoutFoundry.AI.Conversations';latest=doc.Strings.GetValue(section,'Latest')
   valid=json.loads(unicode(doc.Strings.GetValue(section,'Record.'+latest)));payload=json.loads(unicode(valid['Payload']))
   assert payload['Version']==2 and payload['Title']=='Clean-break manual title'
   kinds=[e['Kind'] for e in payload['Events']]
   assert 'Answers' in kinds and 'PlanResult' in kinds and 'ResourceReference' in kinds and 'Attachment' in kinds
   assert 'input_image' not in valid['Payload'] and 'content_base64' not in valid['Payload']
   with open(os.path.join(root,'checkpoint.json'),'w') as f:f.write(valid['Payload'])
   badid=System.Guid.NewGuid();badkey=badid.ToString('N')
   old=dict(valid);old['Id']=str(badid);old['Name']='Unsupported conversation fixture';oldpayload=dict(payload);oldpayload['Version']=1;old['Payload']=json.dumps(oldpayload,ensure_ascii=False)
   original=json.dumps(old,ensure_ascii=False);doc.Strings.SetString(section,'Record.'+badkey,original)
   ids=json.loads(doc.Strings.GetValue(section,'Index'));ids.insert(0,badkey);doc.Strings.SetString(section,'Index',json.dumps(ids))
   typ=ai.GetType('RhinoLayoutFoundry.AI.Rhino.DocumentConversations')
   entries=list(typ.GetMethod('Entries',S).Invoke(None,args(doc.RuntimeSerialNumber)))
   assert len(entries)>=1,'Valid records did not load past unsupported record'
   try:typ.GetMethod('Write',S).Invoke(None,args(doc.RuntimeSerialNumber,badid,System.Guid(valid['FolderId']),'Overwrite forbidden',valid['Payload']));raise AssertionError('Unsupported record overwritten')
   except System.Reflection.TargetInvocationException as e:assert 'preserved' in str(e.InnerException.Message)
   typ.GetMethod('Write',S).Invoke(None,args(doc.RuntimeSerialNumber,System.Guid(valid['Id']),System.Guid(valid['FolderId']),valid['Name'],valid['Payload']))
   assert doc.Strings.GetValue(section,'Record.'+badkey)==original
   assert badkey in json.loads(doc.Strings.GetValue(section,'Index'))
  def duplicate():
   clr.AddReference('RhinoLayoutFoundry.Core')
   from RhinoLayoutFoundry.Core.Operations import DuplicateHierarchySelectionPlanner,DuplicateHierarchySelectionRequest
   from RhinoLayoutFoundry.Core.Overview import OverviewInvalidation,OverviewNodeKey,OverviewNodeKind
   from RhinoLayoutFoundry.Core.Domain import DetailCaptions
   provider=make('RhinoDocumentSnapshotProvider',store,tracker)
   executor=make('RhinoMutationExecutor',tracker,store,System.Action[OverviewInvalidation](lambda x:None))
   snapshot=call(provider,'Capture');source=next(p for p in doc.Views.GetPageViews() if p.PageName=='Clean-break AI plan')
   before=set(str(p.MainViewport.Id) for p in doc.Views.GetPageViews())
   selection=System.Array[OverviewNodeKey]([OverviewNodeKey(OverviewNodeKind.Sheet,source.MainViewport.Id)])
   plan=DuplicateHierarchySelectionPlanner().Plan(DuplicateHierarchySelectionRequest(snapshot.DocumentRuntimeSerialNumber,snapshot.Revision,selection),snapshot)
   assert plan.CanApply
   result=call(executor,'Apply',doc,plan);assert result.Succeeded
   copies=[p for p in doc.Views.GetPageViews() if str(p.MainViewport.Id) not in before];assert len(copies)==1
   copy=copies[0];detail=copy.GetDetailViews()[0]
   assert detail.Attributes.GetUserString(DetailCaptions.SourceViewportKey)==str(detail.Viewport.Id)
   captions=[o for o in doc.Objects if o.Attributes.GetUserString(DetailCaptions.OwnerKey)==str(detail.Id)]
   assert len(captions)>0,'Duplicated caption lost its detail link'
   assert all(o.Attributes.ViewportId==copy.MainViewport.Id for o in captions)
  check('Checkpoint v2 and unsupported-record preservation beside valid conversations',storage)
  check('Active hierarchy duplication preserves detail-caption ownership',duplicate)
 except:results.append({'name':'setup','passed':False,'error':traceback.format_exc()})
 with open(os.path.join(root,'storage-duplicate.json'),'w') as f:json.dump(results,f,indent=2)
 for r in results:print(('PASS ' if r['passed'] else 'FAIL ')+r['name'])
Rhino.RhinoApp.Idle+=run

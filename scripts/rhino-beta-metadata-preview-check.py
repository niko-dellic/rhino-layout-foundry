"""Native preview checkpoint and metadata archive checks, disposable boundary fixture only."""
import Rhino, System, clr, os, json, tempfile, traceback
from System.Reflection import BindingFlags
from System.Collections.Generic import List
clr.AddReference('RhinoLayoutFoundry.Core')
from RhinoLayoutFoundry.Core.Domain import PaperRecipe, BuiltInTitleBlockKind
from RhinoLayoutFoundry.Core.Operations import BatchCreateSheetsPlanner, BatchCreateSheetsRequest, LayoutCreationSpec, BuiltInLayoutKind
from RhinoLayoutFoundry.Core.Overview import DraftLayoutThumbnailKey, DraftLayoutThumbnailRequest
from RhinoLayoutFoundry.Core.Persistence import DocumentStateSerializer, DocumentStateLoadResult
F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic
S=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic
host=next(a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name=='RhinoLayoutFoundry')
def args(*v):return System.Array[System.Object](v)
def make(name,*v):return System.Activator.CreateInstance(host.GetType('RhinoLayoutFoundry.Rhino.'+name),F,None,args(*v),None)
def call(o,n,*v):return o.GetType().GetMethod(n,F).Invoke(o,args(*v))
plugin=host.GetType('RhinoLayoutFoundry.Rhino.LayoutFoundryPlugin').GetProperty('Instance',S).GetValue(None,None)
store=plugin.GetType().GetField('_stateStore',F).GetValue(plugin)
tracker=plugin.GetType().GetField('_revisionTracker',F).GetValue(plugin)
doc=Rhino.RhinoDoc.ActiveDoc
assert doc and os.path.basename(doc.Path or '')=='foundry-boundary-fixture.3dm'
results=[]
def check(name,fn):
 try:fn();results.append({'name':name,'passed':True})
 except:results.append({'name':name,'passed':False,'error':traceback.format_exc()})
def native_content(d):
 return {'pages':sorted(str(p.MainViewport.Id) for p in d.Views.GetPageViews()),'layers':sorted((str(x.Id),int(x.DataCRC(System.UInt32(0)))) for x in d.Layers if not x.IsDeleted),'objects':sorted((str(x.Id),x.Attributes.ToJSON(Rhino.FileIO.SerializationOptions()),int(x.Geometry.DataCRC(System.UInt32(0)))) for x in d.Objects),'definitions':sorted(str(x.Id) for x in d.InstanceDefinitions if not x.IsDeleted),'undo':d.UndoRecordingEnabled}
provider=make('RhinoDocumentSnapshotProvider',store,tracker)
snapshot=call(provider,'Capture')
specs=List[LayoutCreationSpec]();specs.Add(LayoutCreationSpec(1,PaperRecipe(420,297,'Millimeters'),BuiltInLayoutKind.SingleDetail,None,None,BuiltInTitleBlockKind.FullWidthBottom))
plan=BatchCreateSheetsPlanner().Plan(BatchCreateSheetsRequest(snapshot.DocumentRuntimeSerialNumber,snapshot.Revision,snapshot.RootFolderId,specs,'PreviewProbe-{index}',1,1),snapshot)
assert plan.CanApply
request=DraftLayoutThumbnailRequest(DraftLayoutThumbnailKey(doc.RuntimeSerialNumber,System.Guid.NewGuid(),320,240,System.Int64(0)),plan.Changes[0])
def preview(stage):
 reached=[]
 def fail(value):
  if value==stage:reached.append(value);raise System.InvalidOperationException('Injected '+stage)
 capture=make('RhinoDraftLayoutThumbnailProvider',store,System.Action[System.String](fail))
 before=native_content(doc)
 result=call(capture,'CaptureAsync',request,System.Threading.CancellationToken(False)).Result
 assert reached and not result.Succeeded,'Checkpoint not exercised or failure ignored'
 assert native_content(doc)==before,'Native content or Undo differs after '+stage
for stage in ['preview-page','preview-detail','preview-title-block','preview-appearance']:
 check('preview content restoration at '+stage,lambda stage=stage:preview(stage))
def nontrivial_appearance_preview():
 original=call(store,'Get',doc);index=-1;object_id=System.Guid.Empty
 try:
  layer=Rhino.DocObjects.Layer();layer.Name='PreviewAppearanceProbe-'+str(System.Guid.NewGuid())
  index=doc.Layers.Add(layer);layer=doc.Layers[index]
  attributes=Rhino.DocObjects.ObjectAttributes();attributes.LayerIndex=index
  object_id=doc.Objects.AddPoint(Rhino.Geometry.Point3d(3,4,5),attributes)
  mode=Rhino.Display.DisplayModeDescription.GetDisplayModes()[0]
  payload=json.loads(DocumentStateSerializer.Serialize(original))
  payload['AppearanceRules'].append({'Scope':{'Kind':0,'Id':str(original.RootFolderId)},'LayerRules':[{'Layer':{'LayerId':str(layer.Id),'FullPath':layer.FullPath},'Visibility':1}],'ObjectDisplayRules':[{'Selector':{'Kind':0,'ObjectId':str(object_id),'LayerId':None,'LayerFullPath':None},'DisplayModeId':str(mode.Id),'DisplayModeName':mode.EnglishName}]})
  call(store,'Set',doc,DocumentStateSerializer.Deserialize(json.dumps(payload)))
  before=native_content(doc);reached=[]
  def fail(value):
   if value!='preview-appearance':return
   temporary=[p for p in doc.Views.GetPageViews() if str(p.MainViewport.Id) not in before['pages']]
   assert temporary,'No temporary preview page'
   detail=temporary[0].GetDetailViews()[0];did=detail.Viewport.Id
   assert doc.Layers[index].HasPerViewportSettings(did) and not doc.Layers[index].PerViewportIsVisible(did)
   assert doc.Objects.FindId(object_id).Attributes.GetDisplayModeOverride(did)==mode.Id
   reached.append(value)
   raise System.InvalidOperationException('Injected after nontrivial appearance')
  capture=make('RhinoDraftLayoutThumbnailProvider',store,System.Action[System.String](fail))
  result=call(capture,'CaptureAsync',request,System.Threading.CancellationToken(False)).Result
  assert reached and not result.Succeeded,'Nontrivial appearance was not applied before failure'
  assert native_content(doc)==before,'Nontrivial appearance or native content not restored'
 finally:
  call(store,'Set',doc,original)
  if object_id!=System.Guid.Empty:doc.Objects.Delete(object_id,True)
  if index>=0:doc.Layers.Delete(index,True)
check('nontrivial preview layer and object appearance restoration',nontrivial_appearance_preview)
payload=DocumentStateSerializer.Serialize(call(store,'Get',doc))
root=os.path.join(tempfile.gettempdir(),'foundry-beta-metadata')
if not os.path.isdir(root):os.makedirs(root)
def archive(label,version,value,writable):
 d=Rhino.RhinoDoc.CreateHeadless(None);opened=None
 try:
  envelope=Rhino.Collections.ArchivableDictionary(1,'RhinoLayoutFoundry.DocumentState')
  envelope.Set('SchemaVersion',System.Int32(version));envelope.Set('Payload',value)
  loaded=DocumentStateLoadResult.Read(System.Nullable[System.Int32](version),value)
  entries=store.GetType().GetField('_entries',F).GetValue(store)
  entry_type=entries.GetType().GetGenericArguments()[1]
  entry=System.Activator.CreateInstance(entry_type,F,None,args(loaded,envelope),None)
  entries.GetType().GetProperty('Item').SetValue(entries,entry,args(d.RuntimeSerialNumber))
  assert call(store,'CanWrite',d)==writable
  if not writable:
   blocked=False
   try:call(store,'Set',d,call(store,'Get',d))
   except:blocked=True
   assert blocked,'Invalid metadata accepted a mutation'
  path=os.path.join(root,label+'.3dm')
  options=Rhino.FileIO.FileWriteOptions();options.WriteUserData=True;options.SuppressDialogBoxes=True;options.SuppressAllInput=True
  try:assert d.Write3dmFile(path,options)
  finally:options.Dispose()
  opened=Rhino.RhinoDoc.OpenHeadless(path)
  assert opened and call(store,'CanWrite',opened)==writable
  restored=entries.GetType().GetProperty('Item').GetValue(entries,args(opened.RuntimeSerialNumber))
  original=entry_type.GetProperty('OriginalEnvelope',F).GetValue(restored,None)
  assert original['Payload']==value and original['SchemaVersion']==version,'Archive passthrough changed'
  if writable:
   state=call(store,'Get',opened)
   assert state.SchemaVersion==17
   assert json.loads(DocumentStateSerializer.Serialize(state))==json.loads(DocumentStateSerializer.Serialize(loaded.State))
   # An intentional state write upgrades the archive, then survives Save As/reopen.
   call(store,'Set',opened,state)
   options=Rhino.FileIO.FileWriteOptions();options.WriteUserData=True;options.SuppressDialogBoxes=True
   try:assert opened.Write3dmFile(os.path.join(root,label+'-save-as.3dm'),options)
   finally:options.Dispose()
   saved=Rhino.RhinoDoc.OpenHeadless(os.path.join(root,label+'-save-as.3dm'))
   try:
    assert call(store,'CanWrite',saved)
    assert json.loads(DocumentStateSerializer.Serialize(call(store,'Get',saved)))==json.loads(DocumentStateSerializer.Serialize(state))
   finally:call(store,'Remove',saved);saved.Dispose()
 finally:
  if opened:call(store,'Remove',opened);opened.Dispose()
  call(store,'Remove',d);d.Dispose()
check('current metadata native save/reopen and Save As',lambda:archive('current',17,payload,True))
legacy=json.loads(payload);legacy['SchemaVersion']=16
check('schema-16 migration and intentional save upgrade',lambda:archive('schema16',16,json.dumps(legacy),True))
check('malformed metadata is protected and preserved',lambda:archive('malformed',17,'{"SchemaVersion":17,broken',False))
check('mismatched metadata is protected and preserved',lambda:archive('mismatch',16,payload,False))
with open(os.path.join(root,'report.json'),'w') as f:json.dump({'host':host.Location,'rhino':str(Rhino.RhinoApp.Version),'results':results},f,indent=2)
for r in results:print(('PASS ' if r['passed'] else 'FAIL ')+r['name'])
print(root)

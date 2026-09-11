"""Compare optimized native snapshot overrides to direct native enumeration.
Run only on the disposable benchmark-paste-save-as.3dm copy; do not save.
"""
import Rhino,System,clr,json,os,tempfile,traceback
from System.Reflection import BindingFlags
F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public
S=BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public
host=next(a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name=='RhinoLayoutFoundry')
plugin=host.GetType('RhinoLayoutFoundry.Rhino.LayoutFoundryPlugin').GetProperty('Instance',S).GetValue(None,None)
def field(n):return plugin.GetType().GetField(n,F).GetValue(plugin)
provider=System.Activator.CreateInstance(host.GetType('RhinoLayoutFoundry.Rhino.RhinoDocumentSnapshotProvider'),F,None,System.Array[System.Object]([field('_stateStore'),field('_revisionTracker')]),None)
doc=Rhino.RhinoDoc.ActiveDoc
assert doc and os.path.basename(doc.Path or '')=='benchmark-paste-save-as.3dm'
assert len(doc.Views.GetPageViews())==201,'Interactive Save As did not retain pasted sheet'
report={'status':'failed','host':host.Location,'saved_copy_sheets':201,'saved_copy_details':sum(len(p.GetDetailViews()) for p in doc.Views.GetPageViews())}
ids=[];index=-1
try:
 details=doc.Views.GetPageViews()[0].GetDetailViews()
 layer=Rhino.DocObjects.Layer();layer.Name='SnapshotProbe-'+str(System.Guid.NewGuid())
 index=doc.Layers.Add(layer);layer=Rhino.DocObjects.Layer();layer.CopyAttributesFrom(doc.Layers[index])
 layer.SetPerViewportVisible(details[0].Viewport.Id,False);assert doc.Layers.Modify(layer,index,True)
 mode=Rhino.Display.DisplayModeDescription.GetDisplayModes()[0]
 for i in range(2):
  attributes=Rhino.DocObjects.ObjectAttributes();attributes.LayerIndex=index
  if i==0:attributes.SetDisplayModeOverride(mode,details[0].Viewport.Id)
  ids.append(doc.Objects.AddPoint(Rhino.Geometry.Point3d(i,0,0),attributes))
 snapshot=provider.GetType().GetMethod('Capture',F).Invoke(provider,None)
 expected_layers=[];expected_objects=[]
 for page in doc.Views.GetPageViews():
  for detail in page.GetDetailViews():
   did=detail.Viewport.Id
   for l in doc.Layers:
    if not l.IsDeleted and not l.IsReference and l.HasPerViewportSettings(did):expected_layers.append((str(did),str(l.Id),bool(l.PerViewportIsVisible(did)),True))
   for obj in doc.Objects:
    if not isinstance(obj,Rhino.DocObjects.DetailViewObject) and obj.Attributes.Space==Rhino.DocObjects.ActiveSpace.ModelSpace and obj.Attributes.HasDisplayModeOverride(did):expected_objects.append((str(did),str(obj.Id),str(obj.Attributes.GetDisplayModeOverride(did))))
 actual_layers=[(str(x.DetailViewportId),str(x.LayerId),bool(x.IsVisible),bool(x.HasExplicitOverride)) for x in snapshot.DetailLayers]
 actual_objects=[(str(x.DetailViewportId),str(x.ObjectId),str(x.DisplayModeId)) for x in snapshot.ObjectOverrides]
 assert sorted(expected_layers)==sorted(actual_layers),'Layer visibility snapshot differs from native enumeration'
 assert sorted(expected_objects)==sorted(actual_objects),'Object display override snapshot differs from native enumeration'
 assert (str(details[0].Viewport.Id),str(doc.Layers[index].Id),False,True) in actual_layers
 assert (str(details[0].Viewport.Id),str(ids[0]),str(mode.Id)) in actual_objects
 assert not any(x[1]==str(ids[1]) for x in actual_objects)
 assert len(snapshot.Sheets)==201
 report.update({'status':'passed','layer_overrides':len(actual_layers),'object_overrides':len(actual_objects),'nontrivial_override_positive_and_negative_checks':True})
except:report['error']=traceback.format_exc()
finally:
 for oid in ids:doc.Objects.Delete(oid,True)
 if index>=0:doc.Layers.Delete(index,True)
with open(os.path.join(tempfile.gettempdir(),'foundry-beta-snapshot-check.json'),'w') as f:json.dump(report,f,indent=2)
print(report)

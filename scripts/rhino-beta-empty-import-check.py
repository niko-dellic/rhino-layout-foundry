"""Destructive checks on the named disposable boundary fixture; do not save afterward."""
import os, tempfile
source=os.path.join(os.path.dirname(__file__),'rhino-boundary-checks.py')
exec(compile(open(source).read().split('check("preview ownership after partial construction"')[0],source,'exec'))
doc=Rhino.RhinoDoc.ActiveDoc
assert doc and os.path.basename(doc.Path or '')=='foundry-boundary-fixture.3dm'
plugin_type=host.GetType('RhinoLayoutFoundry.Rhino.LayoutFoundryPlugin')
plugin=plugin_type.GetProperty('Instance',STATIC).GetValue(None,None)
store=plugin_type.GetField('_stateStore',FLAGS).GetValue(plugin)
tracker=plugin_type.GetField('_revisionTracker',FLAGS).GetValue(plugin)
service=create('RhinoLayoutPackageService',store,tracker,System.Action(lambda:None),None)
path=os.path.join(tempfile.gettempdir(),'foundry-empty-destination-source.rlf')
request=System.Activator.CreateInstance(core.GetType('RhinoLayoutFoundry.Core.Persistence.LayoutPackageExportRequest'),args(doc.RuntimeSerialNumber,call(tracker,'Current',doc),path))
if len(doc.Views.GetPageViews())>0:
 assert call(service,'ExportOnUiThread',request,System.Threading.CancellationToken(False)).Succeeded
else:assert os.path.exists(path),'Prepare the source package from a populated fixture first'
for page in list(doc.Views.GetPageViews()):assert page.Close()
assert len(doc.Views.GetPageViews())==0
for mode in ['Merge','Replace']:
 for stage in ['named-views','page','page-objects','metadata','cancel:layer-states']:
  check('empty destination '+mode+' rollback at '+stage,lambda stage=stage,mode=mode:import_rollback(stage,mode,path))
report=os.path.join(tempfile.gettempdir(),'foundry-empty-import-checks.json')
with open(report,'w') as f:json.dump({'rhino':str(Rhino.RhinoApp.Version),'results':results},f,indent=2)
for result in results:print(('PASS ' if result['passed'] else 'FAIL ')+result['name'])
print(report)

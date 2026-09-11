"""Create a disposable 200-sheet/1000-detail fixture in a NEW unsaved document.
Measures native overview capture, filtering and actual panel refresh, not pointer
latency or full memory-soak acceptance. Saves a reproducible fixture and report.
"""
import Rhino, System, json, os, tempfile, traceback, clr
from System.Reflection import BindingFlags
from System.Diagnostics import Stopwatch, Process
clr.AddReference('RhinoLayoutFoundry.Core');clr.AddReference('RhinoLayoutFoundry.UI')
from RhinoLayoutFoundry.Core.Overview import OverviewTreeBuilder, OverviewTreeFilter
from RhinoLayoutFoundry.UI import LayoutFoundryPanel
FLAGS=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public
STATIC=BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public
root=os.path.join(tempfile.gettempdir(),'foundry-beta-scale')
if not os.path.isdir(root):os.makedirs(root)
def run(sender,event):
 Rhino.RhinoApp.Idle-=run
 report={'rhino':str(Rhino.RhinoApp.Version),'status':'failed'}
 try:
  doc=Rhino.RhinoDoc.ActiveDoc
  assert doc and (not doc.Path or os.path.basename(doc.Path)=='benchmark-200.3dm') and (len(doc.Objects)==0 or (len(doc.Views.GetPageViews())==200 and all(p.PageName.startswith('Benchmark-') for p in doc.Views.GetPageViews()))),'Use a NEW empty or existing benchmark document'
  doc.ModelUnitSystem=Rhino.UnitSystem.Millimeters;doc.PageUnitSystem=Rhino.UnitSystem.Millimeters
  before=Process.GetCurrentProcess().WorkingSet64
  watch=Stopwatch.StartNew()
  doc.Views.RedrawEnabled=False
  try:
   if len(doc.Objects)==0:doc.Objects.AddBox(Rhino.Geometry.Box(Rhino.Geometry.BoundingBox(0,0,0,100,100,100)))
   for i in range(0 if len(doc.Views.GetPageViews())==200 else 200):
    page=doc.Views.AddPageView('Benchmark-%03d'%(i+1),420,297)
    for j in range(5):
     x=10+(j%3)*130;y=10+(j//3)*140
     detail=page.AddDetailView('Detail-%d'%(j+1),Rhino.Geometry.Point2d(x,y),Rhino.Geometry.Point2d(x+120,y+125),Rhino.Display.DefinedViewportProjection.Top)
     assert detail is not None
  finally:doc.Views.RedrawEnabled=True
  report['fixture_creation_ms']=watch.Elapsed.TotalMilliseconds
  host=next(a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name=='RhinoLayoutFoundry')
  plugin=host.GetType('RhinoLayoutFoundry.Rhino.LayoutFoundryPlugin').GetProperty('Instance',STATIC).GetValue(None,None)
  store=plugin.GetType().GetField('_stateStore',FLAGS).GetValue(plugin)
  provider=System.Activator.CreateInstance(host.GetType('RhinoLayoutFoundry.Rhino.RhinoDocumentOverviewProvider'),FLAGS,None,System.Array[System.Object]([store]),None)
  def timed(fn):
   w=Stopwatch.StartNew();value=fn();return (w.Elapsed.TotalMilliseconds,value)
  cold,overview=timed(lambda:provider.GetType().GetMethod('Capture',FLAGS).Invoke(provider,None))
  warm=[timed(lambda:provider.GetType().GetMethod('Capture',FLAGS).Invoke(provider,None))[0] for _ in range(5)]
  report.update({'sheets':len(overview.Sheets),'details':sum(len(s.Details) for s in overview.Sheets),'capture_cold_ms':cold,'capture_warm_ms':warm})
  panel_host=LayoutFoundryPanel()
  workspace=panel_host.GetType().GetField('_workspace',FLAGS)
  panel=workspace.GetValue(panel_host) if workspace else panel_host
  try:
   refresh=panel.GetType().GetMethod('RefreshOverview',FLAGS)
   report['panel_refresh_ms']=[timed(lambda:refresh.Invoke(panel,None))[0] for _ in range(5)]
   field=panel.GetType().GetField('_filterTextBox',FLAGS).GetValue(panel)
   def query(value):field.Text=value
   report['filter_input_ms']=[timed(lambda q=q:query(q))[0] for q in ['Benchmark-1','Detail-3','missing','']]
   System.GC.Collect();System.GC.WaitForPendingFinalizers();System.GC.Collect()
  finally:panel_host.Dispose()
  report['working_set_before_bytes']=before;report['working_set_after_bytes']=Process.GetCurrentProcess().WorkingSet64
  options=Rhino.FileIO.FileWriteOptions();options.SuppressDialogBoxes=True;options.WriteUserData=True
  assert doc.Write3dmFile(os.path.join(root,'benchmark-200.3dm'),options);options.Dispose()
  report['status']='measured'
 except:report['error']=traceback.format_exc()
 with open(os.path.join(root,'report.json'),'w') as f:json.dump(report,f,indent=2,default=str)
 print(json.dumps(report,default=str))
Rhino.RhinoApp.Idle+=run
print('Scale fixture scheduled on Idle')

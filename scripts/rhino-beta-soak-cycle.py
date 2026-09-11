"""Measure one native benchmark cycle on Idle. Close/reopen the fixture between runs.
Actual loaded panel view switches and scroll handlers are timed. Capture samples
include GC counters to distinguish allocation/collection effects. Not physical
pointer-to-frame latency. Reports append to a temp JSON file.
"""
import Rhino, System, clr, os, json, tempfile, traceback
from System.Reflection import BindingFlags
from System.Diagnostics import Stopwatch, Process
clr.AddReference('RhinoLayoutFoundry.UI')
from RhinoLayoutFoundry.UI import LayoutFoundryPanel
from Eto.Drawing import Point
F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic
S=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic
def args(*v):return System.Array[System.Object](v)
def call(o,n,*v):return o.GetType().GetMethod(n,F).Invoke(o,args(*v))
def field(o,n):return o.GetType().GetField(n,F).GetValue(o)
def run(sender,event):
 Rhino.RhinoApp.Idle-=run
 path=os.path.join(tempfile.gettempdir(),'foundry-beta-soak-resumed.json')
 try:reports=json.load(open(path)) if os.path.exists(path) else []
 except ValueError:reports=[]
 r={'cycle':len(reports)+1,'status':'failed'}
 try:
  doc=Rhino.RhinoDoc.ActiveDoc
  assert doc and os.path.basename(doc.Path or '')=='benchmark-200.3dm' and len(doc.Views.GetPageViews())==200
  panel=Rhino.UI.Panels.GetPanel(clr.GetClrType(LayoutFoundryPanel).GUID,doc)
  assert panel is not None,'Open LayoutFoundry panel first'
  workspace=panel.GetType().GetField('_workspace',F)
  if workspace:panel=workspace.GetValue(panel)
  assert panel is not None and field(panel,'_isLoaded'),'Benchmark requires a mounted, loaded workspace'
  host=next(a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name=='RhinoLayoutFoundry')
  plugin=host.GetType('RhinoLayoutFoundry.Rhino.LayoutFoundryPlugin').GetProperty('Instance',S).GetValue(None,None)
  store=field(plugin,'_stateStore')
  provider=System.Activator.CreateInstance(host.GetType('RhinoLayoutFoundry.Rhino.RhinoDocumentOverviewProvider'),F,None,args(store),None)
  def timed(label,fn):
   gc=[System.GC.CollectionCount(i) for i in range(3)];w=Stopwatch.StartNew();fn();ms=w.Elapsed.TotalMilliseconds
   return {'action':label,'ms':ms,'collections':[System.GC.CollectionCount(i)-gc[i] for i in range(3)]}
  r['capture']=[timed('capture',lambda:call(provider,'Capture')) for _ in range(10)]
  r['refresh']=timed('panel_refresh',lambda:call(panel,'RefreshOverview'))
  actions=[]
  for name in ['ShowListView','ShowThumbnailView','ShowCanvasView','ShowListView']:
   actions.append(timed(name,lambda name=name:call(panel,name)))
  search=field(panel,'_filterTextBox')
  def query(q):search.Text=q
  for q in ['Benchmark-1','Detail-3','missing','']:actions.append(timed('filter '+q,lambda q=q:query(q)))
  thumb=field(panel,'_thumbnailView');scroll=field(thumb,'_scrollable')
  call(panel,'ShowThumbnailView')
  def move(y):scroll.ScrollPosition=Point(0,y)
  for y in [100,500,1500,3000,0]:actions.append(timed('thumbnail scroll '+str(y),lambda y=y:move(y)))
  call(panel,'ShowCanvasView');call(panel,'ShowListView')
  r['input']=actions
  System.GC.Collect();System.GC.WaitForPendingFinalizers();System.GC.Collect()
  process=Process.GetCurrentProcess();process.Refresh()
  r.update({'status':'measured','managed_retained_bytes':int(System.GC.GetTotalMemory(False)),'working_set_bytes':int(process.WorkingSet64),'private_bytes':int(process.PrivateMemorySize64),'serial':int(doc.RuntimeSerialNumber),'documents':len(list(Rhino.RhinoDoc.OpenDocuments())),'process_id':int(process.Id)})
 except:r['error']=traceback.format_exc()
 reports.append(r)
 with open(path,'w') as f:json.dump(reports,f,indent=2)
 print('Soak cycle '+str(r['cycle'])+': '+r['status']+'; '+path)
Rhino.RhinoApp.Idle+=run
print('Benchmark cycle scheduled on Idle')

"""Export/cancel/failure checks on the disposable foundry-boundary-fixture.3dm.
Run after rhino-current-contracts.py. Produces PDF evidence for external inspection.
"""
import Rhino, System, json, os, tempfile, traceback, clr
from System.Reflection import BindingFlags
clr.AddReference('RhinoLayoutFoundry.Core')
from RhinoLayoutFoundry.Core.Overview import LayoutPdfExportRequest
FLAGS=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic
doc=Rhino.RhinoDoc.ActiveDoc
assert doc and os.path.basename(doc.Path or '')=='foundry-boundary-fixture.3dm', 'Use the disposable boundary fixture'
root=os.path.join(tempfile.gettempdir(),'foundry-beta-pdf')
if not os.path.isdir(root):os.makedirs(root)
host=next(a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name=='RhinoLayoutFoundry')
service=System.Activator.CreateInstance(host.GetType('RhinoLayoutFoundry.Rhino.RhinoLayoutPdfExportService'),True)
pages=sorted(doc.Views.GetPageViews(),key=lambda p:p.PageWidth)
ids=System.Array[System.Guid]([p.MainViewport.Id for p in pages])
results=[]
def export(path,token,selected=ids,target=service):
 request=LayoutPdfExportRequest(doc.RuntimeSerialNumber,selected,path,150)
 return target.GetType().GetMethod('ExportAsync',FLAGS).Invoke(target,System.Array[System.Object]([request,token])).Result
def check(name,fn):
 try:fn();results.append({'name':name,'passed':True})
 except:results.append({'name':name,'passed':False,'error':traceback.format_exc()})
none=getattr(System.Threading.CancellationToken,'None')
def ordered():
 result=export(os.path.join(root,'ordered.pdf'),none)
 assert result.Succeeded,result.Message
 assert result.PageCount==len(pages)
def cancelled():
 path=os.path.join(root,'cancelled.pdf')
 with open(path,'wb') as f:f.write(b'original destination')
 cts=System.Threading.CancellationTokenSource();cts.Cancel()
 result=export(path,cts.Token)
 assert not result.Succeeded and open(path,'rb').read()==b'original destination'
 cts.Dispose()
def missing():
 path=os.path.join(root,'missing.pdf')
 with open(path,'wb') as f:f.write(b'original destination')
 result=export(path,none,System.Array[System.Guid]([System.Guid.NewGuid()]))
 assert not result.Succeeded and open(path,'rb').read()==b'original destination'
def late_cancelled(stage):
 path=os.path.join(root,stage+'-cancelled.pdf')
 with open(path,'wb') as f:f.write(b'original destination')
 cts=System.Threading.CancellationTokenSource()
 reached=[]
 def checkpoint(value):
  reached.append(value)
  if value==stage:cts.Cancel()
 target=System.Activator.CreateInstance(service.GetType(),FLAGS,None,System.Array[System.Object]([System.Action[System.String](checkpoint)]),None)
 try:
  result=export(path,cts.Token,System.Array[System.Guid]([ids[0]]),target)
  assert stage in reached and cts.IsCancellationRequested, 'Checkpoint was not exercised'
  with open(path,'rb') as f:assert not result.Succeeded and f.read()==b'original destination'
  assert not [n for n in os.listdir(root) if '.tmp.pdf' in n], 'Temporary PDF leaked'
 finally:cts.Dispose()
check('ordered native PDF export',ordered)
check('cancelled PDF preserves existing destination',cancelled)
check('missing page preserves existing destination',missing)
check('cancellation after final capture preserves destination',lambda:late_cancelled('pages-captured'))
check('cancellation after temporary write preserves destination and removes temporary file',lambda:late_cancelled('temporary-written'))
assert not [n for n in os.listdir(root) if '.tmp.pdf' in n], 'Temporary PDF leaked'
report={'rhino':str(Rhino.RhinoApp.Version),'results':results,'pages':[{'name':p.PageName,'width':p.PageWidth,'height':p.PageHeight,'units':str(doc.PageUnitSystem)} for p in pages]}
with open(os.path.join(root,'report.json'),'w') as f:json.dump(report,f,indent=2)
for r in results:print(('PASS ' if r['passed'] else 'FAIL ')+r['name'])
print(root)

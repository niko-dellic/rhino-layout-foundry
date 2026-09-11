"""Ten-cycle session harness: load ONCE on the open benchmark, then close/reopen
it through the UI. Reuses the same Python code and callbacks so repeated script
compilation is not mistaken for panel retention. Stops after ten closes.
"""
import scriptcontext
import os,json,tempfile,System,Rhino
from System.Reflection import BindingFlags
source=os.path.join(os.path.dirname(__file__),'rhino-beta-soak-cycle.py')
assert 'Foundry.beta.soak.session' not in scriptcontext.sticky,'A session is already running'
report_path=os.path.join(tempfile.gettempdir(),'foundry-beta-soak-session.json')
assert not os.path.exists(report_path),'Archive the previous session report before beginning another'
state={'panels':[],'closes':[],'last_serial':None}
code=open(source).read().split('Rhino.RhinoApp.Idle+=run')[0]
code=code.replace('foundry-beta-soak-resumed.json','foundry-beta-soak-session.json')
code=code.replace("assert panel is not None,'Open LayoutFoundry panel first'", "assert panel is not None,'Open LayoutFoundry panel first'\n  state['panels'].append(System.WeakReference(panel))")
exec(compile(code,source,'exec'))
def after_close(sender,event):
 Rhino.RhinoApp.Idle-=after_close
 System.GC.Collect();System.GC.WaitForPendingFinalizers();System.GC.Collect()
 process=System.Diagnostics.Process.GetCurrentProcess();process.Refresh()
 rows=[]
 for ref in state['panels']:
  target=ref.Target
  rows.append({'alive':bool(ref.IsAlive),'loaded':bool(field(target,'_isLoaded')) if target else None})
 target=None
 state['closes'].append({'closed_cycle':len(state['closes'])+1,'managed_retained_bytes':int(System.GC.GetTotalMemory(False)),'working_set_bytes':int(process.WorkingSet64),'panels':rows,'open_documents':len(list(Rhino.RhinoDoc.OpenDocuments()))})
 with open(os.path.join(tempfile.gettempdir(),'foundry-beta-soak-session-closes.json'),'w') as f:json.dump(state['closes'],f,indent=2)
 if len(state['closes'])>=10:
  Rhino.RhinoApp.Idle-=observe;Rhino.RhinoDoc.CloseDocument-=closed
  scriptcontext.sticky.pop('Foundry.beta.soak.session',None)
  print('Ten-cycle session complete')
def observe(sender,event):
 document=Rhino.RhinoDoc.ActiveDoc
 if document and os.path.basename(document.Path or '')=='benchmark-200.3dm' and document.RuntimeSerialNumber!=state['last_serial']:
  state['last_serial']=document.RuntimeSerialNumber
  run(sender,event)
def closed(sender,event):
 if os.path.basename(event.Document.Path or '')=='benchmark-200.3dm':Rhino.RhinoApp.Idle+=after_close
Rhino.RhinoApp.Idle+=observe
Rhino.RhinoDoc.CloseDocument+=closed
scriptcontext.sticky['Foundry.beta.soak.session']=(observe,closed)
print('Session harness installed once; close/reopen benchmark ten times')

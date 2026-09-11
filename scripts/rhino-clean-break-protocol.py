"""Final loaded-pair protocol and obsolete-contract refusal; never applies a plan."""
import os,json,clr,System,Rhino,traceback
from System.Reflection import BindingFlags
from System.Threading import CancellationToken
clr.AddReference('RhinoLayoutFoundry.UI')
from RhinoLayoutFoundry.UI import LayoutFoundryPanel
root='/Users/nikodellic/Documents/GitHub/rhino-layout-foundry/artifacts/pre-beta-clean-break-20260911/native'
doc=Rhino.RhinoDoc.ActiveDoc
assert doc and os.path.dirname(doc.Path)==root
S=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic
assemblies=list(System.AppDomain.CurrentDomain.GetAssemblies())
ui=next(a for a in assemblies if a.GetName().Name=='RhinoLayoutFoundry.UI')
ai=next(a for a in assemblies if a.GetName().Name=='RhinoLayoutFoundry.AI.Core')
bridge=ui.GetType('RhinoLayoutFoundry.UI.FoundryAutomationBridge')
dispatch=bridge.GetMethod('DispatchAsync',S)
def request(value):return json.loads(unicode(dispatch.Invoke(None,System.Array[System.Object]([json.dumps(value),CancellationToken.None])).Result))
results=[]
def check(name,fn):
 try:fn();results.append({'name':name,'passed':True})
 except:results.append({'name':name,'passed':False,'error':traceback.format_exc()})
def mismatch():
 for version in [1,'1',None]:
  assert 'Update both' in request({'protocol_major':version,'operation':'inspect_document'})['error']
def unknown():assert 'Unsupported' in request({'protocol_major':2,'operation':'stage_create_layouts','arguments':{}})['error']
def old_ai():
 key='RhinoLayoutFoundry.AI.AutomationProtocolMajor';old=System.AppDomain.CurrentDomain.GetData(key)
 try:
  System.AppDomain.CurrentDomain.SetData(key,System.Int32(1))
  panel=Rhino.UI.Panels.GetPanel(clr.GetClrType(LayoutFoundryPanel).GUID,doc)
  try:panel.TryInvokeCreateAction('rhino-layout-foundry.ai');raise AssertionError('Old AI pair accepted')
  except System.InvalidOperationException as e:assert 'Update both' in e.Message
 finally:System.AppDomain.CurrentDomain.SetData(key,old)
def old_layout():
 m=ai.GetType('RhinoLayoutFoundry.AI.Core.AiAutomationProtocol').GetMethod('RequireMatchingHost')
 try:m.Invoke(None,System.Array[System.Object](['{"protocol":{"protocolMajor":1}}']));raise AssertionError('Old Layout accepted')
 except System.Reflection.TargetInvocationException as e:assert 'Update both' in e.InnerException.Message
def loaded():
 import hashlib
 manifest=json.load(open(os.path.join(root,'..','assembly-hashes.json')))['MacOS-Debug']
 for a in assemblies:
  path=a.Location
  name=os.path.basename(path)
  if name in manifest:assert hashlib.sha256(open(path,'rb').read()).hexdigest()==manifest[name],name
check('Protocol mismatches fail before host operations',mismatch)
check('Removed creation tool rejected by native bridge',unknown)
check('Old AI registration rejected before opening a task',old_ai)
check('Old Layout context rejected before starting AI',old_layout)
check('Loaded assemblies match final staged hashes',loaded)
with open(os.path.join(root,'protocol.json'),'w') as f:json.dump(results,f,indent=2)
for r in results:print(('PASS ' if r['passed'] else 'FAIL ')+r['name'])

"""Run once on a fresh foundry-boundary-fixture.3dm with one sheet/detail.

The fixture needs ordinary model/page geometry and millimeter page units.
Checks live sheet/detail registration and built-in/imperial creation. Mutates the
fixture. Waits for Rhino Idle: RunPythonScript already owns an Undo record, while
these modeless operations must acquire their own record after the command ends.
"""
import os
import tempfile
import traceback
import Rhino

def run_checks():
    import clr
    import Rhino
    import System
    import json
    import traceback
    from System.Reflection import BindingFlags
    from System.Collections.Generic import List
    clr.AddReference('RhinoLayoutFoundry.Core')
    from RhinoLayoutFoundry.Core.Domain import HierarchyScope, HierarchyScopeKind, PaperRecipe, BuiltInTitleBlockKind
    from RhinoLayoutFoundry.Core.Operations import SetLayoutTemplateRegistrationPlanner, SetLayoutTemplateRegistrationRequest, BatchCreateSheetsPlanner, BatchCreateSheetsRequest, LayoutCreationSpec, BuiltInLayoutKind
    from RhinoLayoutFoundry.Core.Overview import OverviewInvalidation
    FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
    STATIC = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
    host = next(a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name == 'RhinoLayoutFoundry')
    core = next(a for a in System.AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name == 'RhinoLayoutFoundry.Core')
    pluginType = host.GetType('RhinoLayoutFoundry.Rhino.LayoutFoundryPlugin')
    plugin = pluginType.GetProperty('Instance',STATIC).GetValue(None,None)
    store = pluginType.GetField('_stateStore',FLAGS).GetValue(plugin)
    tracker = pluginType.GetField('_revisionTracker',FLAGS).GetValue(plugin)
    def args(*a):return System.Array[System.Object](a)
    def make(name,*a):return System.Activator.CreateInstance(host.GetType('RhinoLayoutFoundry.Rhino.'+name),FLAGS,None,args(*a),None)
    def call(obj,name,*a):return obj.GetType().GetMethod(name,FLAGS).Invoke(obj,args(*a))
    provider=make('RhinoDocumentSnapshotProvider',store,tracker)
    executor=make('RhinoMutationExecutor',tracker,store,System.Action[OverviewInvalidation](lambda x:None))
    doc=Rhino.RhinoDoc.ActiveDoc
    assert doc and doc.Path and os.path.basename(doc.Path) == 'foundry-boundary-fixture.3dm', 'Open a fresh disposable boundary fixture'
    assert len(doc.Views.GetPageViews()) == 1, 'Run once on a fresh single-sheet fixture'
    results=[]
    def check(name,f):
     try:
      f();results.append({'name':name,'passed':True})
     except Exception:
      results.append({'name':name,'passed':False,'error':traceback.format_exc()})
    def apply(plan):
     assert plan.CanApply, '\n'.join(d.Message for d in plan.Diagnostics)
     result=call(executor,'Apply',doc,plan)
     assert result.Succeeded, '\n'.join(d.Message for d in result.Diagnostics)
    def register(scope):
     snapshot=call(provider,'Capture')
     apply(SetLayoutTemplateRegistrationPlanner().Plan(SetLayoutTemplateRegistrationRequest(snapshot.DocumentRuntimeSerialNumber,snapshot.Revision,scope,True),snapshot))
    def templates():
     source=doc.Views.GetPageViews()[0]
     register(HierarchyScope(HierarchyScopeKind.Sheet,source.MainViewport.Id))
     register(HierarchyScope(HierarchyScopeKind.Detail,source.GetDetailViews()[0].Viewport.Id))
     snapshot=call(provider,'Capture')
     assert len(list(snapshot.Templates))==2
     width=source.PageWidth
     try:
      source.PageWidth=500
      assert all(t.Paper.Width==500 for t in call(provider,'Capture').Templates)
     finally:source.PageWidth=width

    def creation():
     snapshot=call(provider,'Capture')
     specs=List[LayoutCreationSpec]()
     specs.Add(LayoutCreationSpec(1,PaperRecipe(420,297,'Millimeters'),BuiltInLayoutKind.SingleDetail))
     specs.Add(LayoutCreationSpec(1,PaperRecipe(420,297,'Millimeters'),BuiltInLayoutKind.SingleDetail,None,None,BuiltInTitleBlockKind.RightSidebar))
     specs.Add(LayoutCreationSpec(1,PaperRecipe(11,17,'Inches'),BuiltInLayoutKind.SingleDetail,None,None,BuiltInTitleBlockKind.FullWidthBottom))
     request=BatchCreateSheetsRequest(snapshot.DocumentRuntimeSerialNumber,snapshot.Revision,snapshot.RootFolderId,specs,'Cleanup-{index}',1,1)
     apply(BatchCreateSheetsPlanner().Plan(request,snapshot))
     state=call(store,'Get',doc)
     managed=[s for s in state.Sheets.Values if s.TitleBlock is not None]
     assert len(managed)==2
     assert set(str(s.TitleBlock.BuiltInKind) for s in managed)==set(['RightSidebar','FullWidthBottom'])
     for s in managed:assert doc.Objects.FindId(s.TitleBlock.InstanceObjectId) is not None
     assert any(abs(p.PageWidth-279.4)<0.001 and abs(p.PageHeight-431.8)<0.001 for p in doc.Views.GetPageViews())

    check('live sheet/detail registration follows source edits',templates)
    check('None/Right/Bottom creation and imperial conversion',creation)
    with open(os.path.join(tempfile.gettempdir(), 'foundry-current-contracts.json'),'w') as f:json.dump(results,f,indent=2)
    for r in results:print(('PASS ' if r['passed'] else 'FAIL ')+r['name'])

def run_when_idle(sender, event):
    Rhino.RhinoApp.Idle -= run_when_idle
    try:
        run_checks()
    except Exception:
        path = os.path.join(tempfile.gettempdir(), 'foundry-current-contracts-error.txt')
        with open(path, 'w') as output:
            output.write(traceback.format_exc())
        print('Current contract checks could not run; see ' + path)

Rhino.RhinoApp.Idle += run_when_idle
print('Modeless current-format checks scheduled after this command ends.')

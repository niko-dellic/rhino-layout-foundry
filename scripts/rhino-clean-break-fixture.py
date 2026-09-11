"""Create a fresh disposable current-candidate fixture; refuses existing content."""
import os, Rhino, System
root='/Users/nikodellic/Documents/GitHub/rhino-layout-foundry/artifacts/pre-beta-clean-break-20260911/native'
doc=Rhino.RhinoDoc.ActiveDoc
assert doc and not doc.Path and doc.Objects.Count==0 and len(doc.Views.GetPageViews())==0, 'Use a fresh empty unsaved document'
doc.ModelUnitSystem=Rhino.UnitSystem.Millimeters
doc.PageUnitSystem=Rhino.UnitSystem.Millimeters
doc.Objects.AddBox(Rhino.Geometry.Box(Rhino.Geometry.BoundingBox(0,0,0,8000,10000,3000)))
page=doc.Views.AddPageView('Basic source',420,297)
page.AddDetailView('Top',Rhino.Geometry.Point2d(10,18),Rhino.Geometry.Point2d(410,277),Rhino.Display.DefinedViewportProjection.Top)
path=os.path.join(root,'foundry-boundary-fixture.3dm')
assert not os.path.exists(path),'Do not overwrite a fixture'
print('Fixture created. Save it through Rhino Save As at: '+path)

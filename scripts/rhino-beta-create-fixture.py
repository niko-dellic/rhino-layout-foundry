"""Create the single-sheet boundary fixture in a new empty unsaved model.
Save it through Rhino as foundry-boundary-fixture.3dm after this command.
"""
import Rhino, System, os
import rhinoscriptsyntax as rs
doc=Rhino.RhinoDoc.ActiveDoc
assert doc is not None and not doc.Path and len(doc.Objects)==0, 'Use a fresh empty document'
doc.ModelUnitSystem=Rhino.UnitSystem.Millimeters
doc.PageUnitSystem=Rhino.UnitSystem.Millimeters
rs.AddNamedView('Beta plan')
line=Rhino.Geometry.LineCurve(Rhino.Geometry.Point3d(0,0,0),Rhino.Geometry.Point3d(40,30,0))
doc.Objects.AddCurve(line)
page=doc.Views.AddPageView('Beta sheet',420,297)
page.AddDetailView('Plan',Rhino.Geometry.Point2d(10,10),Rhino.Geometry.Point2d(400,280),Rhino.Display.DefinedViewportProjection.Top)
geometry=System.Array[Rhino.Geometry.GeometryBase]([line])
attributes=System.Array[Rhino.DocObjects.ObjectAttributes]([Rhino.DocObjects.ObjectAttributes()])
index=doc.InstanceDefinitions.Add('Beta ordinary block','Page block transport',Rhino.Geometry.Point3d.Origin,geometry,attributes)
att=Rhino.DocObjects.ObjectAttributes();att.Space=Rhino.DocObjects.ActiveSpace.PageSpace;att.ViewportId=page.MainViewport.Id
doc.Objects.AddInstanceObject(index,Rhino.Geometry.Transform.Identity,att)
print('Save this disposable model as foundry-boundary-fixture.3dm using Rhino Save As before running checks.')
print('Beta fixture ready')

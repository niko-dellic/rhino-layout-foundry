#if DEBUG
using System.Text.Json;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Extensibility;
namespace RhinoLayoutFoundry.Rhino;
/// <summary>Explicit development smoke test. Never runs on a populated document.</summary>
public static class WorkflowAuthoringSmoke
{
    public static string Run()
    {
        var doc = RhinoDoc.ActiveDoc ?? throw new InvalidOperationException("Open an empty test document first.");
        if (doc.Objects.Any() || doc.Views.GetPageViews().Length != 0) throw new InvalidOperationException("This smoke test only runs on an empty disposable document.");
        var passed = new List<string>();
        try
        {
            doc.ModelUnitSystem = UnitSystem.Millimeters; doc.PageUnitSystem = UnitSystem.Millimeters;
            var store = new DocumentStateStore(); var revision = new DocumentRevisionTracker();
            var snapshot = new RhinoDocumentSnapshotProvider(store, revision);
            var executor = new RhinoMutationExecutor(revision, store, _ => { });
            var box = new BoundingBox(new Point3d(0, 0, 0), new Point3d(100, 100, 100)).ToBrep();
            var objectId = doc.Objects.AddBrep(box);
            var circleId = doc.Objects.AddCircle(new Circle(new Plane(new Point3d(50, 0, 50), Vector3d.XAxis, Vector3d.ZAxis), 20));
            var initial = snapshot.Capture();
            var view = new DrawingViewSpecification("front", "Front", "elevation", 1, new(20, 80, 190, 260), new(50, -500, 50), new(50, 50, 50), new(0, 0, 1), null) { HiddenLayerIds = [] };
            var spec = new DrawingSetSpecification(2, Guid.NewGuid(), doc.RuntimeSerialNumber, initial.Revision, initial.RootFolderId,
                [new("sheet", "Workflow fixture", 420, 297, [view, view with { Key = "iso", Name = "Underside", Kind = "isometric", BoundsMm = new(210, 80, 400, 260), CameraLocation = new(400, -400, -400) }])])
                { DisplayModeId = DisplayModeDescription.ShadedId, TitleBlock = "bottom" };
            void Apply(OperationPlan plan)
            {
                if (!plan.CanApply) throw new InvalidOperationException("Preflight: " + string.Join("; ", plan.Diagnostics.Select(d => d.Message)));
                var result = executor.Apply(doc, plan);
                if (!result.Succeeded) throw new InvalidOperationException("Apply: " + string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            }
            Apply(DrawingSetPlanner.Plan(spec, snapshot.Capture()));
            var page = doc.Views.GetPageViews().Single(); var detailId = page.GetDetailViews().Single(d => d.Attributes.Name == "Front").Viewport.Id;
            if (page.GetDetailViews().Any(d => d.Viewport.DisplayMode.Id != DisplayModeDescription.ShadedId)) throw new InvalidOperationException("Display mode mismatch.");
            if (doc.NamedViews.Any(v => v.Name.Contains(spec.ProposalId.ToString("N")))) throw new InvalidOperationException("UUID in named view.");
            passed.Add("create sheets, bottom titleblock, isometric, selected mode, readable views");
            GeometryAnchor Vertex(double x, double y, double z)
            {
                var b = (Brep)doc.Objects.FindId(objectId).Geometry;
                var vertex = b.Vertices.OrderBy(v => v.Location.DistanceTo(new(x, y, z))).First();
                return new(objectId, [], "vertex", vertex.VertexIndex, 0, RhinoManagedAnnotations.Topology(b));
            }
            var a = Vertex(0, 0, 0); var b = Vertex(100, 0, 0); var c = Vertex(0, 0, 100);
            var radial = new GeometryAnchor(circleId, [], "curve", 0, 0, RhinoManagedAnnotations.Topology(doc.Objects.FindId(circleId).Geometry));
            var annotations = new[]
            {
                new SheetAnnotationSpecification("width", null, "aligned", detailId, [a,b], 105, 100, "", null),
                new SheetAnnotationSpecification("linear", null, "linear", detailId, [a,b], 105, 110, "", null),
                new SheetAnnotationSpecification("angle", null, "angular", detailId, [a,b,c], 105, 180, "", null),
                new SheetAnnotationSpecification("radius", null, "radius", detailId, [radial], 140, 180, "", null),
                new SheetAnnotationSpecification("diameter", null, "diameter", detailId, [radial], 140, 190, "", null),
                new SheetAnnotationSpecification("leader", null, "leader", detailId, [a], 30, 100, "Measured corner", null),
                new SheetAnnotationSpecification("note", null, "note", null, [], 25, 65, "Workflow test note", null),
            };
            SheetUpdateSpecification Update(IReadOnlyList<SheetAnnotationSpecification> items) => new(Guid.NewGuid(), doc.RuntimeSerialNumber, snapshot.Capture().Revision,
                [new(page.MainViewport.Id, null, null, [], items, [])], []);
            var proposal = Update(annotations); Apply(SheetUpdatePlanner.Plan(proposal, snapshot.Capture()));
            if (SheetUpdatePlanner.Plan(proposal, snapshot.Capture()).CanApply) throw new InvalidOperationException("Replayed update accepted.");
            var dimensions = doc.Objects.Where(o => o.Geometry is Dimension).ToArray();
            if (dimensions.Length != 5) throw new InvalidOperationException("Expected five dimensions.");
            passed.Add("all five native dimension kinds, leader, note, duplicate receipt protection");
            var width = dimensions.Single(o => o.Attributes.Name == "width");
            var beforeValue = ((Dimension)width.Geometry).NumericValue;
            doc.Objects.Transform(objectId, Transform.Scale(Plane.WorldXY, 1.5, 1, 1), true);
            RhinoManagedAnnotations.Synchronize(doc);
            var afterValue = ((Dimension)doc.Objects.FindId(width.Id).Geometry).NumericValue;
            if (Math.Abs(afterValue / beforeValue - 1.5) > 1e-5) throw new InvalidOperationException($"Dimension did not update: {beforeValue} -> {afterValue}");
            passed.Add("live geometric dimension update");
            doc.Objects.Delete(objectId, true); RhinoManagedAnnotations.Synchronize(doc);
            if (doc.Objects.FindId(width.Id)?.Attributes.GetUserString(RhinoManagedAnnotations.WarningKey) is null) throw new InvalidOperationException("Missing reference did not warn.");
            passed.Add("deleted source retains annotation and warning");
            var displayBefore = page.GetDetailViews().First().Viewport.DisplayMode.Id;
            var bad = Update([]) with { Sheets = [new(page.MainViewport.Id, "right", null, [], [], [])] };
            var result = executor.Apply(doc, SheetUpdatePlanner.Plan(bad, snapshot.Capture()));
            if (result.Succeeded) throw new InvalidOperationException("Overlapping titleblock accepted.");
            if (doc.Views.GetPageViews().Single().MainViewport.Id != page.MainViewport.Id || page.GetDetailViews().First().Viewport.DisplayMode.Id != displayBefore)
                throw new InvalidOperationException("Failed update changed sheet identity/presentation.");
            passed.Add("failed titleblock reflow restores existing sheet");
            return JsonSerializer.Serialize(new { succeeded = true, passed, width_before = beforeValue, width_after = afterValue });
        }
        catch (Exception exception) { return JsonSerializer.Serialize(new { succeeded = false, passed, error = exception.ToString() }); }
    }
}
#endif

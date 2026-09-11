using System.Text.Json;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using RhinoLayoutFoundry.Extensibility;
namespace RhinoLayoutFoundry.Rhino;
internal sealed partial class RhinoFoundryAutomationHost
{
    public void SelectAnnotation(Guid id)
    {
        var doc = RhinoDoc.ActiveDoc ?? throw new InvalidOperationException("No active document.");
        var obj = doc.Objects.FindId(id);
        if (obj?.Geometry is not AnnotationBase) throw new InvalidOperationException("Annotation no longer exists.");
        var page = doc.Views.GetPageViews().FirstOrDefault(p => p.MainViewport.Id == obj.Attributes.ViewportId);
        if (page is not null) { doc.Views.ActiveView = page; page.SetPageAsActive(); }
        doc.Objects.UnselectAll(); obj.Select(true); doc.Views.Redraw();
    }
    public string? PickAnnotationReferences(Guid id)
    {
        var doc = RhinoDoc.ActiveDoc ?? throw new InvalidOperationException("No active document.");
        var obj = doc.Objects.FindId(id);
        var json = obj?.Attributes.GetUserString(RhinoManagedAnnotations.Key);
        if (json is null) throw new InvalidOperationException("This annotation has no managed geometry references.");
        var spec = JsonSerializer.Deserialize<SheetAnnotationSpecification>(json, SheetUpdatePlanner.Json)!;
        var originalView = doc.Views.ActiveView;
        var modelView = doc.Views.GetStandardRhinoViews().FirstOrDefault();
        if (modelView is not null) doc.Views.ActiveView = modelView;
        var anchors = new List<GeometryAnchor>();
        try
        {
            for (var i = 0; i < spec.Anchors.Count; i++)
            {
                using var get = new GetObject();
                get.SetCommandPrompt($"Select replacement source {i + 1} of {spec.Anchors.Count} (vertex or curve/edge)");
                get.SubObjectSelect = true; get.EnablePreSelect(false, true);
                if (get.Get() != GetResult.Object) return null;
                var reference = get.Object(0); var source = reference.Object(); var geometry = source.Geometry;
                var topology = RhinoManagedAnnotations.Topology(geometry); var picked = reference.SelectionPoint();
                var curve = reference.Edge() as Curve ?? geometry as Curve;
                if (curve is not null)
                {
                    if (!curve.ClosestPoint(picked, out var t)) throw new InvalidOperationException("Could not resolve the picked point.");
                    var length = curve.GetLength();
                    anchors.Add(new(source.Id, [], geometry is Brep ? "edge" : "curve", geometry is Brep ? reference.GeometryComponentIndex.Index : 0,
                        length > 0 ? curve.GetLength(new Interval(curve.Domain.T0, t)) / length : 0, topology));
                }
                else if (geometry is Brep b)
                {
                    var vertex = b.Vertices.OrderBy(v => v.Location.DistanceTo(picked)).First();
                    anchors.Add(new(source.Id, [], "vertex", vertex.VertexIndex, 0, topology));
                }
                else if (geometry is Mesh mesh)
                {
                    var vertex = Enumerable.Range(0, mesh.Vertices.Count).OrderBy(n => mesh.Vertices.Point3dAt(n).DistanceTo(picked)).First();
                    anchors.Add(new(source.Id, [], "vertex", vertex, 0, topology));
                }
                else throw new InvalidOperationException("Choose a Brep, mesh vertex, or curve/edge. For nested instances, use conversational repair with inspected references.");
            }
            return JsonSerializer.Serialize(spec with { AnnotationId = id, Anchors = anchors }, SheetUpdatePlanner.Json);
        }
        finally { if (originalView is not null) doc.Views.ActiveView = originalView; }
    }
}

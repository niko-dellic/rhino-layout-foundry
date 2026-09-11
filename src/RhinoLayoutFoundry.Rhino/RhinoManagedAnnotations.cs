using System.Text.Json;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoLayoutFoundry.Extensibility;
namespace RhinoLayoutFoundry.Rhino;

/// <summary>Document-owned references; updates require no AI session.</summary>
internal static class RhinoManagedAnnotations
{
    internal const string Key = "RhinoLayoutFoundry.Annotation.Reference";
    internal const string WarningKey = "RhinoLayoutFoundry.Annotation.Warning";
    internal const string FingerprintKey = "RhinoLayoutFoundry.Annotation.Measurement";
    private static readonly HashSet<uint> Pending = [];
    private static bool _running;
    internal static void Start() => RhinoApp.Idle += Idle;
    internal static void Stop() { RhinoApp.Idle -= Idle; Pending.Clear(); }
    internal static void Queue(RhinoDoc doc) { if (!_running) Pending.Add(doc.RuntimeSerialNumber); }
    private static void Idle(object? sender, EventArgs e)
    {
        if (_running) return;
        _running = true;
        try
        {
            foreach (var serial in Pending.ToArray())
            {
                Pending.Remove(serial);
                if (RhinoDoc.FromRuntimeSerialNumber(serial) is { } doc) Synchronize(doc);
            }
        }
        finally { _running = false; }
    }
    internal static string Topology(GeometryBase geometry) => geometry switch
    {
        Brep b => $"brep:{b.Vertices.Count}:{b.Edges.Count}:{b.Faces.Count}:" + string.Join(";", b.Edges.Select(e => $"{e.StartVertex.VertexIndex},{e.EndVertex.VertexIndex}")),
        Mesh m => $"mesh:{m.Vertices.Count}:{m.Faces.Count}:" + string.Join(";", m.Faces.Select(f => $"{f.A},{f.B},{f.C},{f.D}")),
        Curve c => $"curve:{c.Degree}:{c.IsClosed}:{c.SpanCount}",
        _ => geometry.ObjectType.ToString(),
    };
    internal static (GeometryBase Geometry, Transform Transform) ResolveGeometry(RhinoDoc doc, GeometryAnchor anchor)
    {
        var obj = doc.Objects.FindId(anchor.ObjectId) ?? throw new InvalidOperationException("Source geometry is missing; repair or remove this dimension.");
        var transform = Transform.Identity;
        foreach (var index in anchor.InstancePath)
        {
            if (obj is not InstanceObject instance) throw new InvalidOperationException("Instance reference changed.");
            var members = instance.InstanceDefinition.GetObjects();
            if (index < 0 || index >= members.Length) throw new InvalidOperationException("Instance member is missing.");
            transform *= instance.InstanceXform;
            obj = members[index];
        }
        var geometry = obj.Geometry;
        if (Topology(geometry) != anchor.Topology) throw new InvalidOperationException("Source topology changed; rebind this dimension.");
        return (geometry, transform);
    }
    internal static Point3d Resolve(RhinoDoc doc, GeometryAnchor anchor)
    {
        var (g, transform) = ResolveGeometry(doc, anchor);
        Point3d p = (g, anchor.Component) switch
        {
            (Brep b, "vertex") when anchor.Index >= 0 && anchor.Index < b.Vertices.Count => b.Vertices[anchor.Index].Location,
            (Brep b, "edge") when anchor.Index >= 0 && anchor.Index < b.Edges.Count => b.Edges[anchor.Index].PointAtNormalizedLength(anchor.Parameter),
            (Mesh m, "vertex") when anchor.Index >= 0 && anchor.Index < m.Vertices.Count => m.Vertices.Point3dAt(anchor.Index),
            (Curve c, "curve") => c.PointAtNormalizedLength(anchor.Parameter),
            _ => throw new InvalidOperationException("Unsupported or missing geometry reference."),
        };
        if (anchor.Parameter < 0 || anchor.Parameter > 1 || !p.IsValid) throw new InvalidOperationException("Invalid geometry parameter.");
        p.Transform(transform);
        return p;
    }
    internal static AnnotationBase Build(RhinoDoc doc, Guid sheetId, SheetAnnotationSpecification spec, Guid styleId, out string fingerprint)
    {
        var style = doc.DimStyles.FindId(styleId) ?? throw new InvalidOperationException("Dimension style is missing; select an installed style.");
        var page = doc.Views.GetPageViews().Single(p => p.MainViewport.Id == sheetId);
        var detail = spec.DetailId is { } id ? page.GetDetailViews().SingleOrDefault(d => d.Viewport.Id == id) : null;
        if (spec.Kind != "note" && detail is null) throw new InvalidOperationException("A source detail is required for this annotation.");
        var factor = RhinoMath.UnitScale(UnitSystem.Millimeters, doc.PageUnitSystem);
        var location = new Point3d(spec.Xmm * factor, spec.Ymm * factor, 0);
        var world = spec.Anchors.Select(a => Resolve(doc, a)).ToArray();
        var points = world.Select(p => { p.Transform(detail!.WorldToPageTransform); return new Point3d(p.X, p.Y, 0); }).ToArray();
        var styleHash = style.DataCRC(0);
        fingerprint = JsonSerializer.Serialize(new { points, world, location, styleId, styleHash, spec.Kind, spec.Text, scale = detail?.DetailGeometry.PageToModelRatio });
        AnnotationBase result;
        if (spec.Kind == "note") result = new TextEntity { Plane = new Plane(location, Vector3d.ZAxis), PlainText = spec.Text, DimensionStyleId = styleId };
        else if (spec.Kind == "leader")
        {
            if (points.Length != 1) throw new InvalidOperationException("A leader requires one measured anchor.");
            result = Leader.Create(spec.Text, Plane.WorldXY, style, [points[0], location]);
        }
        else
        {
            if (!detail!.Viewport.IsParallelProjection) throw new InvalidOperationException("Dimensions require a parallel detail.");
            var units = RhinoMath.UnitScale(doc.ModelUnitSystem, doc.PageUnitSystem);
            var ratio = detail.DetailGeometry.PageToModelRatio;
            if (ratio <= 0) throw new InvalidOperationException("Invalid detail scale.");
            if (spec.Kind is "linear" or "aligned")
            {
                if (points.Length != 2) throw new InvalidOperationException("Linear dimensions require two measured anchors.");
                var actual = world[0].DistanceTo(world[1]);
                var projected = points[0].DistanceTo(points[1]) / ratio;
                if (Math.Abs(actual - projected) > Math.Max(doc.ModelAbsoluteTolerance * 2, actual * 1e-6))
                    throw new InvalidOperationException("This view foreshortens the measurement. Use a true-length orthographic detail.");
                result = LinearDimension.Create(spec.Kind == "aligned" ? AnnotationType.Aligned : AnnotationType.Rotated,
                    style, Plane.WorldXY, Vector3d.XAxis, points[0], points[1], location, 0);
            }
            else if (spec.Kind == "angular")
            {
                if (points.Length != 3) throw new InvalidOperationException("Angular dimensions require center, first and second anchors.");
                var forward = detail.Viewport.CameraDirection; forward.Unitize();
                if (world.Skip(1).Any(p => Math.Abs((p - world[0]) * forward) > doc.ModelAbsoluteTolerance))
                    throw new InvalidOperationException("The angle is not in the view plane; use an orthographic detail.");
                result = AngularDimension.Create(style, Plane.WorldXY, Vector3d.XAxis, points[0], points[1], points[2], location);
            }
            else if (spec.Kind is "radius" or "diameter")
            {
                if (spec.Anchors.Count != 1) throw new InvalidOperationException("Radial dimensions require a circular edge/curve reference.");
                var a = spec.Anchors[0]; var (g, xform) = ResolveGeometry(doc, a);
                var curve = g is Curve c ? c.DuplicateCurve() : g is Brep b && a.Component == "edge" ? b.Edges[a.Index].DuplicateCurve() : null;
                using (curve)
                {
                    if (curve is null || !curve.Transform(xform) || !curve.TryGetCircle(out var circle)) throw new InvalidOperationException("Reference is not a circular edge/curve.");
                    var direction = detail.Viewport.CameraDirection; direction.Unitize();
                    if (Math.Abs(circle.Normal * direction) < 0.999999) throw new InvalidOperationException("Circle is foreshortened; use a face-on detail.");
                    var center = circle.Center; center.Transform(detail.WorldToPageTransform); center.Z = 0;
                    result = RadialDimension.Create(style, spec.Kind == "radius" ? AnnotationType.Radius : AnnotationType.Diameter,
                        Plane.WorldXY, center, points[0], location);
                }
            }
            else throw new InvalidOperationException("Unsupported dimension kind.");
            if (result is Dimension dimension) dimension.LengthFactor = style.LengthFactor / ratio;
        }
        if (result is null || !result.IsValid) throw new InvalidOperationException("Rhino rejected the annotation geometry.");
        result.DimensionStyleId = styleId;
        result.DimensionScale = RhinoMath.UnitScale(doc.ModelUnitSystem, doc.PageUnitSystem);
        return result;
    }
    internal static void Synchronize(RhinoDoc doc)
    {
        var changed = false;
        foreach (var obj in doc.Objects.Where(o => o.Geometry is AnnotationBase && o.Attributes.GetUserString(Key) is not null).ToArray())
        {
            var attributes = obj.Attributes.Duplicate();
            var objectChanged = false;
            try
            {
                var spec = JsonSerializer.Deserialize<SheetAnnotationSpecification>(attributes.GetUserString(Key)!, SheetUpdatePlanner.Json)!;
                using var geometry = Build(doc, attributes.ViewportId, spec, ((AnnotationBase)obj.Geometry).DimensionStyleId, out var fingerprint);
                if (fingerprint != attributes.GetUserString(FingerprintKey))
                {
                    if (!doc.Objects.Replace(obj.Id, geometry, true)) throw new InvalidOperationException("Could not update annotation.");
                    attributes.SetUserString(FingerprintKey, fingerprint); objectChanged = true;
                }
                if (attributes.GetUserString(WarningKey) is not null) { attributes.SetUserString(WarningKey, null); objectChanged = true; }
            }
            catch (Exception exception)
            {
                if (attributes.GetUserString(WarningKey) == exception.Message) continue;
                attributes.SetUserString(WarningKey, exception.Message); objectChanged = true;
            }
            if (objectChanged) { doc.Objects.ModifyAttributes(obj.Id, attributes, true); changed = true; }
        }
        if (changed) doc.Views.Redraw();
    }
    internal static string Inspect(RhinoDoc doc, IReadOnlyList<Guid> ids)
    {
        var objects = new List<object>();
        void Visit(RhinoObject obj, Guid root, int[] path, Transform transform)
        {
            if (objects.Count >= 200 || path.Length > 16) return;
            if (obj is InstanceObject instance)
            {
                var members = instance.InstanceDefinition.GetObjects();
                for (var i = 0; i < members.Length; i++) Visit(members[i], root, [..path, i], transform * instance.InstanceXform);
                return;
            }
            var g = obj.Geometry; var topology = Topology(g); var anchors = new List<object>();
            void Anchor(string component, int index, double parameter)
            {
                var a = new GeometryAnchor(root, path, component, index, parameter, topology);
                try { anchors.Add(new { reference = a, point = Resolve(doc, a) }); } catch (InvalidOperationException) { }
            }
            if (g is Brep b)
            {
                for (var i = 0; i < Math.Min(100, b.Vertices.Count); i++) Anchor("vertex", i, 0);
                for (var i = 0; i < Math.Min(100, b.Edges.Count); i++) Anchor("edge", i, 0.5);
            }
            else if (g is Mesh m) for (var i = 0; i < Math.Min(200, m.Vertices.Count); i++) Anchor("vertex", i, 0);
            else if (g is Curve) foreach (var t in new[] { 0d, .5, 1d }) Anchor("curve", 0, t);
            objects.Add(new { object_id = root, instance_path = path, obj.Name, geometry_type = g.ObjectType.ToString(), anchors, truncated = anchors.Count >= 100 });
        }
        foreach (var obj in doc.Objects.Where(o => o.Attributes.Space == ActiveSpace.ModelSpace && !o.IsHidden && (ids.Count == 0 || ids.Contains(o.Id))).Take(200))
            Visit(obj, obj.Id, [], Transform.Identity);
        return JsonSerializer.Serialize(new { units = doc.ModelUnitSystem.ToString(), objects,
            dimension_styles = doc.DimStyles.Where(s => !s.IsDeleted).Select(s => new { id = s.Id, s.Name, current = s.Id == doc.DimStyles.Current.Id }),
            annotations = doc.Objects.Where(o => o.Geometry is AnnotationBase && o.Attributes.Space == ActiveSpace.PageSpace).Select(o => new
            { id = o.Id, sheet_id = o.Attributes.ViewportId, style_id = ((AnnotationBase)o.Geometry).DimensionStyleId,
                reference = o.Attributes.GetUserString(Key), warning = o.Attributes.GetUserString(WarningKey), has_overrides = ((AnnotationBase)o.Geometry).HasPropertyOverrides }),
            limitations = "References describe measured components, not design intent. Edge/curve parameters are normalized arc length. Topology changes invalidate references. Lists are bounded; request specific object IDs to inspect further." }, SheetUpdatePlanner.Json);
    }
}

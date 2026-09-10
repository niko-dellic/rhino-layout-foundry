using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Rhino;

/// <summary>Owns only explicitly tagged captions. Never changes detail geometry or projection.</summary>
internal sealed class RhinoDetailCaptionService(DocumentStateStore store) : IDisposable
{
    private DateTime _nextCheck;
    private bool _updating;
    public void Start()
    {
        RhinoApp.Idle += OnIdle;
        Command.EndCommand += OnCommandEnded;
    }
    public void Dispose()
    {
        RhinoApp.Idle -= OnIdle;
        Command.EndCommand -= OnCommandEnded;
    }
    private void OnCommandEnded(object? sender, CommandEventArgs args) => _nextCheck = DateTime.MinValue;
    private void OnIdle(object? sender, EventArgs args)
    {
        if (_updating || RhinoApp.InCommand > 0 || DateTime.UtcNow < _nextCheck || RhinoDoc.ActiveDoc is not { } doc
            || doc.UndoActive || doc.RedoActive || !store.CanWrite(doc)) return;
        _nextCheck = DateTime.UtcNow.AddMilliseconds(500);
        _updating = true;
        try { Synchronize(doc, store.Get(doc)); }
        catch (Exception error) { _nextCheck = DateTime.UtcNow.AddSeconds(30); RhinoApp.WriteLine("Detail captions: " + error.Message); }
        finally { _updating = false; }
    }

    internal static void Mark(DetailViewObject detail)
    {
        detail.Attributes.SetUserString(DetailCaptions.ManagedKey, "1");
        detail.Attributes.SetUserString(DetailCaptions.SourceViewportKey, detail.Viewport.Id.ToString("D"));
        if (!detail.CommitChanges()) throw new InvalidOperationException("Could not register the detail caption.");
    }

    internal static void Synchronize(RhinoDoc doc, DocumentState state)
    {
        // Derived maintenance must not introduce independent undo steps. Explicit
        // caption-setting transactions retain normal undo recording.
        var recording = doc.UndoRecordingEnabled;
        try
        {
            if (doc.CurrentUndoRecordSerialNumber == 0) doc.UndoRecordingEnabled = false;
            SynchronizeCore(doc, state);
        }
        finally { doc.UndoRecordingEnabled = recording; }
    }

    private static void SynchronizeCore(RhinoDoc doc, DocumentState state)
    {
        var folders = state.Folders.ToDictionary(f => f.Id);
        var captions = doc.Objects.GetObjectList(ObjectType.Annotation).Where(o => o.Attributes.GetUserString(DetailCaptions.OwnerKey) is not null).ToArray();
        var owners = new HashSet<Guid>();
        var changed = false;
        var factor = RhinoMath.UnitScale(UnitSystem.Millimeters, doc.PageUnitSystem);
        foreach (var page in doc.Views.GetPageViews())
        foreach (var detail in page.GetDetailViews())
        {
            if (detail.Attributes.GetUserString(DetailCaptions.ManagedKey) != "1") continue;
            owners.Add(detail.Id);
            var visible = DetailCaptions.Resolve(state.Metadata, folders,
                state.Sheets.GetValueOrDefault(page.MainViewport.Id)?.FolderId ?? state.RootFolderId,
                page.MainViewport.Id, detail.Viewport.Id, detail.Viewport.IsParallelProjection);
            var text = DetailCaptions.Text(detail.Attributes.Name ?? detail.DescriptiveTitle, detail.Id, visible);
            var matches = captions.Where(o => o.Attributes.GetUserString(DetailCaptions.OwnerKey) == detail.Id.ToString("D")
                && o.Attributes.ViewportId == page.MainViewport.Id).ToArray();
            var existing = matches.FirstOrDefault();
            foreach (var duplicate in matches.Skip(1)) changed |= doc.Objects.Delete(duplicate, true);
            var bounds = detail.Geometry.GetBoundingBox(true);
            if (!bounds.IsValid) continue;
            var origin = new Point3d(bounds.Min.X, bounds.Min.Y - 2 * factor, 0);
            // Retain an object/link when hidden; Rhino cannot add a truly empty text entity.
            var content = text.Length == 0 ? " " : text;
            using var entity = TextEntity.Create(content, new Plane(origin, Vector3d.XAxis, Vector3d.YAxis),
                doc.DimStyles.Current, false, 0, 0);
            if (entity is null) throw new InvalidOperationException("Could not create linked detail text.");
            entity.TextHeight = 2.5 * factor;
            entity.TextHorizontalAlignment = TextHorizontalAlignment.Left;
            entity.TextVerticalAlignment = TextVerticalAlignment.Top;
            var attributes = existing?.Attributes.Duplicate() ?? new ObjectAttributes
            {
                Space = ActiveSpace.PageSpace, ViewportId = page.MainViewport.Id,
                LayerIndex = detail.Attributes.LayerIndex, ColorSource = ObjectColorSource.ColorFromObject,
                ObjectColor = System.Drawing.Color.Black
            };
            attributes.SetUserString(DetailCaptions.OwnerKey, detail.Id.ToString("D"));
            attributes.Visible = text.Length > 0;
            var captionBounds = entity.GetBoundingBox(true);
            var overflow = origin.X < 0 || captionBounds.Min.Y < 0 || captionBounds.Max.X > page.PageWidth
                || captionBounds.Max.X > bounds.Max.X;
            var warning = overflow ? "Caption exceeds its detail width or page boundary." : null;
            attributes.SetUserString("RhinoLayoutFoundry.Caption.Warning", warning);
            if (existing is null)
            {
                if (doc.Objects.AddText(entity, attributes) == Guid.Empty) throw new InvalidOperationException("Could not add linked detail caption.");
                changed = true;
            }
            else
            {
                // Stored source rather than evaluated field text makes this stable across scale changes.
                if (existing.Geometry is not TextEntity old || old.PlainTextWithFields != entity.PlainTextWithFields
                    || old.Plane.Origin.DistanceTo(origin) > 1e-8 || Math.Abs(old.TextHeight - entity.TextHeight) > 1e-8)
                    changed |= doc.Objects.Replace(existing.Id, entity);
                if (existing.Attributes.Visible != attributes.Visible
                    || existing.Attributes.GetUserString("RhinoLayoutFoundry.Caption.Warning") != warning)
                    changed |= doc.Objects.ModifyAttributes(existing.Id, attributes, true);
            }
        }
        foreach (var caption in captions)
            if (!Guid.TryParse(caption.Attributes.GetUserString(DetailCaptions.OwnerKey), out var owner) || !owners.Contains(owner)
                || doc.Objects.FindId(owner)?.Attributes.ViewportId != caption.Attributes.ViewportId)
                changed |= doc.Objects.Delete(caption, true);
        if (changed) doc.Views.Redraw();
    }
}

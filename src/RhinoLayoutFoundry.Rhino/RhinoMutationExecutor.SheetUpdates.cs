using System.Text.Json;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;
using RhinoLayoutFoundry.Extensibility;
namespace RhinoLayoutFoundry.Rhino;
internal sealed partial class RhinoMutationExecutor
{
    private OperationResult ApplySheetUpdates(RhinoDoc document, OperationPlan plan, UpdateSheetsChange change)
    {
        var spec = change.Specification;
        if (change.Digest != SheetUpdatePlanner.Digest(spec)) return Failure("sheet_update.changed", "Approved proposal changed.");
        var check = SheetUpdatePlanner.Plan(spec, new RhinoDocumentSnapshotProvider(_stateStore, _revisionTracker).Capture());
        if (!check.CanApply) return new(false, check.Diagnostics);
        var originalState = _stateStore.Get(document);
        var before = WithCurrentPageRecords(document, originalState);
        var targets = spec.Sheets.Select(s => s.SheetId).ToHashSet();
        var originals = document.Objects.Where(o => o.Attributes.Space == ActiveSpace.PageSpace && targets.Contains(o.Attributes.ViewportId))
            .ToDictionary(o => o.Id, o => (Object: o, Geometry: o.Geometry.Duplicate(), Attributes: o.Attributes.Duplicate()));
        var cameras = document.Views.GetPageViews().Where(p => targets.Contains(p.MainViewport.Id)).SelectMany(p => p.GetDetailViews())
            .ToDictionary(d => d.Viewport.Id, d => new ViewportInfo(d.Viewport));
        var created = new List<Guid>(); var clips = new List<Guid>(); var names = new List<string>(); var renamed = new List<NamedViewRename>();
        var layersBefore = new Dictionary<Guid, Layer>();
        var replacedViews = new List<ViewInfo>();
        var undo = document.BeginUndoRecord(plan.UndoDescription);
        if (undo == 0) return Failure("operation.undo_unavailable", "Could not start an update undo record.");
        var factor = RhinoMath.UnitScale(UnitSystem.Millimeters, document.PageUnitSystem);
        var modelFactor = RhinoMath.UnitScale(UnitSystem.Millimeters, document.ModelUnitSystem);
        try
        {
            var sheets = before.Sheets.ToDictionary(p => p.Key, p => p.Value);
            var metadata = before.Metadata.ToDictionary(p => p.Key, p => p.Value);
            foreach (var update in spec.Sheets)
            {
                var page = document.Views.GetPageViews().Single(p => p.MainViewport.Id == update.SheetId);
                var sheet = sheets[update.SheetId];
                var assignments = ResolveManagedAssignments(document, before, sheet).ToDictionary(p => p.Key, p => p.Value);
                foreach (var patch in update.Details)
                {
                    var v = patch.View; var bounds = v.BoundsMm;
                    var detail = patch.DetailId is { } id ? page.GetDetailViews().Single(d => d.Viewport.Id == id) :
                        page.AddDetailView(v.Name, new(bounds.Left * factor, bounds.Bottom * factor), new(bounds.Right * factor, bounds.Top * factor), DefinedViewportProjection.Top)
                        ?? throw new InvalidOperationException("Could not add detail.");
                    var viewportId = detail.Viewport.Id;
                    if (patch.DetailId is null) created.Add(detail.Id);
                    var mode = patch.DisplayModeId is { } modeId ? DisplayModeDescription.GetDisplayMode(modeId) : detail.Viewport.DisplayMode;
                    if (mode is null) throw new InvalidOperationException("Display mode unavailable.");
                    detail.DetailGeometry.IsProjectionLocked = false;
                    if (patch.DetailId is not null)
                    {
                        var box = detail.Geometry.GetBoundingBox(true);
                        var transform = Transform.Translation(new Vector3d(bounds.Left * factor, bounds.Bottom * factor, 0)) *
                            Transform.Scale(Plane.WorldXY, (bounds.Right - bounds.Left) * factor / (box.Max.X - box.Min.X),
                                (bounds.Top - bounds.Bottom) * factor / (box.Max.Y - box.Min.Y), 1) * Transform.Translation(-box.Min.X, -box.Min.Y, 0);
                        if (document.Objects.Transform(detail.Id, transform, true) == Guid.Empty) throw new InvalidOperationException("Could not resize detail frame.");
                        detail = page.GetDetailViews().Single(d => d.Viewport.Id == viewportId);
                        detail.DetailGeometry.IsProjectionLocked = false;
                    }
                    detail.Viewport.SetCameraLocations(Point(v.CameraTarget), Point(v.CameraLocation)); detail.Viewport.CameraUp = Vector(v.CameraUp);
                    if (!detail.Viewport.ChangeToParallelProjection(true) || !detail.CommitViewportChanges() ||
                        !detail.DetailGeometry.SetScale(v.ScaleDenominator * modelFactor, document.ModelUnitSystem, factor, document.PageUnitSystem))
                        throw new InvalidOperationException("Could not apply camera/scale.");
                    detail.Attributes.Name = v.Name; detail.DetailGeometry.IsProjectionLocked = true;
                    if (!detail.CommitChanges()) throw new InvalidOperationException("Could not commit detail.");
                    detail = page.GetDetailViews().Single(d => d.Viewport.Id == viewportId);
                    detail.Viewport.DisplayMode = mode;
                    if (!detail.CommitViewportChanges()) throw new InvalidOperationException("Could not apply display mode.");
                    RhinoDetailCaptionService.Mark(document, detail);
                    foreach (var layerId in v.HiddenLayerIds ?? [])
                    {
                        var source = document.Layers.FindId(layerId) ?? throw new InvalidOperationException("A visibility layer disappeared.");
                        if (!layersBefore.ContainsKey(layerId)) layersBefore[layerId] = CopyLayer(source);
                        var layer = CopyLayer(source);
                        layer.SetPerViewportVisible(viewportId, false); layer.SetPerViewportPersistentVisibility(viewportId, false);
                        if (!document.Layers.Modify(layer, source.Index, true)) throw new InvalidOperationException("Could not apply detail visibility.");
                    }
                    // Preserve unrelated clipping planes; replace only this detail's managed cut.
                    foreach (var old in document.Objects.FindClippingPlanesForViewport(detail.Viewport).Where(c =>
                        c.Attributes.GetUserString("RhinoLayoutFoundry.Automation.SessionId") is not null))
                    {
                        if (old.ClippingPlaneGeometry.ViewportIds().Length != 1) throw new InvalidOperationException("The cut is shared; edit its scope separately.");
                        if (!originals.ContainsKey(old.Id)) originals.Add(old.Id, (old, old.Geometry.Duplicate(), old.Attributes.Duplicate()));
                        if (!document.Objects.Delete(old.Id, true)) throw new InvalidOperationException("Could not replace cut.");
                    }
                    if (v.Cut is { } cut)
                    {
                        var attrs = new ObjectAttributes { Name = v.Name + " cut", LayerIndex = EnsureClippingLayer(document) };
                        attrs.SetUserString("RhinoLayoutFoundry.Automation.SessionId", spec.ProposalId.ToString("D"));
                        var clip = document.Objects.AddClippingPlane(new Plane(Point(cut.Origin), Vector(cut.Normal)), 1000 * modelFactor, 1000 * modelFactor, [viewportId], attrs);
                        if (clip == Guid.Empty) throw new InvalidOperationException("Could not create cut.");
                        created.Add(clip); clips.Add(clip);
                    }
                    var ownedView = metadata.Where(p => p.Key.StartsWith("RhinoLayoutFoundry.ManagedView.", StringComparison.Ordinal)).Select(p =>
                    {
                        try { using var json = JsonDocument.Parse(p.Value); return json.RootElement.TryGetProperty("detail_id", out var item) && item.TryGetGuid(out var did) && did == viewportId
                            ? document.NamedViews.FirstOrDefault(n => p.Key.EndsWith(n.NamedViewId.ToString("D"), StringComparison.OrdinalIgnoreCase)) : null; }
                        catch (JsonException) { return null; }
                    }).FirstOrDefault(n => n is not null);
                    var name = ownedView?.Name ?? ReadableViewName(document, page.PageName + " — " + v.Name);
                    if (ownedView is not null) replacedViews.Add(ownedView);
                    using var saved = new ViewInfo(detail.Viewport) { Name = name };
                    var index = document.NamedViews.Add(saved);
                    if (index < 0) throw new InvalidOperationException("Could not save named view.");
                    if (ownedView is null) names.Add(name);
                    else metadata.Remove("RhinoLayoutFoundry.ManagedView." + ownedView.NamedViewId.ToString("D"));
                    assignments[viewportId] = name;
                    metadata["RhinoLayoutFoundry.ManagedView." + document.NamedViews[index].NamedViewId.ToString("D")] = JsonSerializer.Serialize(new
                        { proposal_id = spec.ProposalId, sheet_id = update.SheetId, detail_id = viewportId, logical_key = v.Key, name });
                }
                var role = sheet.TitleBlock;
                if (update.TitleBlock is not null)
                {
                    if (role is not null && !document.Objects.Delete(role.InstanceObjectId, true)) throw new InvalidOperationException("Could not replace the managed titleblock.");
                    role = null;
                    if (update.TitleBlock != "off")
                    {
                        var kind = update.TitleBlock == "right" ? BuiltInTitleBlockKind.RightSidebar : BuiltInTitleBlockKind.FullWidthBottom;
                        var paper = new PaperRecipe(page.PageWidth, page.PageHeight, document.PageUnitSystem.ToString());
                        var layout = AdaptiveTitleBlockLayoutSolver.Solve(kind, paper, before.ProjectInfo, page.GetDetailViews().Length, null);
                        foreach (var detail in page.GetDetailViews())
                        {
                            var box = detail.Geometry.GetBoundingBox(true); var c = layout.Content;
                            if (box.Min.X < c.Left || box.Max.X > c.Right || box.Min.Y - 8 * factor < c.Bottom || box.Max.Y > c.Top)
                                throw new InvalidOperationException("Propose a scale-preserving rearrangement of the detail frames before inserting this titleblock.");
                        }
                        var blockId = CreateManagedTitleBlock(document, page, paper, kind, before.ProjectInfo, sheet.TitleBlockData ?? new(string.Empty, []),
                            page.GetDetailViews().Select(d => CaptureDetail(document, d)).ToArray(), null);
                        created.Add(blockId);
                        role = new(blockId, ((InstanceObject)document.Objects.FindId(blockId)).InstanceDefinition.Id, kind);
                    }
                }
                var sheetMetadata = sheet.Metadata.ToDictionary(p => p.Key, p => p.Value);
                if (update.DimensionStyleId is { } styleId)
                {
                    if (document.DimStyles.FindId(styleId) is null) throw new InvalidOperationException("Dimension style missing.");
                    foreach (var obj in document.Objects.Where(o => o.Attributes.Space == ActiveSpace.PageSpace && o.Attributes.ViewportId == update.SheetId && o.Geometry is Dimension).ToArray())
                    {
                        using var geometry = (Dimension)obj.Geometry.Duplicate(); geometry.ClearPropertyOverrides(); geometry.DimensionStyleId = styleId;
                        if (!document.Objects.Replace(obj.Id, geometry, true)) throw new InvalidOperationException("Could not apply dimension style.");
                        if (obj.Attributes.GetUserString(RhinoManagedAnnotations.Key) is not null)
                        {
                            var attrs = obj.Attributes.Duplicate(); attrs.SetUserString(RhinoManagedAnnotations.FingerprintKey, null);
                            if (!document.Objects.ModifyAttributes(obj.Id, attrs, true)) throw new InvalidOperationException("Could not update managed style metadata.");
                        }
                    }
                    sheetMetadata["RhinoLayoutFoundry.DimensionStyle"] = styleId.ToString("D");
                }
                var defaultStyle = sheetMetadata.TryGetValue("RhinoLayoutFoundry.DimensionStyle", out var styleValue) && Guid.TryParse(styleValue, out var sheetStyle)
                    ? sheetStyle : document.DimStyles.Current.Id;
                foreach (var annotation in update.Annotations)
                {
                    var existing = annotation.AnnotationId is { } id ? document.Objects.FindId(id) : null;
                    if (annotation.AnnotationId is not null && (existing?.Geometry is not AnnotationBase || existing.Attributes.ViewportId != update.SheetId))
                        throw new InvalidOperationException("Target annotation is missing or belongs to another sheet.");
                    var style = annotation.StyleId ?? (existing?.Geometry as AnnotationBase)?.DimensionStyleId ?? defaultStyle;
                    using var geometry = RhinoManagedAnnotations.Build(document, update.SheetId, annotation, style, out var fingerprint);
                    var attributes = existing?.Attributes.Duplicate() ?? new ObjectAttributes { Space = ActiveSpace.PageSpace, ViewportId = update.SheetId, Name = annotation.Key };
                    attributes.SetUserString(RhinoManagedAnnotations.Key, JsonSerializer.Serialize(annotation, SheetUpdatePlanner.Json));
                    attributes.SetUserString(RhinoManagedAnnotations.FingerprintKey, fingerprint);
                    attributes.SetUserString(RhinoManagedAnnotations.WarningKey, null);
                    if (existing is not null)
                    {
                        if (!document.Objects.Replace(existing.Id, geometry, true) || !document.Objects.ModifyAttributes(existing.Id, attributes, true)) throw new InvalidOperationException("Could not update annotation.");
                    }
                    else
                    {
                        var added = document.Objects.Add(geometry, attributes);
                        if (added == Guid.Empty) throw new InvalidOperationException("Could not add annotation.");
                        created.Add(added);
                    }
                }
                foreach (var id in update.RemoveAnnotationIds)
                {
                    var obj = document.Objects.FindId(id);
                    if (obj?.Geometry is not AnnotationBase || obj.Attributes.ViewportId != update.SheetId || !document.Objects.Delete(id, true))
                        throw new InvalidOperationException("Cannot remove an annotation outside the selected sheet.");
                }
                sheets[update.SheetId] = sheet with { DetailNamedViews = assignments, TitleBlock = role, Metadata = sheetMetadata };
            }
            foreach (var rename in spec.RenameViews)
            {
                if (!document.NamedViews.Rename(rename.OldName, rename.NewName)) throw new InvalidOperationException("Could not rename named view.");
                renamed.Add(rename);
                foreach (var pair in sheets.ToArray()) sheets[pair.Key] = pair.Value with
                    { DetailNamedViews = pair.Value.DetailNamedViews.ToDictionary(p => p.Key, p => p.Value == rename.OldName ? rename.NewName : p.Value) };
            }
            metadata[DrawingSetPlanner.ReceiptKey(spec.ProposalId)] = JsonSerializer.Serialize(new
                { digest = change.Digest, resources = spec.Sheets.ToDictionary(s => s.SheetId.ToString(), s => s.SheetId),
                    clipping_plane_ids = clips, named_views = names, annotation_ids = created });
            _stateStore.Set(document, before with { Sheets = sheets, Metadata = metadata });
            if (!document.AddCustomUndoEvent(plan.UndoDescription, OnUndoDocumentState, new DocumentStateUndoTag(plan.UndoDescription, originalState)))
                throw new InvalidOperationException("Could not register metadata Undo.");
            RhinoManagedAnnotations.Queue(document);
            _revisionTracker.Bump(document); document.Views.Redraw(); _overviewChanged(OverviewInvalidation.All);
            return SuccessWithEntity(plan, "sheet_update.applied", "Updated the selected sheets. Inspect the resulting drawings and annotation warnings.");
        }
        catch (Exception exception)
        {
            var failures = new List<string>();
            foreach (var id in created.AsEnumerable().Reverse()) if (!document.Objects.Delete(id, true)) failures.Add(id.ToString());
            foreach (var pair in originals)
            {
                try
                {
                    if (document.Objects.FindId(pair.Key) is null) document.Objects.Undelete(pair.Value.Object);
                    else if (pair.Value.Object is not ClippingPlaneObject) document.Objects.Replace(pair.Key, pair.Value.Geometry, true);
                    document.Objects.ModifyAttributes(pair.Key, pair.Value.Attributes, true);
                }
                catch { failures.Add(pair.Key.ToString()); }
            }
            foreach (var detail in document.Views.GetPageViews().SelectMany(p => p.GetDetailViews()))
                if (cameras.TryGetValue(detail.Viewport.Id, out var camera)) { detail.Viewport.SetViewProjection(camera, true); detail.CommitViewportChanges(); }
            foreach (var layer in layersBefore.Values)
                if (!document.Layers.Modify(layer, layer.Index, true)) failures.Add("layer " + layer.Id);
            foreach (var name in names) document.NamedViews.Delete(name);
            foreach (var view in replacedViews) if (document.NamedViews.Add(view) < 0) failures.Add("named view " + view.Name);
            foreach (var rename in renamed.AsEnumerable().Reverse()) document.NamedViews.Rename(rename.NewName, rename.OldName);
            _stateStore.Set(document, originalState);
            return Failure("sheet_update.failed", exception.Message + (failures.Count == 0 ? " Original sheet content restored." : " Rollback needs review: " + string.Join(", ", failures)));
        }
        finally
        {
            foreach (var item in originals.Values) item.Geometry.Dispose();
            foreach (var camera in cameras.Values) camera.Dispose();
            foreach (var layer in layersBefore.Values) layer.Dispose();
            foreach (var view in replacedViews) view.Dispose();
            document.EndUndoRecord(undo);
        }
    }
}

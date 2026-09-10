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
    private OperationResult ApplyDrawingSet(RhinoDoc document, OperationPlan plan, CreateDrawingSetChange change)
    {
        var spec = change.Specification;
        var before = _stateStore.Get(document);
        if (RhinoDoc.ActiveDoc?.RuntimeSerialNumber != document.RuntimeSerialNumber)
            return Failure("drawing_set.document_changed", "The active document changed.");
        var validation = DrawingSetSpecificationValidator.Validate(spec,
            new RhinoDocumentSnapshotProvider(_stateStore, _revisionTracker).Capture());
        if (!validation.IsValid)
            return Failure("drawing_set.invalid", string.Join("; ", validation.Issues.Select(i => i.Message)));
        if (change.Digest != DrawingSetPlanner.Digest(spec))
            return Failure("drawing_set.digest_mismatch", "The approved drawing set was altered.");
        if (before.Metadata.ContainsKey(DrawingSetPlanner.ReceiptKey(spec.ProposalId)))
            return Failure("drawing_set.already_applied", "This proposal has already been applied.");
        // Do not let inherited unitless documents silently produce a physically wrong scale.
        if (document.ModelUnitSystem is UnitSystem.None or UnitSystem.CustomUnits ||
            document.PageUnitSystem is UnitSystem.None or UnitSystem.CustomUnits)
            return Failure("drawing_set.units_required", "Set standard model and page units before creating scaled drawings.");
        if (!before.Folders.Any(f => f.Id == spec.DestinationFolderId))
            return Failure("drawing_set.folder_missing", "The destination folder no longer exists.");
        if (spec.NewDestinationFolderName is { } folderName && before.Folders.Any(f => f.ParentId == spec.DestinationFolderId && string.Equals(f.Name, folderName.Trim(), StringComparison.OrdinalIgnoreCase)))
            return Failure("drawing_set.folder_conflict", "The proposed destination folder already exists. Re-inspect before continuing.");
        var names = document.Views.GetPageViews().Select(p => p.PageName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (spec.Sheets.Any(s => !names.Add(s.Name.Trim())))
            return Failure("drawing_set.name_conflict", "A proposed sheet name already exists.");
        var mode = FoundryDrawingDisplayMode.GetOrCreate();
        if (mode is null) return Failure("drawing_set.presentation_missing", "The Foundry drawing display mode could not be initialized.");
        var pages = new List<RhinoPageView>();
        var clips = new List<Guid>();
        var namedViews = new List<string>();
        var annotations = new List<Guid>();
        var hiddenScopes = new List<(Guid LayerId, Guid ViewportId)>();
        var presentationLayerIndex = -1;
        var resources = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var active = document.Views.ActiveView;
        var pageFactor = RhinoMath.UnitScale(UnitSystem.Millimeters, document.PageUnitSystem);
        var modelFactor = RhinoMath.UnitScale(UnitSystem.Millimeters, document.ModelUnitSystem);
        try
        {
            if (spec.SchemaVersion == 2)
            {
                presentationLayerIndex = document.Layers.Add(new Layer
                {
                    Name = $"Foundry Sheets {spec.ProposalId:N}",
                    Color = System.Drawing.Color.Black,
                });
                if (presentationLayerIndex < 0) throw new InvalidOperationException("Could not create the drawing presentation layer.");
            }
            var sheets = before.Sheets.ToDictionary(p => p.Key, p => p.Value);
            var folders = before.Folders.ToList();
            var destinationId = spec.DestinationFolderId;
            if (spec.NewDestinationFolderName is { } newName)
            {
                destinationId = Guid.NewGuid();
                folders.Add(new FolderRecord(destinationId, spec.DestinationFolderId, newName.Trim(),
                    folders.Where(f => f.ParentId == spec.DestinationFolderId).Select(f => f.Order).DefaultIfEmpty(-1).Max() + 1));
            }
            var order = sheets.Values.Where(s => s.FolderId == destinationId)
                .Select(s => s.Order).DefaultIfEmpty(-1).Max() + 1;
            foreach (var sheet in spec.Sheets)
            {
                var page = document.Views.AddPageView(sheet.Name.Trim(), sheet.WidthMm * pageFactor, sheet.HeightMm * pageFactor)
                    ?? throw new InvalidOperationException($"Could not create {sheet.Name}.");
                pages.Add(page);
                resources.Add(sheet.Key, page.MainViewport.Id);
                if (spec.SchemaVersion == 2)
                    annotations.Add(AddDrawingSetCaption(document, page, spec.ProposalId, sheet.Name.Trim(),
                        10 * pageFactor, (sheet.HeightMm - 8) * pageFactor, 3.5 * pageFactor,
                        (sheet.WidthMm - 20) * pageFactor, presentationLayerIndex));
                var assignments = new Dictionary<Guid, string>();
                foreach (var view in sheet.Views)
                {
                    var b = view.BoundsMm;
                    var detail = page.AddDetailView(view.Name.Trim(), new Point2d(b.Left * pageFactor, b.Bottom * pageFactor),
                        new Point2d(b.Right * pageFactor, b.Top * pageFactor), DefinedViewportProjection.Top)
                        ?? throw new InvalidOperationException($"Could not create {view.Name}.");
                    var viewportId = detail.Viewport.Id;
                    resources.Add(view.Key, viewportId);
                    detail.Viewport.SetCameraLocations(Point(view.CameraTarget), Point(view.CameraLocation));
                    detail.Viewport.CameraUp = Vector(view.CameraUp);
                    if (!detail.Viewport.ChangeToParallelProjection(true) || !detail.CommitViewportChanges())
                        throw new InvalidOperationException($"Could not set camera for {view.Name}.");
                    if (!detail.DetailGeometry.SetScale(view.ScaleDenominator * modelFactor, document.ModelUnitSystem,
                            pageFactor, document.PageUnitSystem))
                        throw new InvalidOperationException($"Could not set scale for {view.Name}.");
                    detail.Attributes.Name = view.Name.Trim();
                    if (presentationLayerIndex >= 0) detail.Attributes.LayerIndex = presentationLayerIndex;
                    detail.Attributes.SetUserString("RhinoLayoutFoundry.DrawingSet.ProposalId", spec.ProposalId.ToString("D"));
                    detail.DetailGeometry.IsProjectionLocked = true;
                    if (!detail.CommitChanges()) throw new InvalidOperationException("Could not commit detail geometry.");
                    // Rhino replaces details during geometry commits: never use the stale viewport afterward.
                    detail = page.GetDetailViews().Single(d => d.Viewport.Id == viewportId);
                    detail.Viewport.DisplayMode = mode;
                    if (!detail.CommitViewportChanges()) throw new InvalidOperationException("Could not set drawing presentation.");
                    var ratio = pageFactor / (view.ScaleDenominator * modelFactor);
                    detail = page.GetDetailViews().Single(d => d.Viewport.Id == viewportId);
                    if (!detail.DetailGeometry.IsParallelProjection || !detail.DetailGeometry.IsProjectionLocked ||
                        Math.Abs(detail.DetailGeometry.PageToModelRatio - ratio) > Math.Abs(ratio) * 1e-8 ||
                        detail.Viewport.DisplayMode.Id != mode.Id)
                        throw new InvalidOperationException($"Native scale/projection verification failed for {view.Name}.");
                    var viewName = $"Foundry-{spec.ProposalId:N}-{view.Key}";
                    if (document.NamedViews.FindByName(viewName) >= 0)
                        throw new InvalidOperationException("A generated named-view identity already exists.");
                    using var saved = new ViewInfo(detail.Viewport) { Name = viewName };
                    if (document.NamedViews.Add(saved) < 0) throw new InvalidOperationException("Could not save named view.");
                    namedViews.Add(viewName);
                    assignments.Add(viewportId, viewName);
                    foreach (var layerId in view.HiddenLayerIds ?? [])
                    {
                        var sourceLayer = document.Layers.FindId(layerId)
                            ?? throw new InvalidOperationException("A layer in the approved visibility scope disappeared.");
                        var layer = CopyLayer(sourceLayer);
                        // Track before committing: even an uncertain commit must be cleaned up.
                        hiddenScopes.Add((layerId, viewportId));
                        layer.SetPerViewportVisible(viewportId, false);
                        layer.SetPerViewportPersistentVisibility(viewportId, false);
                        if (!document.Layers.Modify(layer, sourceLayer.Index, quiet: true) ||
                            document.Layers[sourceLayer.Index].PerViewportIsVisible(viewportId))
                            throw new InvalidOperationException($"Could not hide layer '{sourceLayer.FullPath}' in {view.Name}.");
                    }
                    RhinoDetailCaptionService.Mark(detail);
                    if (view.Cut is { } cut)
                    {
                        var plane = new Plane(Point(cut.Origin), Vector(cut.Normal));
                        var attributes = new ObjectAttributes { Name = $"{view.Name} cut" };
                        attributes.SetUserString("RhinoLayoutFoundry.Automation.SessionId", spec.ProposalId.ToString("D"));
                        attributes.SetUserString("RhinoLayoutFoundry.Automation.Kind", "ClippingPlane");
                        var clipId = document.Objects.AddClippingPlane(plane, 1000 * modelFactor, 1000 * modelFactor,
                            new[] { viewportId }, attributes);
                        if (clipId == Guid.Empty) throw new InvalidOperationException("Could not create clipping plane.");
                        clips.Add(clipId);
                        if (document.Objects.FindId(clipId) is not ClippingPlaneObject native ||
                            !native.ClippingPlaneGeometry.ViewportIds().SequenceEqual(new[] { viewportId }))
                            throw new InvalidOperationException("Native clipping scope verification failed.");
                    }
                }
                sheets.Add(page.MainViewport.Id, new SheetRecord(page.MainViewport.Id, destinationId,
                    order++, new Dictionary<string, string>
                    {
                        ["RhinoLayoutFoundry.DrawingSet.ProposalId"] = spec.ProposalId.ToString("D"),
                        ["RhinoLayoutFoundry.DrawingSet.SheetKey"] = sheet.Key,
                        // Intended content, not a claim that subsequent user edits are up to date.
                        // Keep generated provenance separate from user-owned sheet notes.
                        ["RhinoLayoutFoundry.DrawingSet.IntendedContent"] = JsonSerializer.Serialize(sheet),
                    }, null) { DetailNamedViews = assignments });
            }
            var metadata = before.Metadata.ToDictionary(p => p.Key, p => p.Value);
            metadata.Add(DrawingSetPlanner.ReceiptKey(spec.ProposalId), JsonSerializer.Serialize(new
            { digest = change.Digest, resources, clipping_plane_ids = clips, named_views = namedViews,
                annotation_ids = annotations, presentation_layer_id = presentationLayerIndex < 0 ? Guid.Empty : document.Layers[presentationLayerIndex].Id,
                hidden_layer_scopes = hiddenScopes.Select(s => new { layer_id = s.LayerId, viewport_id = s.ViewportId }) }));
            _stateStore.Set(document, before with { Sheets = sheets, Metadata = metadata, Folders = folders });
            document.Modified = true;
            _revisionTracker.Bump(document);
            _overviewChanged(OverviewInvalidation.All);
            return SuccessWithEntity(plan, "drawing_set.created",
                $"Created {pages.Count} sheets and {resources.Count - pages.Count} scaled details. Proposal receipt saved. " +
                "Visual review is still required. Layout creation is not native-Undo enabled.");
        }
        catch (Exception exception)
        {
            var cleanup = new List<string>();
            foreach (var id in annotations)
                try { if (!document.Objects.Delete(id, true)) cleanup.Add($"caption {id}"); }
                catch { cleanup.Add($"caption {id}"); }
            foreach (var scope in hiddenScopes)
                try
                {
                    var source = document.Layers.FindId(scope.LayerId);
                    if (source is null) continue;
                    var layer = CopyLayer(source);
                    layer.DeletePerViewportVisible(scope.ViewportId);
                    layer.UnsetPerViewportPersistentVisibility(scope.ViewportId);
                    if (!document.Layers.Modify(layer, source.Index, quiet: true)) cleanup.Add($"visibility {scope.LayerId}");
                }
                catch { cleanup.Add($"visibility {scope.LayerId}"); }
            foreach (var id in clips)
                try { if (!document.Objects.Delete(id, true)) cleanup.Add($"clip {id}"); }
                catch { cleanup.Add($"clip {id}"); }
            foreach (var name in namedViews)
                try { document.NamedViews.Delete(name); if (document.NamedViews.FindByName(name) >= 0) cleanup.Add($"view {name}"); }
                catch { cleanup.Add($"view {name}"); }
            foreach (var page in pages.AsEnumerable().Reverse())
                try { var id = page.MainViewport.Id; page.Close();
                    if (document.Views.GetPageViews().Any(p => p.MainViewport.Id == id)) cleanup.Add($"sheet {id}"); }
                catch { cleanup.Add("sheet cleanup"); }
            if (presentationLayerIndex >= 0)
                try { if (!document.Layers.Delete(presentationLayerIndex, true)) cleanup.Add("presentation layer"); }
                catch { cleanup.Add("presentation layer"); }
            _stateStore.Set(document, before);
            _revisionTracker.Bump(document);
            _overviewChanged(OverviewInvalidation.All);
            return Failure("drawing_set.apply_failed", exception.Message + (cleanup.Count == 0
                ? " Newly created resources were removed; existing resources were preserved."
                : " Cleanup incomplete; inspect before retrying: " + string.Join(", ", cleanup)));
        }
        finally
        {
            if (active is not null) document.Views.ActiveView = active;
            document.Views.Redraw();
        }
    }

    private static Guid AddDrawingSetCaption(RhinoDoc document, RhinoPageView page, Guid proposalId,
        string text, double x, double y, double height, double width, int layerIndex)
    {
        var plane = new Plane(new Point3d(x, y, 0), Vector3d.XAxis, Vector3d.YAxis);
        using var entity = TextEntity.Create(text, plane, document.DimStyles.Current, false, width, 0)
            ?? throw new InvalidOperationException("Could not create drawing caption.");
        entity.TextHeight = height;
        entity.TextHorizontalAlignment = TextHorizontalAlignment.Left;
        entity.TextVerticalAlignment = TextVerticalAlignment.Top;
        var bounds = entity.GetBoundingBox(true);
        if (!bounds.IsValid || bounds.Max.X > x + width + height * 0.1 ||
            bounds.Min.Y < y - height * 2)
            throw new InvalidOperationException("A drawing caption does not fit. Shorten the title or widen the frame before approving again.");
        var attributes = new ObjectAttributes { Space = ActiveSpace.PageSpace,
            ViewportId = page.MainViewport.Id, LayerIndex = layerIndex,
            Name = text, ColorSource = ObjectColorSource.ColorFromObject, ObjectColor = System.Drawing.Color.Black };
        attributes.SetUserString("RhinoLayoutFoundry.DrawingSet.ProposalId", proposalId.ToString("D"));
        var id = document.Objects.Add(entity, attributes);
        if (id == Guid.Empty) throw new InvalidOperationException("Could not add drawing caption to its sheet.");
        return id;
    }
}

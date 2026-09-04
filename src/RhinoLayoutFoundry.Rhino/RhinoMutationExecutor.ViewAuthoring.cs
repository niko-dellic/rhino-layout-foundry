using Rhino;
using Rhino.Commands;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Observer;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Rhino;

internal sealed partial class RhinoMutationExecutor
{
    private OperationResult ApplyCreateNamedView(
        RhinoDoc document,
        OperationPlan plan,
        CreateNamedViewChange change)
    {
        var definition = change.Definition;
        if (document.NamedViews.FindByName(definition.Name) >= 0)
            return Failure("named_view.duplicate_name", $"A named view called '{definition.Name}' already exists.");
        var sourceView = document.Views.ActiveView ?? document.Views.GetStandardRhinoViews().FirstOrDefault();
        if (sourceView is null)
            return Failure("named_view.viewport_unavailable", "No Rhino viewport is available to seed the named view.");

        var undoRecord = document.BeginUndoRecord(plan.UndoDescription);
        if (undoRecord == 0)
            return Failure("operation.undo_unavailable", "Rhino could not start a dedicated undo record.");
        var created = false;
        try
        {
            using var view = new ViewInfo(sourceView.ActiveViewport) { Name = definition.Name };
            var viewport = view.Viewport;
            viewport.UnlockCamera();
            var location = Point(definition.CameraLocation);
            var target = Point(definition.CameraTarget);
            var direction = target - location;
            if (!viewport.SetCameraLocation(location) ||
                !viewport.SetCameraDirection(direction) ||
                !viewport.SetCameraUp(Vector(definition.CameraUp)))
                throw new InvalidOperationException("Rhino rejected the proposed camera frame.");
            viewport.TargetPoint = target;
            var projectionChanged = definition.Projection switch
            {
                FoundryViewProjection.Parallel => viewport.ChangeToParallelProjection(true),
                FoundryViewProjection.Perspective => viewport.ChangeToPerspectiveProjection(
                    location.DistanceTo(target), true, definition.LensLength),
                _ => false,
            };
            if (!projectionChanged)
                throw new InvalidOperationException("Rhino rejected the proposed projection.");
            if (document.NamedViews.Add(view) < 0)
                throw new InvalidOperationException("Rhino did not create the named view.");
            created = true;
            document.Modified = true;
            _revisionTracker.Bump(document);
            _overviewChanged(OverviewInvalidation.All);
            return SuccessWithEntity(plan, "named_view.created", $"Created named view '{definition.Name}'.");
        }
        catch (Exception exception)
        {
            if (created) document.NamedViews.Delete(definition.Name);
            return Failure(
                "named_view.apply_failed",
                $"Named-view creation failed and the new view was removed: {exception.Message}");
        }
        finally
        {
            document.EndUndoRecord(undoRecord);
        }
    }

    private OperationResult ApplyCreateClippingPlane(
        RhinoDoc document,
        OperationPlan plan,
        CreateClippingPlaneChange change)
    {
        var definition = change.Definition;
        var availableViewportIds = document.Views.GetStandardRhinoViews()
            .Select(view => view.ActiveViewport.Id)
            .Concat(document.Views.GetPageViews().Select(page => page.MainViewport.Id))
            .Concat(document.Views.GetPageViews()
                .SelectMany(page => page.GetDetailViews())
                .Select(detail => detail.Viewport.Id))
            .ToHashSet();
        var missingViewport = definition.ViewportIds.FirstOrDefault(id => !availableViewportIds.Contains(id));
        if (missingViewport != Guid.Empty)
            return Failure("clipping_plane.viewport_missing", "A targeted viewport no longer exists.");

        var undoRecord = document.BeginUndoRecord(plan.UndoDescription);
        if (undoRecord == 0)
            return Failure("operation.undo_unavailable", "Rhino could not start a dedicated undo record.");
        var objectId = Guid.Empty;
        try
        {
            var normal = Vector(definition.Normal);
            var xAxis = Vector(definition.XAxis);
            var yAxis = Vector3d.CrossProduct(normal, xAxis);
            if (!normal.Unitize() || !xAxis.Unitize() || !yAxis.Unitize())
                throw new InvalidOperationException("Rhino could not normalize the clipping-plane axes.");
            xAxis = Vector3d.CrossProduct(yAxis, normal);
            if (!xAxis.Unitize())
                throw new InvalidOperationException("Rhino could not orthogonalize the clipping-plane axes.");
            var plane = new Plane(Point(definition.Origin), xAxis, yAxis);
            if (!plane.IsValid)
                throw new InvalidOperationException("The clipping plane is invalid.");
            var attributes = new ObjectAttributes { Name = definition.Name };
            attributes.SetUserString("RhinoLayoutFoundry.Automation.SessionId", definition.SessionId);
            attributes.SetUserString("RhinoLayoutFoundry.Automation.Kind", "ClippingPlane");
            objectId = document.Objects.AddClippingPlane(
                plane,
                definition.Width,
                definition.Height,
                definition.ViewportIds,
                attributes);
            if (objectId == Guid.Empty)
                throw new InvalidOperationException("Rhino did not create the clipping plane.");
            document.Modified = true;
            _revisionTracker.Bump(document);
            document.Views.Redraw();
            _overviewChanged(OverviewInvalidation.All);
            return SuccessWithEntity(
                plan,
                "clipping_plane.created",
                $"Created clipping plane '{definition.Name}'.",
                objectId);
        }
        catch (Exception exception)
        {
            if (objectId != Guid.Empty) document.Objects.Delete(objectId, quiet: true);
            return Failure(
                "clipping_plane.apply_failed",
                $"Clipping-plane creation failed and the new object was removed: {exception.Message}");
        }
        finally
        {
            document.EndUndoRecord(undoRecord);
        }
    }

    private static Point3d Point(Point3Coordinates value) => new(value.X, value.Y, value.Z);

    private static Vector3d Vector(Vector3Coordinates value) => new(value.X, value.Y, value.Z);

    private static OperationResult SuccessWithEntity(
        OperationPlan plan,
        string code,
        string message,
        Guid? entityId = null) =>
        new(true, plan.Diagnostics.Concat([
            new Diagnostic(code, DiagnosticSeverity.Information, message, entityId),
        ]).ToArray());

    private OperationResult ApplyNamedView(
        RhinoDoc document,
        OperationPlan plan,
        AssignNamedViewToDetailsChange change,
        UpdateLinkedSheetNamesChange? linkedNames = null)
    {
        var namedViewIndex = document.NamedViews.FindByName(change.NamedViewName);
        if (namedViewIndex < 0)
            return Failure("named_view.missing", "The selected Rhino named view no longer exists.");
        var details = document.Views.GetPageViews()
            .SelectMany(page => page.GetDetailViews())
            .Where(detail => change.DetailViewportIds.Contains(detail.Viewport.Id))
            .ToArray();
        if (details.Length != change.DetailViewportIds.Distinct().Count())
            return Failure("named_view.detail_missing", "A targeted detail viewport no longer exists.");
        if (linkedNames is not null && ValidateLinkedSheetNames(document, linkedNames) is { } namingFailure)
            return namingFailure;
        var before = details.ToDictionary(
            detail => detail.Viewport.Id,
            detail => new ViewportInfo(detail.Viewport));
        var stateBefore = _stateStore.Get(document);
        var pageNamesBefore = CapturePageNames(document, linkedNames?.NewNames.Keys);
        var undoRecord = document.BeginUndoRecord(plan.UndoDescription);
        if (undoRecord == 0)
            return Failure("operation.undo_unavailable", "Rhino could not start a dedicated undo record.");
        try
        {
            foreach (var detail in details)
            {
                if (!document.NamedViews.RestoreWithAspectRatio(namedViewIndex, detail.Viewport))
                    throw new InvalidOperationException(
                        $"Rhino did not apply named view '{change.NamedViewName}' to detail '{detail.DescriptiveTitle}'.");
                if (!detail.CommitViewportChanges())
                    throw new InvalidOperationException(
                        $"Rhino did not commit named view '{change.NamedViewName}' on detail '{detail.DescriptiveTitle}'.");
            }

            var sheets = stateBefore.Sheets.ToDictionary(pair => pair.Key, pair => pair.Value);
            var targetIds = change.DetailViewportIds.ToHashSet();
            foreach (var page in document.Views.GetPageViews())
            {
                if (!sheets.TryGetValue(page.MainViewport.Id, out var sheet)) continue;
                var assigned = sheet.DetailNamedViews
                    .ToDictionary(pair => pair.Key, pair => pair.Value);
                var changed = false;
                foreach (var detailId in page.GetDetailViews()
                             .Select(detail => detail.Viewport.Id)
                             .Where(targetIds.Contains))
                {
                    assigned[detailId] = change.NamedViewName;
                    changed = true;
                }
                if (changed)
                    sheets[page.MainViewport.Id] = sheet with
                    {
                        DetailNamedViews = assigned,
                    };
            }

            if (linkedNames is not null)
            {
                var bindingFailure = ApplyLinkedSheetBindings(sheets, linkedNames);
                if (bindingFailure is not null)
                    throw new InvalidOperationException(bindingFailure.Diagnostics.First().Message);
            }
            var afterState = stateBefore with { Sheets = sheets };
            _stateStore.Set(document, afterState);
            if (linkedNames is not null)
                ApplyLinkedPageNames(document, linkedNames.NewNames, afterState);
            if (!document.AddCustomUndoEvent(
                    plan.UndoDescription,
                    OnUndoDocumentState,
                    new DocumentStateUndoTag(plan.UndoDescription, stateBefore, pageNamesBefore)))
                throw new InvalidOperationException("Rhino could not register named-view metadata with Undo.");

            _revisionTracker.Bump(document);
            document.Views.Redraw();
            return new OperationResult(true, plan.Diagnostics);
        }
        catch (Exception exception)
        {
            foreach (var detail in details)
            {
                if (before.TryGetValue(detail.Viewport.Id, out var viewport))
                {
                    detail.Viewport.SetViewProjection(viewport, true);
                    detail.CommitViewportChanges();
                }
            }
            RestorePageNames(document, pageNamesBefore, stateBefore);
            _stateStore.Set(document, stateBefore);

            return Failure(
                "named_view.apply_failed",
                $"Named-view assignment failed and the original cameras were restored: {exception.Message}");
        }
        finally
        {
            foreach (var viewport in before.Values) viewport.Dispose();
            document.EndUndoRecord(undoRecord);
        }
    }

}

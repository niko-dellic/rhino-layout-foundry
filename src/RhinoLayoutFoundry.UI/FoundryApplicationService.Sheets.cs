using Eto.Drawing;
using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Observer;
using RhinoLayoutFoundry.Core.Overview;
using RhinoLayoutFoundry.Core.Persistence;

namespace RhinoLayoutFoundry.UI;

internal sealed partial class FoundryApplicationService
{
    public async Task<OperationResult> BatchCreateSheetsAsync(
        BatchCreateSheetsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_snapshotProvider is null || _mutationService is null)
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");
        try
        {
            var snapshot = _snapshotProvider.Capture();
            var currentRequest = request with
            {
                DocumentRuntimeSerialNumber = snapshot.DocumentRuntimeSerialNumber,
                SourceRevision = snapshot.Revision,
            };
            var plan = new BatchCreateSheetsPlanner().Plan(currentRequest, snapshot);
            var result = plan.CanApply
                ? await Mutations.ApplyAsync(plan, cancellationToken)
                : new OperationResult(false, plan.Diagnostics);
            if (result.Succeeded)
                NotifyOverviewChanged(new OverviewInvalidation(snapshot.DocumentRuntimeSerialNumber,
                    OverviewInvalidationKind.All));
            return result;
        }
        catch (Exception exception)
        {
            return UnavailableResult(exception.Message);
        }
    }

    public async Task<OperationResult> UpdateProjectInformationAsync(
        ProjectInformation information,
        CancellationToken cancellationToken = default)
    {
        if (_snapshotProvider is null || _mutationService is null)
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");
        try
        {
            var snapshot = _snapshotProvider.Capture();
            var plan = new UpdateProjectInformationPlanner().Plan(
                new UpdateProjectInformationRequest(
                    snapshot.DocumentRuntimeSerialNumber,
                    snapshot.Revision,
                    information),
                snapshot);
            var result = plan.CanApply
                ? await Mutations.ApplyAsync(plan, cancellationToken)
                : new OperationResult(false, plan.Diagnostics);
            if (result.Succeeded)
                NotifyOverviewChanged(new OverviewInvalidation(
                    snapshot.DocumentRuntimeSerialNumber,
                    OverviewInvalidationKind.Metadata |
                    OverviewInvalidationKind.Thumbnails |
                    OverviewInvalidationKind.Diagnostics));
            return result;
        }
        catch (Exception exception)
        {
            return UnavailableResult(exception.Message);
        }
    }

    public async Task<OperationResult> BatchUpdateSheetsAsync(
        BatchUpdateSheetsRequest request,
        CancellationToken cancellationToken = default)
    {
        return await RunOperationAsync(async snapshot =>
        {
            var current = request with
            {
                DocumentRuntimeSerialNumber = snapshot.DocumentRuntimeSerialNumber,
                SourceRevision = snapshot.Revision,
            };
            var plan = new BatchUpdateSheetsPlanner().Plan(current, snapshot);
            var result = plan.CanApply
                ? await Mutations.ApplyAsync(plan, cancellationToken)
                : new OperationResult(false, plan.Diagnostics);
            if (result.Succeeded)
                NotifyOverviewChanged(new OverviewInvalidation(snapshot.DocumentRuntimeSerialNumber,
                    OverviewInvalidationKind.All));
            return result;
        }, cancellationToken);
    }

    public async Task<OperationResult> SetDisplayModeAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        Guid displayModeId,
        CancellationToken cancellationToken = default)
    {
        return await RunOperationAsync(async snapshot =>
        {
            var detailIds = BatchTargetResolver.ResolveDetailIds(snapshot, targets);
            var plan = new UpdateDetailDisplayModesPlanner().Plan(
                new UpdateDetailDisplayModesRequest(
                    snapshot.DocumentRuntimeSerialNumber,
                    snapshot.Revision,
                    detailIds,
                    displayModeId),
                snapshot);
            var result = plan.CanApply
                ? await Mutations.ApplyAsync(plan, cancellationToken)
                : new OperationResult(false, plan.Diagnostics);
            if (result.Succeeded)
                NotifyOverviewChanged(new OverviewInvalidation(
                    snapshot.DocumentRuntimeSerialNumber,
                    OverviewInvalidationKind.Metadata |
                    OverviewInvalidationKind.Diagnostics |
                    OverviewInvalidationKind.Thumbnails,
                    detailIds.ToHashSet()));
            return result;
        }, cancellationToken);
    }

    public async Task<OperationResult> SetPaperSizeAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        double width,
        double height,
        string unitSystem,
        CancellationToken cancellationToken = default)
    {
        if (_snapshotProvider is null)
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");
        var snapshot = CaptureSnapshot();
        if (snapshot is null)
            return UnavailableResult("The active Rhino document is unavailable.");
        var sheetIds = BatchTargetResolver.ResolveSheetIds(snapshot, targets);
        return await BatchUpdateSheetsAsync(new BatchUpdateSheetsRequest(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            sheetIds,
            null,
            1,
            1,
            width,
            height,
            unitSystem,
            null), cancellationToken);
    }

    public async Task<OperationResult> SetPrintInclusionAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        bool include,
        CancellationToken cancellationToken = default)
    {
        return await RunOperationAsync(async snapshot =>
        {
            var sheetIds = BatchTargetResolver.ResolveSheetIds(snapshot, targets);
            var plan = new SetPrintInclusionPlanner().Plan(
                new SetPrintInclusionRequest(
                    snapshot.DocumentRuntimeSerialNumber,
                    snapshot.Revision,
                    sheetIds,
                    include),
                snapshot);
            var result = plan.CanApply
                ? await Mutations.ApplyAsync(plan, cancellationToken)
                : new OperationResult(false, plan.Diagnostics);
            if (result.Succeeded)
                NotifyOverviewChanged(new OverviewInvalidation(
                    snapshot.DocumentRuntimeSerialNumber,
                    OverviewInvalidationKind.Metadata | OverviewInvalidationKind.Diagnostics,
                    sheetIds.ToHashSet()));
            return result;
        }, cancellationToken);
    }

    public async Task<OperationResult> SetObserverCanvasStateAsync(
        ObserverCanvasState newState,
        string undoDescription = "Organize observer canvas",
        CancellationToken cancellationToken = default)
    {
        return await RunOperationAsync(async snapshot =>
        {
            var plan = new SetObserverCanvasStatePlanner().Plan(
                new SetObserverCanvasStateRequest(
                    snapshot.DocumentRuntimeSerialNumber,
                    snapshot.Revision,
                    newState,
                    undoDescription),
                snapshot);
            var result = plan.CanApply
                ? await Mutations.ApplyAsync(plan, cancellationToken)
                : new OperationResult(false, plan.Diagnostics);
            if (result.Succeeded)
            {
                NotifyOverviewChanged(new OverviewInvalidation(
                    snapshot.DocumentRuntimeSerialNumber,
                    OverviewInvalidationKind.Metadata | OverviewInvalidationKind.Diagnostics));
            }

            return result;
        }, cancellationToken);
    }

    public async Task<OperationResult> UpdateHierarchyNotesAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        string notes,
        CancellationToken cancellationToken = default)
    {
        return await RunOperationAsync(async snapshot =>
        {
            var request = new UpdateHierarchyNotesRequest(
                snapshot.DocumentRuntimeSerialNumber,
                snapshot.Revision,
                targets,
                notes ?? string.Empty);
            var plan = new UpdateHierarchyNotesPlanner().Plan(request, snapshot);
            if (plan.Changes.Count == 0 && plan.Diagnostics.All(item =>
                    item.Severity != DiagnosticSeverity.Error))
                return new OperationResult(true, plan.Diagnostics);
            return await ApplyHierarchyPlanAsync(
                plan,
                snapshot.DocumentRuntimeSerialNumber,
                targets.Select(target => target.Id).ToHashSet(),
                cancellationToken);
        }, cancellationToken);
    }

    public async Task<OperationResult> AssignNamedViewAsync(
        IReadOnlyList<Guid> detailViewportIds,
        string namedViewName,
        CancellationToken cancellationToken = default)
    {
        return await RunOperationAsync(async snapshot =>
        {
            var plan = new AssignNamedViewPlanner().Plan(
                new AssignNamedViewRequest(
                    snapshot.DocumentRuntimeSerialNumber,
                    snapshot.Revision,
                    detailViewportIds,
                    namedViewName),
                snapshot);
            var result = plan.CanApply
                ? await Mutations.ApplyAsync(plan, cancellationToken)
                : new OperationResult(false, plan.Diagnostics);
            if (result.Succeeded)
            {
                var affected = detailViewportIds
                    .Concat(plan.Changes.OfType<UpdateLinkedSheetNamesChange>()
                        .SelectMany(change => change.NewNames.Keys.Concat(change.NewBindings.Keys)))
                    .ToHashSet();
                NotifyOverviewChanged(new OverviewInvalidation(
                    snapshot.DocumentRuntimeSerialNumber,
                    OverviewInvalidationKind.Metadata |
                    OverviewInvalidationKind.Diagnostics |
                    OverviewInvalidationKind.Thumbnails,
                    affected));
            }

            return result;
        }, cancellationToken);
    }

    public async Task<OperationResult> ReorderSheetAsync(
        Guid movingSheetId,
        Guid? beforeSheetId,
        CancellationToken cancellationToken = default)
    {
        return await RunOperationAsync(async snapshot =>
        {
            var plan = new ReorderSheetsPlanner().Plan(
                new ReorderSheetsRequest(
                    snapshot.DocumentRuntimeSerialNumber,
                    snapshot.Revision,
                    movingSheetId,
                    beforeSheetId),
                snapshot);
            return await ApplyHierarchyPlanAsync(
                plan,
                snapshot.DocumentRuntimeSerialNumber,
                beforeSheetId is { } targetId
                    ? new HashSet<Guid> { movingSheetId, targetId }
                    : new HashSet<Guid> { movingSheetId },
                cancellationToken);
        }, cancellationToken);
    }

}

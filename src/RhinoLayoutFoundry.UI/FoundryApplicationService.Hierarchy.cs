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
    public async Task<OperationResult> RenameSheetAsync(
        Guid pageViewId,
        string expectedName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        if (!CaptureMutationCapabilities().PageRenameUndo.IsSupported)
        {
            return UnavailableResult(
                CaptureMutationCapabilities().PageRenameUndo.Reason);
        }

        if (_snapshotProvider is null || _mutationService is null)
        {
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");
        }

        try
        {
            var snapshot = _snapshotProvider.Capture();
            var request = new RenameSheetRequest(
                snapshot.DocumentRuntimeSerialNumber,
                snapshot.Revision,
                pageViewId,
                expectedName,
                newName);
            var plan = new RenameSheetPlanner().Plan(request, snapshot);
            if (!plan.CanApply)
            {
                return new OperationResult(false, plan.Diagnostics);
            }

            var result = await Mutations.ApplyAsync(plan, cancellationToken);
            if (result.Succeeded)
            {
                NotifyOverviewChanged(new OverviewInvalidation(
                    snapshot.DocumentRuntimeSerialNumber,
                    OverviewInvalidationKind.Hierarchy |
                    OverviewInvalidationKind.Metadata |
                    OverviewInvalidationKind.Diagnostics |
                    OverviewInvalidationKind.Thumbnails,
                    new HashSet<Guid> { pageViewId }));
            }

            return result;
        }
        catch (Exception exception)
        {
            return UnavailableResult(exception.Message);
        }
    }

    public async Task<OperationResult> CreateFolderAsync(
        Guid folderId,
        Guid parentFolderId,
        string name,
        CancellationToken cancellationToken = default)
    {
        return await RunOperationAsync(async snapshot =>
        {
            var request = new AddFolderRequest(
                snapshot.DocumentRuntimeSerialNumber,
                snapshot.Revision,
                folderId,
                parentFolderId,
                name);
            var plan = new AddFolderPlanner().Plan(request, snapshot);
            if (!plan.CanApply)
            {
                return new OperationResult(false, plan.Diagnostics);
            }

            var result = await Mutations.ApplyAsync(plan, cancellationToken);
            if (result.Succeeded)
            {
                NotifyOverviewChanged(new OverviewInvalidation(
                    snapshot.DocumentRuntimeSerialNumber,
                    OverviewInvalidationKind.Hierarchy |
                    OverviewInvalidationKind.Metadata |
                    OverviewInvalidationKind.Diagnostics,
                    new HashSet<Guid> { folderId, parentFolderId }));
            }

            return result;
        }, cancellationToken);
    }

    public async Task<OperationResult> RenameFolderAsync(
        Guid folderId,
        string expectedName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        return await RunOperationAsync(async snapshot =>
        {
            var plan = new RenameFolderPlanner().Plan(
                new RenameFolderRequest(
                    snapshot.DocumentRuntimeSerialNumber,
                    snapshot.Revision,
                    folderId,
                    expectedName,
                    newName),
                snapshot);
            return await ApplyHierarchyPlanAsync(
                plan,
                snapshot.DocumentRuntimeSerialNumber,
                new HashSet<Guid> { folderId },
                cancellationToken);
        }, cancellationToken);
    }

    public async Task<OperationResult> DeleteFolderAsync(
        Guid folderId,
        string expectedName,
        CancellationToken cancellationToken = default)
    {
        return await RunOperationAsync(async snapshot =>
        {
            var plan = new DeleteFolderPlanner().Plan(
                new DeleteFolderRequest(
                    snapshot.DocumentRuntimeSerialNumber,
                    snapshot.Revision,
                    folderId,
                    expectedName),
                snapshot);
            return await ApplyHierarchyPlanAsync(
                plan,
                snapshot.DocumentRuntimeSerialNumber,
                new HashSet<Guid> { folderId },
                cancellationToken);
        }, cancellationToken);
    }

    public async Task<OperationResult> DuplicateFolderAsync(
        Guid folderId,
        string expectedName,
        CancellationToken cancellationToken = default)
    {
        return await RunOperationAsync(async snapshot =>
        {
            var plan = new DuplicateFolderPlanner().Plan(new DuplicateFolderRequest(
                snapshot.DocumentRuntimeSerialNumber,
                snapshot.Revision,
                folderId,
                expectedName), snapshot);
            return await ApplyHierarchyPlanAsync(plan, snapshot.DocumentRuntimeSerialNumber,
                new HashSet<Guid> { folderId }, cancellationToken);
        }, cancellationToken);
    }

    public async Task<OperationResult> DuplicateSelectionAsync(
        IReadOnlyList<OverviewNodeKey> selection,
        CancellationToken cancellationToken = default)
    {
        return await RunOperationAsync(async snapshot =>
        {
            var plan = new DuplicateHierarchySelectionPlanner().Plan(
                new DuplicateHierarchySelectionRequest(
                    snapshot.DocumentRuntimeSerialNumber,
                    snapshot.Revision,
                    selection),
                snapshot);
            return await ApplyHierarchyPlanAsync(
                plan,
                snapshot.DocumentRuntimeSerialNumber,
                selection.Select(item => item.Id).ToHashSet(),
                cancellationToken);
        }, cancellationToken);
    }

    public async Task<OperationResult> PasteSelectionAsync(
        uint sourceDocumentRuntimeSerialNumber,
        IReadOnlyList<OverviewNodeKey> selection,
        Guid destinationFolderId,
        ObserverPointRecord? canvasTargetOrigin = null,
        CancellationToken cancellationToken = default)
    {
        return await RunOperationAsync(async snapshot =>
        {
            var plan = new PasteHierarchySelectionPlanner().Plan(
                new PasteHierarchySelectionRequest(
                    sourceDocumentRuntimeSerialNumber,
                    snapshot.Revision,
                    destinationFolderId,
                    selection,
                    canvasTargetOrigin),
                snapshot);
            return await ApplyHierarchyPlanAsync(
                plan,
                snapshot.DocumentRuntimeSerialNumber,
                selection.Select(item => item.Id).Append(destinationFolderId).ToHashSet(),
                cancellationToken);
        }, cancellationToken);
    }

    public async Task<OperationResult> DeleteSelectionAsync(
        IReadOnlyList<OverviewNodeKey> selection,
        CancellationToken cancellationToken = default)
    {
        return await RunOperationAsync(async snapshot =>
        {
            var plan = new DeleteHierarchySelectionPlanner().Plan(
                new DeleteHierarchySelectionRequest(
                    snapshot.DocumentRuntimeSerialNumber,
                    snapshot.Revision,
                    selection),
                snapshot);
            return await ApplyHierarchyPlanAsync(
                plan,
                snapshot.DocumentRuntimeSerialNumber,
                selection.Select(item => item.Id).ToHashSet(),
                cancellationToken);
        }, cancellationToken);
    }

    public async Task<OperationResult> MoveSheetsAsync(
        Guid destinationFolderId,
        IReadOnlyList<Guid> sheetPageViewIds,
        CancellationToken cancellationToken = default)
    {
        return await RunOperationAsync(async snapshot =>
        {
            var plan = new MoveSheetsPlanner().Plan(
                new MoveSheetsRequest(
                    snapshot.DocumentRuntimeSerialNumber,
                    snapshot.Revision,
                    destinationFolderId,
                    sheetPageViewIds),
                snapshot);
            return await ApplyHierarchyPlanAsync(
                plan,
                snapshot.DocumentRuntimeSerialNumber,
                sheetPageViewIds.Append(destinationFolderId).ToHashSet(),
                cancellationToken);
        }, cancellationToken);
    }

    public async Task<OperationResult> MoveFoldersAsync(
        Guid destinationFolderId,
        IReadOnlyList<Guid> folderIds,
        CancellationToken cancellationToken = default)
    {
        return await RunOperationAsync(async snapshot =>
        {
            var plan = new MoveFoldersPlanner().Plan(
                new MoveFoldersRequest(
                    snapshot.DocumentRuntimeSerialNumber,
                    snapshot.Revision,
                    destinationFolderId,
                    folderIds),
                snapshot);
            return await ApplyHierarchyPlanAsync(
                plan,
                snapshot.DocumentRuntimeSerialNumber,
                folderIds.Append(destinationFolderId).ToHashSet(),
                cancellationToken);
        }, cancellationToken);
    }

    public async Task<OperationResult> MoveHierarchySelectionAsync(
        Guid destinationFolderId,
        IReadOnlyList<Guid> folderIds,
        IReadOnlyList<Guid> sheetIds,
        CancellationToken cancellationToken = default)
    {
        return await RunOperationAsync(async snapshot =>
        {
            var plan = new MoveHierarchySelectionPlanner().Plan(
                new MoveHierarchySelectionRequest(
                    snapshot.DocumentRuntimeSerialNumber,
                    snapshot.Revision,
                    destinationFolderId,
                    folderIds,
                    sheetIds),
                snapshot);
            return await ApplyHierarchyPlanAsync(
                plan,
                snapshot.DocumentRuntimeSerialNumber,
                folderIds.Concat(sheetIds).Append(destinationFolderId).ToHashSet(),
                cancellationToken);
        }, cancellationToken);
    }

    public async Task<OperationResult> ReorganizeHierarchyAsync(
        IReadOnlyList<Guid> folderIds,
        IReadOnlyList<Guid> sheetIds,
        HierarchyPlacementTarget target,
        CancellationToken cancellationToken = default)
    {
        return await RunOperationAsync(async snapshot =>
        {
            var plan = new HierarchyPlacementPlanner().Plan(
                new HierarchyPlacementRequest(
                    snapshot.DocumentRuntimeSerialNumber,
                    snapshot.Revision,
                    folderIds,
                    sheetIds,
                    target),
                snapshot);
            return await ApplyHierarchyPlanAsync(
                plan,
                snapshot.DocumentRuntimeSerialNumber,
                folderIds.Concat(sheetIds).Append(target.TargetId).ToHashSet(),
                cancellationToken);
        }, cancellationToken);
    }

    public async Task<OperationResult> CreateSheetAsync(
        Guid destinationFolderId,
        string name,
        CancellationToken cancellationToken = default)
    {
        return await RunOperationAsync(async snapshot =>
        {
            var plan = new CreateSheetPlanner().Plan(
                new CreateSheetRequest(
                    snapshot.DocumentRuntimeSerialNumber,
                    snapshot.Revision,
                    destinationFolderId,
                    name),
                snapshot);
            return await ApplyHierarchyPlanAsync(
                plan,
                snapshot.DocumentRuntimeSerialNumber,
                new HashSet<Guid> { destinationFolderId },
                cancellationToken);
        }, cancellationToken);
    }

}

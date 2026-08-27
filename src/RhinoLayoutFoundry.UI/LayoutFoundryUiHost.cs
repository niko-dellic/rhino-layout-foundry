using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

public static class LayoutFoundryUiHost
{
    private static IDocumentOverviewProvider? _overviewProvider;
    private static IDocumentSnapshotProvider? _snapshotProvider;
    private static IDocumentMutationService? _mutationService;
    private static IDocumentOverviewNavigationService? _navigationService;
    private static ILayoutPdfExportService? _pdfExportService;
    private static IDocumentThumbnailProvider? _thumbnailProvider;
    private static IMutationCapabilityProvider? _capabilityProvider;
    private static ITemplateCaptureContextProvider? _templateCaptureContextProvider;
    private static EventHandler<OverviewInvalidationEventArgs>? _overviewChanged;

    public static event EventHandler<OverviewInvalidationEventArgs> OverviewChanged
    {
        add => _overviewChanged += value;
        remove => _overviewChanged -= value;
    }

    public static void Configure(
        IDocumentOverviewProvider overviewProvider,
        IDocumentSnapshotProvider snapshotProvider,
        IDocumentMutationService mutationService,
        IDocumentOverviewNavigationService navigationService,
        ILayoutPdfExportService pdfExportService,
        IDocumentThumbnailProvider thumbnailProvider,
        IMutationCapabilityProvider capabilityProvider,
        ITemplateCaptureContextProvider templateCaptureContextProvider)
    {
        _overviewProvider = overviewProvider ?? throw new ArgumentNullException(nameof(overviewProvider));
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
        _mutationService = mutationService ?? throw new ArgumentNullException(nameof(mutationService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _pdfExportService = pdfExportService ?? throw new ArgumentNullException(nameof(pdfExportService));
        _thumbnailProvider = thumbnailProvider ?? throw new ArgumentNullException(nameof(thumbnailProvider));
        _capabilityProvider = capabilityProvider ?? throw new ArgumentNullException(nameof(capabilityProvider));
        _templateCaptureContextProvider = templateCaptureContextProvider ??
            throw new ArgumentNullException(nameof(templateCaptureContextProvider));
        NotifyOverviewChanged(OverviewInvalidation.All);
    }

    public static DocumentOverview CaptureOverview()
    {
        return _overviewProvider?.Capture() ?? DocumentOverview.NoDocument;
    }

    public static DocumentOverviewIdentity CaptureOverviewIdentity()
    {
        return _overviewProvider?.CaptureIdentity() ?? new DocumentOverviewIdentity(null, 0);
    }

    public static (uint DocumentRuntimeSerialNumber, long Revision)? CaptureDocumentContext()
    {
        if (_snapshotProvider is null)
        {
            return null;
        }

        try
        {
            var snapshot = _snapshotProvider.Capture();
            return (snapshot.DocumentRuntimeSerialNumber, snapshot.Revision);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static DocumentSnapshot? CaptureSnapshot()
    {
        try
        {
            return _snapshotProvider?.Capture();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static TemplateCaptureContext? CaptureTemplateContext(Guid sourcePageViewId)
    {
        try
        {
            return _templateCaptureContextProvider?.Capture(sourcePageViewId);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static void NotifyOverviewChanged(OverviewInvalidation? invalidation = null)
    {
        _overviewChanged?.Invoke(
            null,
            new OverviewInvalidationEventArgs(invalidation ?? OverviewInvalidation.All));
    }

    public static OverviewNavigationResult Navigate(OverviewNavigationTarget target)
    {
        return _navigationService?.Navigate(target) ??
               new OverviewNavigationResult(false, "Foundry is not connected to an active Rhino plug-in.");
    }

    public static OverviewNavigationResult DuplicateSheet(Guid sheetPageViewId)
    {
        var result = _navigationService?.DuplicateSheet(sheetPageViewId) ??
                     new OverviewNavigationResult(false, "Foundry is not connected to an active Rhino plug-in.");
        if (result.Succeeded)
        {
            NotifyOverviewChanged(OverviewInvalidation.All);
        }

        return result;
    }

    public static OverviewNavigationResult DeleteSheet(Guid sheetPageViewId)
    {
        var result = _navigationService?.DeleteSheet(sheetPageViewId) ??
                     new OverviewNavigationResult(false, "Foundry is not connected to an active Rhino plug-in.");
        if (result.Succeeded)
        {
            NotifyOverviewChanged(OverviewInvalidation.All);
        }

        return result;
    }

    public static OverviewNavigationResult RenameSheetDirect(Guid sheetPageViewId, string newName)
    {
        var result = _navigationService?.RenameSheet(sheetPageViewId, newName) ??
                     new OverviewNavigationResult(false, "Foundry is not connected to an active Rhino plug-in.");
        if (result.Succeeded)
        {
            NotifyOverviewChanged(OverviewInvalidation.All);
        }

        return result;
    }

    public static OverviewNavigationResult RunSheetCommand(Guid sheetPageViewId, LayoutSheetCommand command)
    {
        return _navigationService?.RunSheetCommand(sheetPageViewId, command) ??
               new OverviewNavigationResult(false, "Foundry is not connected to an active Rhino plug-in.");
    }

    public static Task<LayoutPdfExportResult> ExportPdfAsync(
        LayoutPdfExportRequest request,
        CancellationToken cancellationToken = default)
    {
        return _pdfExportService?.ExportAsync(request, cancellationToken) ??
               Task.FromResult(new LayoutPdfExportResult(
                   false,
                   0,
                   "Foundry is not connected to a PDF export service."));
    }

    public static Task<OverviewThumbnailResult> CaptureThumbnailAsync(
        OverviewThumbnailRequest request,
        CancellationToken cancellationToken = default)
    {
        return _thumbnailProvider?.CaptureAsync(request, cancellationToken) ??
               Task.FromResult(new OverviewThumbnailResult(
                   request.Key,
                   null,
                   "Foundry is not connected to a thumbnail provider."));
    }

    public static FoundryMutationCapabilities CaptureMutationCapabilities()
    {
        return _capabilityProvider?.Capture() ?? FoundryMutationCapabilities.Unavailable;
    }

    public static async Task<OperationResult> RenameSheetAsync(
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

            var result = await _mutationService.ApplyAsync(plan, cancellationToken);
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
        catch (InvalidOperationException exception)
        {
            return UnavailableResult(exception.Message);
        }
    }

    public static async Task<OperationResult> CreateFolderAsync(
        Guid folderId,
        Guid parentFolderId,
        string name,
        CancellationToken cancellationToken = default)
    {
        if (_snapshotProvider is null || _mutationService is null)
        {
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");
        }

        try
        {
            var snapshot = _snapshotProvider.Capture();
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

            var result = await _mutationService.ApplyAsync(plan, cancellationToken);
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
        }
        catch (InvalidOperationException exception)
        {
            return UnavailableResult(exception.Message);
        }
    }

    public static async Task<OperationResult> RenameFolderAsync(
        Guid folderId,
        string expectedName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        if (_snapshotProvider is null || _mutationService is null)
        {
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");
        }

        try
        {
            var snapshot = _snapshotProvider.Capture();
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
        }
        catch (InvalidOperationException exception)
        {
            return UnavailableResult(exception.Message);
        }
    }

    public static async Task<OperationResult> DeleteFolderAsync(
        Guid folderId,
        string expectedName,
        CancellationToken cancellationToken = default)
    {
        if (_snapshotProvider is null || _mutationService is null)
        {
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");
        }

        try
        {
            var snapshot = _snapshotProvider.Capture();
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
        }
        catch (InvalidOperationException exception)
        {
            return UnavailableResult(exception.Message);
        }
    }

    public static async Task<OperationResult> DuplicateFolderAsync(
        Guid folderId,
        string expectedName,
        CancellationToken cancellationToken = default)
    {
        if (_snapshotProvider is null || _mutationService is null)
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");
        try
        {
            var snapshot = _snapshotProvider.Capture();
            var plan = new DuplicateFolderPlanner().Plan(new DuplicateFolderRequest(
                snapshot.DocumentRuntimeSerialNumber,
                snapshot.Revision,
                folderId,
                expectedName), snapshot);
            return await ApplyHierarchyPlanAsync(plan, snapshot.DocumentRuntimeSerialNumber,
                new HashSet<Guid> { folderId }, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return UnavailableResult(exception.Message);
        }
    }

    public static async Task<OperationResult> DuplicateSelectionAsync(
        IReadOnlyList<OverviewNodeKey> selection,
        CancellationToken cancellationToken = default)
    {
        if (_snapshotProvider is null || _mutationService is null)
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");
        try
        {
            var snapshot = _snapshotProvider.Capture();
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
        }
        catch (InvalidOperationException exception)
        {
            return UnavailableResult(exception.Message);
        }
    }

    public static async Task<OperationResult> DeleteSelectionAsync(
        IReadOnlyList<OverviewNodeKey> selection,
        CancellationToken cancellationToken = default)
    {
        if (_snapshotProvider is null || _mutationService is null)
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");
        try
        {
            var snapshot = _snapshotProvider.Capture();
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
        }
        catch (InvalidOperationException exception)
        {
            return UnavailableResult(exception.Message);
        }
    }

    public static async Task<OperationResult> MoveSheetsAsync(
        Guid destinationFolderId,
        IReadOnlyList<Guid> sheetPageViewIds,
        CancellationToken cancellationToken = default)
    {
        if (_snapshotProvider is null || _mutationService is null)
        {
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");
        }

        try
        {
            var snapshot = _snapshotProvider.Capture();
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
        }
        catch (InvalidOperationException exception)
        {
            return UnavailableResult(exception.Message);
        }
    }

    public static async Task<OperationResult> MoveFoldersAsync(
        Guid destinationFolderId,
        IReadOnlyList<Guid> folderIds,
        CancellationToken cancellationToken = default)
    {
        if (_snapshotProvider is null || _mutationService is null)
        {
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");
        }

        try
        {
            var snapshot = _snapshotProvider.Capture();
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
        }
        catch (InvalidOperationException exception)
        {
            return UnavailableResult(exception.Message);
        }
    }

    public static async Task<OperationResult> CreateSheetAsync(
        Guid destinationFolderId,
        string name,
        CancellationToken cancellationToken = default)
    {
        if (_snapshotProvider is null || _mutationService is null)
        {
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");
        }

        try
        {
            var snapshot = _snapshotProvider.Capture();
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
        }
        catch (InvalidOperationException exception)
        {
            return UnavailableResult(exception.Message);
        }
    }

    public static async Task<OperationResult> CaptureSheetTemplateAsync(
        Guid sourcePageViewId,
        string name,
        string defaultNamingPattern,
        Guid? titleBlockInstanceObjectId,
        CancellationToken cancellationToken = default)
    {
        if (_snapshotProvider is null || _mutationService is null)
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");
        try
        {
            var snapshot = _snapshotProvider.Capture();
            var plan = new CaptureSheetTemplatePlanner().Plan(new CaptureSheetTemplateRequest(
                snapshot.DocumentRuntimeSerialNumber,
                snapshot.Revision,
                Guid.NewGuid(),
                sourcePageViewId,
                name,
                defaultNamingPattern,
                titleBlockInstanceObjectId), snapshot);
            var result = plan.CanApply
                ? await _mutationService.ApplyAsync(plan, cancellationToken)
                : new OperationResult(false, plan.Diagnostics);
            if (result.Succeeded)
                NotifyOverviewChanged(new OverviewInvalidation(snapshot.DocumentRuntimeSerialNumber,
                    OverviewInvalidationKind.Metadata | OverviewInvalidationKind.Diagnostics));
            return result;
        }
        catch (InvalidOperationException exception)
        {
            return UnavailableResult(exception.Message);
        }
    }

    public static async Task<OperationResult> BatchCreateSheetsAsync(
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
                ? await _mutationService.ApplyAsync(plan, cancellationToken)
                : new OperationResult(false, plan.Diagnostics);
            if (result.Succeeded)
                NotifyOverviewChanged(new OverviewInvalidation(snapshot.DocumentRuntimeSerialNumber,
                    OverviewInvalidationKind.All));
            return result;
        }
        catch (InvalidOperationException exception)
        {
            return UnavailableResult(exception.Message);
        }
    }

    public static async Task<OperationResult> BatchUpdateSheetsAsync(
        BatchUpdateSheetsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_snapshotProvider is null || _mutationService is null)
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");
        try
        {
            var snapshot = _snapshotProvider.Capture();
            var current = request with
            {
                DocumentRuntimeSerialNumber = snapshot.DocumentRuntimeSerialNumber,
                SourceRevision = snapshot.Revision,
            };
            var plan = new BatchUpdateSheetsPlanner().Plan(current, snapshot);
            var result = plan.CanApply
                ? await _mutationService.ApplyAsync(plan, cancellationToken)
                : new OperationResult(false, plan.Diagnostics);
            if (result.Succeeded)
                NotifyOverviewChanged(new OverviewInvalidation(snapshot.DocumentRuntimeSerialNumber,
                    OverviewInvalidationKind.All));
            return result;
        }
        catch (InvalidOperationException exception)
        {
            return UnavailableResult(exception.Message);
        }
    }

    public static void Reset()
    {
        _overviewProvider = null;
        _snapshotProvider = null;
        _mutationService = null;
        _navigationService = null;
        _pdfExportService = null;
        _thumbnailProvider = null;
        _capabilityProvider = null;
        _templateCaptureContextProvider = null;
        _overviewChanged = null;
    }

    private static OperationResult UnavailableResult(string message)
    {
        return new OperationResult(
            false,
            [new Diagnostic("ui.unavailable", DiagnosticSeverity.Error, message)]);
    }

    private static async Task<OperationResult> ApplyHierarchyPlanAsync(
        OperationPlan plan,
        uint documentRuntimeSerialNumber,
        IReadOnlySet<Guid> affectedEntityIds,
        CancellationToken cancellationToken)
    {
        if (!plan.CanApply)
        {
            return new OperationResult(false, plan.Diagnostics);
        }

        var result = await _mutationService!.ApplyAsync(plan, cancellationToken);
        if (result.Succeeded)
        {
            NotifyOverviewChanged(new OverviewInvalidation(
                documentRuntimeSerialNumber,
                OverviewInvalidationKind.Hierarchy |
                OverviewInvalidationKind.Metadata |
                OverviewInvalidationKind.Diagnostics,
                affectedEntityIds));
        }

        return result;
    }
}

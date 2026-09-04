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
    private IDocumentOverviewProvider? _overviewProvider;
    private IDocumentSnapshotProvider? _snapshotProvider;
    private IDocumentMutationService? _mutationService;
    private IDocumentMutationService Mutations => _mutationService ??
        throw new InvalidOperationException("Foundry is no longer connected to Rhino.");
    private IDocumentOverviewNavigationService? _navigationService;
    private ILayoutPdfExportService? _pdfExportService;
    private ILayoutPrintDialogService? _printDialogService;
    private ILayoutPackageService? _layoutPackageService;
    private IDocumentThumbnailProvider? _thumbnailProvider;
    private INamedViewThumbnailProvider? _namedViewThumbnailProvider;
    private IDraftLayoutThumbnailProvider? _draftLayoutThumbnailProvider;
    private IMutationCapabilityProvider? _capabilityProvider;
    private ITemplateCaptureContextProvider? _templateCaptureContextProvider;
    private IDocumentObserverSnapshotProvider? _observerSnapshotProvider;
    private IModelObjectSelectionService? _modelObjectSelectionService;
    private Image? _projectIcon;
    private EventHandler<OverviewInvalidationEventArgs>? _overviewChanged;
    private readonly DocumentSelectionState SharedSelection = new();

    public event EventHandler<OverviewInvalidationEventArgs> OverviewChanged
    {
        add => _overviewChanged += value;
        remove => _overviewChanged -= value;
    }

    public void Configure(
        IDocumentOverviewProvider overviewProvider,
        IDocumentSnapshotProvider snapshotProvider,
        IDocumentMutationService mutationService,
        IDocumentOverviewNavigationService navigationService,
        ILayoutPdfExportService pdfExportService,
        ILayoutPrintDialogService printDialogService,
        ILayoutPackageService layoutPackageService,
        IDocumentThumbnailProvider thumbnailProvider,
        INamedViewThumbnailProvider namedViewThumbnailProvider,
        IDraftLayoutThumbnailProvider draftLayoutThumbnailProvider,
        IMutationCapabilityProvider capabilityProvider,
        ITemplateCaptureContextProvider templateCaptureContextProvider,
        IDocumentObserverSnapshotProvider observerSnapshotProvider,
        IModelObjectSelectionService modelObjectSelectionService,
        Image? projectIcon = null)
    {
        _overviewProvider = overviewProvider ?? throw new ArgumentNullException(nameof(overviewProvider));
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
        _mutationService = mutationService ?? throw new ArgumentNullException(nameof(mutationService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _pdfExportService = pdfExportService ?? throw new ArgumentNullException(nameof(pdfExportService));
        _printDialogService = printDialogService ?? throw new ArgumentNullException(nameof(printDialogService));
        _layoutPackageService = layoutPackageService ?? throw new ArgumentNullException(nameof(layoutPackageService));
        _thumbnailProvider = thumbnailProvider ?? throw new ArgumentNullException(nameof(thumbnailProvider));
        _namedViewThumbnailProvider = namedViewThumbnailProvider ??
            throw new ArgumentNullException(nameof(namedViewThumbnailProvider));
        _draftLayoutThumbnailProvider = draftLayoutThumbnailProvider ??
            throw new ArgumentNullException(nameof(draftLayoutThumbnailProvider));
        _capabilityProvider = capabilityProvider ?? throw new ArgumentNullException(nameof(capabilityProvider));
        _templateCaptureContextProvider = templateCaptureContextProvider ??
            throw new ArgumentNullException(nameof(templateCaptureContextProvider));
        _observerSnapshotProvider = observerSnapshotProvider ??
            throw new ArgumentNullException(nameof(observerSnapshotProvider));
        _modelObjectSelectionService = modelObjectSelectionService ??
            throw new ArgumentNullException(nameof(modelObjectSelectionService));
        _projectIcon?.Dispose();
        _projectIcon = projectIcon;
        NotifyOverviewChanged(OverviewInvalidation.All);
    }

    public Image? ProjectIcon => _projectIcon;

    public DocumentSelectionState Selection => SharedSelection;

    public ObserverSnapshot CaptureObserverSnapshot()
    {
        try
        {
            return _observerSnapshotProvider?.Capture() ?? ObserverSnapshot.NoDocument;
        }
        catch (InvalidOperationException)
        {
            return ObserverSnapshot.NoDocument;
        }
    }

    public DocumentOverview CaptureOverview()
    {
        return _overviewProvider?.Capture() ?? DocumentOverview.NoDocument;
    }

    public DocumentOverviewIdentity CaptureOverviewIdentity()
    {
        return _overviewProvider?.CaptureIdentity() ??
               new DocumentOverviewIdentity(null, 0, DocumentOverview.NoDocument.DocumentName);
    }

    public (uint DocumentRuntimeSerialNumber, long Revision)? CaptureDocumentContext()
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

    public DocumentSnapshot? CaptureSnapshot()
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

    public TemplateCaptureContext? CaptureTemplateContext(Guid sourcePageViewId)
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

    public void NotifyOverviewChanged(OverviewInvalidation? invalidation = null)
    {
        _overviewChanged?.Invoke(
            null,
            new OverviewInvalidationEventArgs(invalidation ?? OverviewInvalidation.All));
    }

    public OverviewNavigationResult Navigate(OverviewNavigationTarget target)
    {
        return _navigationService?.Navigate(target) ??
               new OverviewNavigationResult(false, "Foundry is not connected to an active Rhino plug-in.");
    }

    public OverviewNavigationResult DuplicateSheet(Guid sheetPageViewId)
    {
        var result = _navigationService?.DuplicateSheet(sheetPageViewId) ??
                     new OverviewNavigationResult(false, "Foundry is not connected to an active Rhino plug-in.");
        if (result.Succeeded)
        {
            NotifyOverviewChanged(OverviewInvalidation.All);
        }

        return result;
    }

    public OverviewNavigationResult DeleteSheet(Guid sheetPageViewId)
    {
        var result = _navigationService?.DeleteSheet(sheetPageViewId) ??
                     new OverviewNavigationResult(false, "Foundry is not connected to an active Rhino plug-in.");
        if (result.Succeeded)
        {
            NotifyOverviewChanged(OverviewInvalidation.All);
        }

        return result;
    }

    public OverviewNavigationResult RenameSheetDirect(Guid sheetPageViewId, string newName)
    {
        var result = _navigationService?.RenameSheet(sheetPageViewId, newName) ??
                     new OverviewNavigationResult(false, "Foundry is not connected to an active Rhino plug-in.");
        if (result.Succeeded)
        {
            NotifyOverviewChanged(OverviewInvalidation.All);
        }

        return result;
    }

    public OverviewNavigationResult RunSheetCommand(Guid sheetPageViewId, LayoutSheetCommand command)
    {
        return _navigationService?.RunSheetCommand(sheetPageViewId, command) ??
               new OverviewNavigationResult(false, "Foundry is not connected to an active Rhino plug-in.");
    }

    public Task<LayoutPdfExportResult> ExportPdfAsync(
        LayoutPdfExportRequest request,
        CancellationToken cancellationToken = default)
    {
        return _pdfExportService?.ExportAsync(request, cancellationToken) ??
               Task.FromResult(new LayoutPdfExportResult(
                   false,
                   0,
                   "Foundry is not connected to a PDF export service."));
    }

    public OverviewNavigationResult ShowPrintDialog(LayoutPrintDialogRequest request)
    {
        return _printDialogService?.Show(request) ??
               new OverviewNavigationResult(
                   false,
                   "Foundry is not connected to Rhino's print dialog.");
    }

    public Task<LayoutPackageExportResult> ExportLayoutPackageAsync(
        LayoutPackageExportRequest request,
        CancellationToken cancellationToken = default) =>
        _layoutPackageService?.ExportAsync(request, cancellationToken) ??
        Task.FromResult(new LayoutPackageExportResult(
            false, 0, "Foundry is not connected to a layout package service."));

    public Task<LayoutPackagePreflight> PreflightLayoutPackageAsync(
        string filePath,
        CancellationToken cancellationToken = default) =>
        _layoutPackageService?.PreflightAsync(filePath, cancellationToken) ??
        Task.FromResult(new LayoutPackagePreflight(
            false, filePath, null, [], [], "Foundry is not connected to a layout package service."));

    public Task<LayoutPackageImportResult> ImportLayoutPackageAsync(
        LayoutPackageImportRequest request,
        CancellationToken cancellationToken = default) =>
        _layoutPackageService?.ImportAsync(request, cancellationToken) ??
        Task.FromResult(new LayoutPackageImportResult(
            false, 0, [], "Foundry is not connected to a layout package service."));

    public Task<OverviewThumbnailResult> CaptureThumbnailAsync(
        OverviewThumbnailRequest request,
        CancellationToken cancellationToken = default)
    {
        return _thumbnailProvider?.CaptureAsync(request, cancellationToken) ??
               Task.FromResult(new OverviewThumbnailResult(
                   request.Key,
                   null,
                   "Foundry is not connected to a thumbnail provider."));
    }

    public Task<NamedViewThumbnailResult> CaptureNamedViewThumbnailAsync(
        NamedViewThumbnailRequest request,
        CancellationToken cancellationToken = default)
    {
        return _namedViewThumbnailProvider?.CaptureAsync(request, cancellationToken) ??
               Task.FromResult(new NamedViewThumbnailResult(
                   request.Key,
                   null,
               "Foundry is not connected to a named-view thumbnail provider."));
    }

    public Task<DraftLayoutThumbnailResult> CaptureDraftLayoutThumbnailAsync(
        DraftLayoutThumbnailRequest request,
        CancellationToken cancellationToken = default)
    {
        return _draftLayoutThumbnailProvider?.CaptureAsync(request, cancellationToken) ??
               Task.FromResult(new DraftLayoutThumbnailResult(
                   request.Key,
                   null,
                   "Foundry is not connected to a draft-layout thumbnail provider."));
    }

    public void BeginDraftLayoutThumbnailSession(uint documentRuntimeSerialNumber) =>
        _draftLayoutThumbnailProvider?.BeginSession(documentRuntimeSerialNumber);

    public Task<EditSheetThumbnailResult> CaptureEditSheetThumbnailAsync(
        EditSheetThumbnailRequest request,
        CancellationToken cancellationToken = default)
    {
        return _draftLayoutThumbnailProvider?.CaptureEditAsync(request, cancellationToken) ??
               Task.FromResult(new EditSheetThumbnailResult(
                   request.Key,
                   null,
                   "Foundry is not connected to an edit-sheet thumbnail provider."));
    }

    public Task CompleteDraftLayoutThumbnailSessionAsync(
        uint documentRuntimeSerialNumber,
        bool restoreOriginalModifiedState,
        bool endSession = true,
        CancellationToken cancellationToken = default)
    {
        return _draftLayoutThumbnailProvider?.CompleteSessionAsync(
                   documentRuntimeSerialNumber,
                   restoreOriginalModifiedState,
                   endSession,
                   cancellationToken) ??
               Task.CompletedTask;
    }

    public FoundryMutationCapabilities CaptureMutationCapabilities()
    {
        return _capabilityProvider?.Capture() ?? FoundryMutationCapabilities.Unavailable;
    }

    public void Reset()
    {
        _overviewProvider = null;
        _snapshotProvider = null;
        _mutationService = null;
        _navigationService = null;
        _pdfExportService = null;
        _printDialogService = null;
        _layoutPackageService = null;
        _thumbnailProvider = null;
        _namedViewThumbnailProvider = null;
        _draftLayoutThumbnailProvider = null;
        _capabilityProvider = null;
        _templateCaptureContextProvider = null;
        _observerSnapshotProvider = null;
        _modelObjectSelectionService = null;
        _projectIcon?.Dispose();
        _projectIcon = null;
        _overviewChanged = null;
        SharedSelection.Clear(null);
    }

    private async Task<OperationResult> RunOperationAsync(
        Func<DocumentSnapshot, Task<OperationResult>> operation, CancellationToken cancellationToken)
    {
        if (_snapshotProvider is null || _mutationService is null)
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await operation(_snapshotProvider.Capture());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return UnavailableResult("The operation was cancelled.");
        }
        catch (InvalidOperationException exception) { return UnavailableResult(exception.Message); }
    }

    private OperationResult UnavailableResult(string message)
    {
        return new OperationResult(
            false,
            [new Diagnostic("ui.unavailable", DiagnosticSeverity.Error, message)]);
    }

    private async Task<OperationResult> ApplyHierarchyPlanAsync(
        OperationPlan plan,
        uint documentRuntimeSerialNumber,
        IReadOnlySet<Guid> affectedEntityIds,
        CancellationToken cancellationToken)
    {
        if (!plan.CanApply)
        {
            return new OperationResult(false, plan.Diagnostics);
        }

        var result = await Mutations.ApplyAsync(plan, cancellationToken);
        if (result.Succeeded)
        {
            var affected = affectedEntityIds
                .Concat(plan.Changes.OfType<UpdateLinkedSheetNamesChange>()
                    .SelectMany(change => change.NewNames.Keys.Concat(change.NewBindings.Keys)))
                .ToHashSet();
            NotifyOverviewChanged(new OverviewInvalidation(
                documentRuntimeSerialNumber,
                OverviewInvalidationKind.Hierarchy |
                OverviewInvalidationKind.Metadata |
                OverviewInvalidationKind.Diagnostics,
                affected));
        }

        return result;
    }
}

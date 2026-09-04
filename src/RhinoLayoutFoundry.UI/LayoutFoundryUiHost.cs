using Eto.Drawing;
using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Observer;
using RhinoLayoutFoundry.Core.Overview;
using RhinoLayoutFoundry.Core.Persistence;

namespace RhinoLayoutFoundry.UI;

/// <summary>Rhino composition entry point; application state and workflows belong to the service instance.</summary>
public static class LayoutFoundryUiHost
{
    private static readonly FoundryApplicationService Service = new();
    public static event EventHandler<OverviewInvalidationEventArgs> OverviewChanged
    {
        add => Service.OverviewChanged += value;
        remove => Service.OverviewChanged -= value;
    }
    public static Image? ProjectIcon => Service.ProjectIcon;
    public static DocumentSelectionState Selection => Service.Selection;

    public static void Configure(
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
        Image? projectIcon = null) =>
        Service.Configure(overviewProvider, snapshotProvider, mutationService, navigationService, pdfExportService, printDialogService, layoutPackageService, thumbnailProvider, namedViewThumbnailProvider, draftLayoutThumbnailProvider, capabilityProvider, templateCaptureContextProvider, observerSnapshotProvider, modelObjectSelectionService, projectIcon);

    public static ObserverSnapshot CaptureObserverSnapshot() =>
        Service.CaptureObserverSnapshot();

    public static DocumentOverview CaptureOverview() =>
        Service.CaptureOverview();

    public static DocumentOverviewIdentity CaptureOverviewIdentity() =>
        Service.CaptureOverviewIdentity();

    public static (uint DocumentRuntimeSerialNumber, long Revision)? CaptureDocumentContext() =>
        Service.CaptureDocumentContext();

    public static DocumentSnapshot? CaptureSnapshot() =>
        Service.CaptureSnapshot();

    public static TemplateCaptureContext? CaptureTemplateContext(Guid sourcePageViewId) =>
        Service.CaptureTemplateContext(sourcePageViewId);

    public static void NotifyOverviewChanged(OverviewInvalidation? invalidation = null) =>
        Service.NotifyOverviewChanged(invalidation);

    public static OverviewNavigationResult Navigate(OverviewNavigationTarget target) =>
        Service.Navigate(target);

    public static OverviewNavigationResult DuplicateSheet(Guid sheetPageViewId) =>
        Service.DuplicateSheet(sheetPageViewId);

    public static OverviewNavigationResult DeleteSheet(Guid sheetPageViewId) =>
        Service.DeleteSheet(sheetPageViewId);

    public static OverviewNavigationResult RenameSheetDirect(Guid sheetPageViewId, string newName) =>
        Service.RenameSheetDirect(sheetPageViewId, newName);

    public static OverviewNavigationResult RunSheetCommand(Guid sheetPageViewId, LayoutSheetCommand command) =>
        Service.RunSheetCommand(sheetPageViewId, command);

    public static Task<LayoutPdfExportResult> ExportPdfAsync(
        LayoutPdfExportRequest request,
        CancellationToken cancellationToken = default) =>
        Service.ExportPdfAsync(request, cancellationToken);

    public static OverviewNavigationResult ShowPrintDialog(LayoutPrintDialogRequest request) =>
        Service.ShowPrintDialog(request);

    public static Task<LayoutPackageExportResult> ExportLayoutPackageAsync(
        LayoutPackageExportRequest request,
        CancellationToken cancellationToken = default) =>
        Service.ExportLayoutPackageAsync(request, cancellationToken);

    public static Task<LayoutPackagePreflight> PreflightLayoutPackageAsync(
        string filePath,
        CancellationToken cancellationToken = default) =>
        Service.PreflightLayoutPackageAsync(filePath, cancellationToken);

    public static Task<LayoutPackageImportResult> ImportLayoutPackageAsync(
        LayoutPackageImportRequest request,
        CancellationToken cancellationToken = default) =>
        Service.ImportLayoutPackageAsync(request, cancellationToken);

    public static Task<OverviewThumbnailResult> CaptureThumbnailAsync(
        OverviewThumbnailRequest request,
        CancellationToken cancellationToken = default) =>
        Service.CaptureThumbnailAsync(request, cancellationToken);

    public static Task<NamedViewThumbnailResult> CaptureNamedViewThumbnailAsync(
        NamedViewThumbnailRequest request,
        CancellationToken cancellationToken = default) =>
        Service.CaptureNamedViewThumbnailAsync(request, cancellationToken);

    public static Task<DraftLayoutThumbnailResult> CaptureDraftLayoutThumbnailAsync(
        DraftLayoutThumbnailRequest request,
        CancellationToken cancellationToken = default) =>
        Service.CaptureDraftLayoutThumbnailAsync(request, cancellationToken);

    public static void BeginDraftLayoutThumbnailSession(uint documentRuntimeSerialNumber) =>
        Service.BeginDraftLayoutThumbnailSession(documentRuntimeSerialNumber);

    public static Task<EditSheetThumbnailResult> CaptureEditSheetThumbnailAsync(
        EditSheetThumbnailRequest request,
        CancellationToken cancellationToken = default) =>
        Service.CaptureEditSheetThumbnailAsync(request, cancellationToken);

    public static Task CompleteDraftLayoutThumbnailSessionAsync(
        uint documentRuntimeSerialNumber,
        bool restoreOriginalModifiedState,
        bool endSession = true,
        CancellationToken cancellationToken = default) =>
        Service.CompleteDraftLayoutThumbnailSessionAsync(documentRuntimeSerialNumber, restoreOriginalModifiedState, endSession, cancellationToken);

    public static FoundryMutationCapabilities CaptureMutationCapabilities() =>
        Service.CaptureMutationCapabilities();

    public static Task<OperationResult> RenameSheetAsync(
        Guid pageViewId,
        string expectedName,
        string newName,
        CancellationToken cancellationToken = default) =>
        Service.RenameSheetAsync(pageViewId, expectedName, newName, cancellationToken);

    public static Task<OperationResult> CreateFolderAsync(
        Guid folderId,
        Guid parentFolderId,
        string name,
        CancellationToken cancellationToken = default) =>
        Service.CreateFolderAsync(folderId, parentFolderId, name, cancellationToken);

    public static Task<OperationResult> RenameFolderAsync(
        Guid folderId,
        string expectedName,
        string newName,
        CancellationToken cancellationToken = default) =>
        Service.RenameFolderAsync(folderId, expectedName, newName, cancellationToken);

    public static Task<OperationResult> DeleteFolderAsync(
        Guid folderId,
        string expectedName,
        CancellationToken cancellationToken = default) =>
        Service.DeleteFolderAsync(folderId, expectedName, cancellationToken);

    public static Task<OperationResult> DuplicateFolderAsync(
        Guid folderId,
        string expectedName,
        CancellationToken cancellationToken = default) =>
        Service.DuplicateFolderAsync(folderId, expectedName, cancellationToken);

    public static Task<OperationResult> DuplicateSelectionAsync(
        IReadOnlyList<OverviewNodeKey> selection,
        CancellationToken cancellationToken = default) =>
        Service.DuplicateSelectionAsync(selection, cancellationToken);

    public static Task<OperationResult> PasteSelectionAsync(
        uint sourceDocumentRuntimeSerialNumber,
        IReadOnlyList<OverviewNodeKey> selection,
        Guid destinationFolderId,
        ObserverPointRecord? canvasTargetOrigin = null,
        CancellationToken cancellationToken = default) =>
        Service.PasteSelectionAsync(sourceDocumentRuntimeSerialNumber, selection, destinationFolderId, canvasTargetOrigin, cancellationToken);

    public static Task<OperationResult> DeleteSelectionAsync(
        IReadOnlyList<OverviewNodeKey> selection,
        CancellationToken cancellationToken = default) =>
        Service.DeleteSelectionAsync(selection, cancellationToken);

    public static Task<OperationResult> MoveSheetsAsync(
        Guid destinationFolderId,
        IReadOnlyList<Guid> sheetPageViewIds,
        CancellationToken cancellationToken = default) =>
        Service.MoveSheetsAsync(destinationFolderId, sheetPageViewIds, cancellationToken);

    public static Task<OperationResult> MoveFoldersAsync(
        Guid destinationFolderId,
        IReadOnlyList<Guid> folderIds,
        CancellationToken cancellationToken = default) =>
        Service.MoveFoldersAsync(destinationFolderId, folderIds, cancellationToken);

    public static Task<OperationResult> MoveHierarchySelectionAsync(
        Guid destinationFolderId,
        IReadOnlyList<Guid> folderIds,
        IReadOnlyList<Guid> sheetIds,
        CancellationToken cancellationToken = default) =>
        Service.MoveHierarchySelectionAsync(destinationFolderId, folderIds, sheetIds, cancellationToken);

    public static Task<OperationResult> ReorganizeHierarchyAsync(
        IReadOnlyList<Guid> folderIds,
        IReadOnlyList<Guid> sheetIds,
        HierarchyPlacementTarget target,
        CancellationToken cancellationToken = default) =>
        Service.ReorganizeHierarchyAsync(folderIds, sheetIds, target, cancellationToken);

    public static Task<OperationResult> CreateSheetAsync(
        Guid destinationFolderId,
        string name,
        CancellationToken cancellationToken = default) =>
        Service.CreateSheetAsync(destinationFolderId, name, cancellationToken);

    public static Task<OperationResult> CaptureSheetTemplateAsync(
        Guid sourcePageViewId,
        string name,
        string defaultNamingPattern,
        Guid? titleBlockInstanceObjectId,
        CancellationToken cancellationToken = default) =>
        Service.CaptureSheetTemplateAsync(sourcePageViewId, name, defaultNamingPattern, titleBlockInstanceObjectId, cancellationToken);

    public static Task<OperationResult> SetSheetTemplateRegistrationAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        bool registered,
        CancellationToken cancellationToken = default) =>
        Service.SetSheetTemplateRegistrationAsync(targets, registered, cancellationToken);

    public static Task<OperationResult> SetTemplateCapabilitiesAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        TemplateCapability capabilities,
        CancellationToken cancellationToken = default) =>
        Service.SetTemplateCapabilitiesAsync(targets, capabilities, cancellationToken);

    public static Task<OperationResult> SetLayerVisibilityRulesAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        IReadOnlyList<Guid> layerIds,
        LayerVisibilityOverride? visibility,
        CancellationToken cancellationToken = default) =>
        Service.SetLayerVisibilityRulesAsync(targets, layerIds, visibility, cancellationToken);

    public static Task<OperationResult> SetObjectDisplayRulesAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        IReadOnlyList<ObjectDisplayRule> rules,
        CancellationToken cancellationToken = default) =>
        Service.SetObjectDisplayRulesAsync(targets, rules, cancellationToken);

    public static Task<OperationResult> SetAppearanceRulesAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        IReadOnlyList<LayerVisibilityRule> layerRules,
        IReadOnlyList<ObjectDisplayRule> objectRules,
        CancellationToken cancellationToken = default) =>
        Service.SetAppearanceRulesAsync(targets, layerRules, objectRules, cancellationToken);

    public static Task<OperationResult> CreateAppearanceStateAsync(
        Guid folderId,
        string name,
        IReadOnlyList<LayerVisibilityRule>? layerRules = null,
        IReadOnlyList<ObjectDisplayRule>? objectRules = null,
        string notes = "",
        CancellationToken cancellationToken = default) =>
        Service.CreateAppearanceStateAsync(folderId, name, layerRules, objectRules, notes, cancellationToken);

    public static Task<OperationResult> UpdateAppearanceStateAsync(
        Guid stateId,
        string? name = null,
        IReadOnlyList<LayerVisibilityRule>? layerRules = null,
        IReadOnlyList<ObjectDisplayRule>? objectRules = null,
        string? notes = null,
        CancellationToken cancellationToken = default) =>
        Service.UpdateAppearanceStateAsync(stateId, name, layerRules, objectRules, notes, cancellationToken);

    public static Task<OperationResult> AssignAppearanceStateAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        Guid? stateId,
        CancellationToken cancellationToken = default) =>
        Service.AssignAppearanceStateAsync(targets, stateId, cancellationToken);

    public static ModelObjectSelectionResult PickModelObjects() =>
        Service.PickModelObjects();

    public static Task<OperationResult> MoveAppearanceStatesAsync(
        IReadOnlyList<Guid> stateIds,
        Guid destinationFolderId,
        CancellationToken cancellationToken = default) =>
        Service.MoveAppearanceStatesAsync(stateIds, destinationFolderId, cancellationToken);

    public static Task<OperationResult> LinkTemplateCapabilityAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        Guid sourceRegistrationId,
        TemplateCapability capability,
        CancellationToken cancellationToken = default) =>
        Service.LinkTemplateCapabilityAsync(targets, sourceRegistrationId, capability, cancellationToken);

    public static Task<OperationResult> DetachTemplateCapabilityAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        TemplateCapability capability,
        CancellationToken cancellationToken = default) =>
        Service.DetachTemplateCapabilityAsync(targets, capability, cancellationToken);

    internal static HierarchyScope ToHierarchyScope(OverviewNodeKey key) =>
        Service.ToHierarchyScope(key);

    public static Task<OperationResult> SetSheetTemplateRegistrationAsync(
        Guid sourcePageViewId,
        bool registered,
        CancellationToken cancellationToken = default) =>
        Service.SetSheetTemplateRegistrationAsync(sourcePageViewId, registered, cancellationToken);

    public static Task<OperationResult> BatchCreateSheetsAsync(
        BatchCreateSheetsRequest request,
        CancellationToken cancellationToken = default) =>
        Service.BatchCreateSheetsAsync(request, cancellationToken);

    public static Task<OperationResult> UpdateProjectInformationAsync(
        ProjectInformation information,
        CancellationToken cancellationToken = default) =>
        Service.UpdateProjectInformationAsync(information, cancellationToken);

    public static Task<OperationResult> BatchUpdateSheetsAsync(
        BatchUpdateSheetsRequest request,
        CancellationToken cancellationToken = default) =>
        Service.BatchUpdateSheetsAsync(request, cancellationToken);

    public static Task<OperationResult> SetDisplayModeAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        Guid displayModeId,
        CancellationToken cancellationToken = default) =>
        Service.SetDisplayModeAsync(targets, displayModeId, cancellationToken);

    public static Task<OperationResult> SetPaperSizeAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        double width,
        double height,
        string unitSystem,
        CancellationToken cancellationToken = default) =>
        Service.SetPaperSizeAsync(targets, width, height, unitSystem, cancellationToken);

    public static Task<OperationResult> SetPrintInclusionAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        bool include,
        CancellationToken cancellationToken = default) =>
        Service.SetPrintInclusionAsync(targets, include, cancellationToken);

    public static Task<OperationResult> SetObserverCanvasStateAsync(
        ObserverCanvasState newState,
        string undoDescription = "Organize observer canvas",
        CancellationToken cancellationToken = default) =>
        Service.SetObserverCanvasStateAsync(newState, undoDescription, cancellationToken);

    public static Task<OperationResult> UpdateHierarchyNotesAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        string notes,
        CancellationToken cancellationToken = default) =>
        Service.UpdateHierarchyNotesAsync(targets, notes, cancellationToken);

    public static Task<OperationResult> AssignNamedViewAsync(
        IReadOnlyList<Guid> detailViewportIds,
        string namedViewName,
        CancellationToken cancellationToken = default) =>
        Service.AssignNamedViewAsync(detailViewportIds, namedViewName, cancellationToken);

    public static Task<OperationResult> ReorderSheetAsync(
        Guid movingSheetId,
        Guid? beforeSheetId,
        CancellationToken cancellationToken = default) =>
        Service.ReorderSheetAsync(movingSheetId, beforeSheetId, cancellationToken);

    public static void Reset() =>
        Service.Reset();
}

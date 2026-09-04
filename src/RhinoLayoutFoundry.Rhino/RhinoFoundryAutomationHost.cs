using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;
using RhinoLayoutFoundry.Extensibility;

namespace RhinoLayoutFoundry.Rhino;

internal sealed class RhinoFoundryAutomationHost : IFoundryAutomationHost
{
    private readonly AutomationPlanRegistry _plans;
    private readonly IDocumentSnapshotProvider _snapshotProvider;
    private readonly IDocumentThumbnailProvider _thumbnailProvider;
    private readonly INamedViewThumbnailProvider _namedViewThumbnailProvider;

    public RhinoFoundryAutomationHost(
        IDocumentSnapshotProvider snapshotProvider,
        IDocumentMutationService mutationService,
        IDocumentThumbnailProvider thumbnailProvider,
        INamedViewThumbnailProvider namedViewThumbnailProvider)
    {
        _plans = new AutomationPlanRegistry(snapshotProvider, mutationService);
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
        ArgumentNullException.ThrowIfNull(mutationService);
        _thumbnailProvider = thumbnailProvider ?? throw new ArgumentNullException(nameof(thumbnailProvider));
        _namedViewThumbnailProvider = namedViewThumbnailProvider ??
            throw new ArgumentNullException(nameof(namedViewThumbnailProvider));
    }

    public AutomationCapabilities GetCapabilities() => new(
        FoundryAutomationProtocol.MajorVersion,
        FoundryAutomationProtocol.MinorVersion,
        CanInspectDocument: true,
        CanCaptureLayouts: true,
        CanCaptureNamedViews: true,
        CanCreateNamedViews: true,
        CanCreateClippingPlanes: true,
        CanCreateLayouts: true,
        CanAssignNamedViews: true,
        CanManageAppearanceStates: true,
        CanExportPdf: false,
        Limitations:
        [
            "Layout creation is rollback-protected but is not natively undoable in Rhino 8.",
            "Arbitrary deletion/rename plans are unavailable; appearance resource edits and linked naming are supported.",
            "PDF export is not exposed through the automation host in protocol 1.0.",
        ]);

    public DocumentSnapshot CaptureSnapshot() => _snapshotProvider.Capture();

    public async Task<AutomationCaptureResult> CaptureAsync(
        AutomationCaptureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Width is < 64 or > 4096 || request.Height is < 64 or > 4096)
            return AutomationCaptureResult.Failure("Capture dimensions must be between 64 and 4096 pixels.");
        var snapshot = _snapshotProvider.Capture();
        switch (request.Kind)
        {
            case AutomationCaptureKind.Layout when request.SheetPageViewId is { } sheetId:
            {
                if (!snapshot.Sheets.ContainsKey(sheetId))
                    return AutomationCaptureResult.Failure("The requested layout no longer exists.");
                var result = await _thumbnailProvider.CaptureAsync(
                    new OverviewThumbnailRequest(
                        new OverviewThumbnailKey(
                            snapshot.DocumentRuntimeSerialNumber,
                            sheetId,
                            request.Width,
                            request.Height,
                            snapshot.Revision,
                            BackgroundArgb: request.BackgroundArgb),
                        Priority: 0),
                    cancellationToken);
                return result.Succeeded
                    ? new AutomationCaptureResult(true, "image/png", result.PngBytes, "")
                    : AutomationCaptureResult.Failure(result.Error ?? "Rhino did not return a layout capture.");
            }
            case AutomationCaptureKind.NamedView when !string.IsNullOrWhiteSpace(request.NamedViewName):
            {
                var name = request.NamedViewName.Trim();
                if (!snapshot.NamedViews.Contains(name))
                    return AutomationCaptureResult.Failure("The requested named view no longer exists.");
                var result = await _namedViewThumbnailProvider.CaptureAsync(
                    new NamedViewThumbnailRequest(
                        new NamedViewThumbnailKey(
                            snapshot.DocumentRuntimeSerialNumber,
                            name,
                            request.Width,
                            request.Height,
                            snapshot.Revision,
                            BackgroundArgb: request.BackgroundArgb)),
                    cancellationToken);
                return result.Succeeded
                    ? new AutomationCaptureResult(true, "image/png", result.PngBytes, "")
                    : AutomationCaptureResult.Failure(result.Error ?? "Rhino did not return a named-view capture.");
            }
            default:
                return AutomationCaptureResult.Failure("The capture target is incomplete or unsupported.");
        }
    }

    public AutomationPlanEnvelope StagePlan(OperationPlan plan) => _plans.StagePlan(plan);
    public AutomationApproval ApprovePlan(Guid planId) => _plans.ApprovePlan(planId);
    public Task<OperationResult> ApplyApprovedPlanAsync(AutomationApproval approval, CancellationToken cancellationToken) =>
        _plans.ApplyApprovedPlanAsync(approval, cancellationToken);
    public void AbandonPlan(Guid planId) => _plans.AbandonPlan(planId);
}

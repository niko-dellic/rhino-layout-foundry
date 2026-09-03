using System.Security.Cryptography;
using System.Text;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;
using RhinoLayoutFoundry.Extensibility;

namespace RhinoLayoutFoundry.Rhino;

internal sealed class RhinoFoundryAutomationHost : IFoundryAutomationHost
{
    private static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(15);
    private readonly object _syncRoot = new();
    private readonly Dictionary<Guid, PendingPlan> _plans = new();
    private readonly IDocumentSnapshotProvider _snapshotProvider;
    private readonly IDocumentMutationService _mutationService;
    private readonly IDocumentThumbnailProvider _thumbnailProvider;
    private readonly INamedViewThumbnailProvider _namedViewThumbnailProvider;

    public RhinoFoundryAutomationHost(
        IDocumentSnapshotProvider snapshotProvider,
        IDocumentMutationService mutationService,
        IDocumentThumbnailProvider thumbnailProvider,
        INamedViewThumbnailProvider namedViewThumbnailProvider)
    {
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
        _mutationService = mutationService ?? throw new ArgumentNullException(nameof(mutationService));
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
            "Automation cannot delete or rename existing document content.",
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

    public AutomationPlanEnvelope StagePlan(OperationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.CanApply)
            throw new InvalidOperationException("Only an applicable operation plan can be staged.");
        var snapshot = _snapshotProvider.Capture();
        if (snapshot.DocumentRuntimeSerialNumber != plan.DocumentRuntimeSerialNumber ||
            snapshot.Revision != plan.SourceRevision)
            throw new InvalidOperationException("The Rhino document changed before the plan was staged.");
        if (plan.Changes.Count == 0 || plan.Changes.Any(change => !IsAllowed(change)))
            throw new InvalidOperationException("The plan contains an operation that automation is not allowed to apply.");

        var now = DateTimeOffset.UtcNow;
        var envelope = new AutomationPlanEnvelope(
            Guid.NewGuid(),
            plan.DocumentRuntimeSerialNumber,
            plan.SourceRevision,
            plan.UndoDescription,
            AutomationApprovalRequirement.DocumentMutation,
            now + PlanLifetime,
            plan);
        lock (_syncRoot)
        {
            RemoveExpired(now);
            _plans[envelope.PlanId] = new PendingPlan(envelope, null);
        }
        return envelope;
    }

    public AutomationApproval ApprovePlan(Guid planId)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_syncRoot)
        {
            RemoveExpired(now);
            if (!_plans.TryGetValue(planId, out var pending))
                throw new InvalidOperationException("The automation plan is missing or expired.");
            var snapshot = _snapshotProvider.Capture();
            if (snapshot.DocumentRuntimeSerialNumber != pending.Envelope.DocumentRuntimeSerialNumber ||
                snapshot.Revision != pending.Envelope.SourceRevision)
            {
                _plans.Remove(planId);
                throw new InvalidOperationException("The Rhino document changed before approval.");
            }
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var approval = new AutomationApproval(planId, token, pending.Envelope.ExpiresAt);
            _plans[planId] = pending with { ApprovalToken = token };
            return approval;
        }
    }

    public async Task<OperationResult> ApplyApprovedPlanAsync(
        AutomationApproval approval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approval);
        PendingPlan pending;
        lock (_syncRoot)
        {
            RemoveExpired(DateTimeOffset.UtcNow);
            if (!_plans.TryGetValue(approval.PlanId, out pending!) || pending.ApprovalToken is null ||
                !TokensEqual(pending.ApprovalToken, approval.Token))
                throw new InvalidOperationException("The automation approval is missing, expired, or invalid.");
            _plans.Remove(approval.PlanId);
        }
        return await _mutationService.ApplyAsync(pending.Envelope.Plan, cancellationToken);
    }

    public void AbandonPlan(Guid planId)
    {
        lock (_syncRoot) _plans.Remove(planId);
    }

    private static bool IsAllowed(OperationChange change) => change is
        CreateNamedViewChange or
        CreateClippingPlaneChange or
        CreateSheetFromTemplateChange or
        AssignNamedViewToDetailsChange or
        UpdateLinkedSheetNamesChange or
        SetAppearanceStateResourceChange or
        SetAppearanceStateAssignmentChange;

    private static bool TokensEqual(string expected, string actual)
    {
        var left = Encoding.UTF8.GetBytes(expected);
        var right = Encoding.UTF8.GetBytes(actual ?? string.Empty);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (var planId in _plans.Where(pair => pair.Value.Envelope.ExpiresAt <= now)
                     .Select(pair => pair.Key).ToArray())
            _plans.Remove(planId);
    }

    private sealed record PendingPlan(AutomationPlanEnvelope Envelope, string? ApprovalToken);
}

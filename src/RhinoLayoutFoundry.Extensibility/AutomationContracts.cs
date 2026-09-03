using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Extensibility;

public static class FoundryAutomationProtocol
{
    public const int MajorVersion = 1;
    public const int MinorVersion = 0;
}

public sealed record AutomationCapabilities(
    int ProtocolMajor,
    int ProtocolMinor,
    bool CanInspectDocument,
    bool CanCaptureLayouts,
    bool CanCaptureNamedViews,
    bool CanCreateNamedViews,
    bool CanCreateClippingPlanes,
    bool CanCreateLayouts,
    bool CanAssignNamedViews,
    bool CanManageAppearanceStates,
    bool CanExportPdf,
    IReadOnlyList<string> Limitations);

public enum AutomationCaptureKind
{
    Layout,
    NamedView,
}

public sealed record AutomationCaptureRequest(
    AutomationCaptureKind Kind,
    Guid? SheetPageViewId,
    string? NamedViewName,
    int Width,
    int Height,
    uint BackgroundArgb = 0xfff5f5f5);

public sealed record AutomationCaptureResult(
    bool Succeeded,
    string MediaType,
    byte[]? Content,
    string Message)
{
    public static AutomationCaptureResult Failure(string message) =>
        new(false, "image/png", null, message);
}

public enum AutomationApprovalRequirement
{
    None,
    DataSharing,
    DocumentMutation,
    FileWrite,
}

public sealed record AutomationPlanEnvelope(
    Guid PlanId,
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    string Summary,
    AutomationApprovalRequirement ApprovalRequirement,
    DateTimeOffset ExpiresAt,
    OperationPlan Plan);

public sealed record AutomationApproval(
    Guid PlanId,
    string Token,
    DateTimeOffset ExpiresAt);

public interface IFoundryAutomationHost
{
    AutomationCapabilities GetCapabilities();

    DocumentSnapshot CaptureSnapshot();

    Task<AutomationCaptureResult> CaptureAsync(
        AutomationCaptureRequest request,
        CancellationToken cancellationToken);

    AutomationPlanEnvelope StagePlan(OperationPlan plan);

    AutomationApproval ApprovePlan(Guid planId);

    Task<OperationResult> ApplyApprovedPlanAsync(
        AutomationApproval approval,
        CancellationToken cancellationToken);

    void AbandonPlan(Guid planId);
}

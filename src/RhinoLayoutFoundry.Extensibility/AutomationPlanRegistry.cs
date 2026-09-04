using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Extensibility;

/// <summary>
/// Experimental trusted-companion staging. Approval is a caller assertion of user consent,
/// not a sandbox against other in-process plug-ins. Tokens are single-use and revision-bound.
/// </summary>
public sealed class AutomationPlanRegistry(
    IDocumentSnapshotProvider snapshotProvider,
    IDocumentMutationService mutationService,
    TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(15);
    private readonly object _syncRoot = new();
    private readonly Dictionary<Guid, PendingPlan> _plans = new();
    private readonly IDocumentSnapshotProvider _snapshotProvider = snapshotProvider;
    private readonly IDocumentMutationService _mutationService = mutationService;
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

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

        plan = Freeze(plan);
        var now = _clock.GetUtcNow();
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
        return envelope with { Plan = Freeze(plan) };
    }

    public AutomationApproval ApprovePlan(Guid planId)
    {
        var now = _clock.GetUtcNow();
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
            RemoveExpired(_clock.GetUtcNow());
            if (!_plans.TryGetValue(approval.PlanId, out pending!) || pending.ApprovalToken is null ||
                !TokensEqual(pending.ApprovalToken, approval.Token))
                throw new InvalidOperationException("The automation approval is missing, expired, or invalid.");
            _plans.Remove(approval.PlanId);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = _snapshotProvider.Capture();
        if (snapshot.DocumentRuntimeSerialNumber != pending.Envelope.DocumentRuntimeSerialNumber ||
            snapshot.Revision != pending.Envelope.SourceRevision)
            throw new InvalidOperationException("The Rhino document changed after approval.");
        return await _mutationService.ApplyAsync(pending.Envelope.Plan, cancellationToken);
    }

    public void AbandonPlan(Guid planId)
    {
        lock (_syncRoot) _plans.Remove(planId);
    }

    private static OperationPlan Freeze(OperationPlan plan) => plan with
    {
        Changes = plan.Changes.Select(change => (OperationChange)(JsonSerializer.Deserialize(
            JsonSerializer.Serialize(change, change.GetType()), change.GetType())
            ?? throw new InvalidOperationException("The automation change could not be frozen."))).ToArray(),
        Diagnostics = plan.Diagnostics.ToArray(),
    };

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

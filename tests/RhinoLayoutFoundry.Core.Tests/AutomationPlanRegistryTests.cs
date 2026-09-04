using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Extensibility;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class AutomationPlanRegistryTests
{
    [Fact]
    public async Task ApprovalIsSingleUseAndCannotBeForged()
    {
        var context = new Context();
        var registry = new AutomationPlanRegistry(context, context);
        var envelope = registry.StagePlan(Plan());
        var approval = registry.ApprovePlan(envelope.PlanId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.ApplyApprovedPlanAsync(
            approval with { Token = "incorrect" }, CancellationToken.None));
        Assert.True((await registry.ApplyApprovedPlanAsync(approval, CancellationToken.None)).Succeeded);
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.ApplyApprovedPlanAsync(approval, CancellationToken.None));
        Assert.Equal(1, context.Applied);
    }

    [Fact]
    public async Task ExpiredAndStaleApprovalsDoNotApply()
    {
        var context = new Context();
        var clock = new Clock();
        var registry = new AutomationPlanRegistry(context, context, clock);
        var approval = registry.ApprovePlan(registry.StagePlan(Plan()).PlanId);
        clock.Now += TimeSpan.FromMinutes(16);
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.ApplyApprovedPlanAsync(approval, CancellationToken.None));
        approval = registry.ApprovePlan(registry.StagePlan(Plan()).PlanId);
        context.Snapshot = context.Snapshot with { Revision = 2 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.ApplyApprovedPlanAsync(approval, CancellationToken.None));
        Assert.Equal(0, context.Applied);
    }

    [Fact]
    public void DisallowedAndStalePlansAreRejected()
    {
        var context = new Context();
        var registry = new AutomationPlanRegistry(context, context);
        Assert.Throws<InvalidOperationException>(() => registry.StagePlan(Plan() with { SourceRevision = 99 }));
        Assert.Throws<InvalidOperationException>(() => registry.StagePlan(Plan() with
        { Changes = [new RenameSheetChange(Guid.NewGuid(), "old", "new")] }));
        var envelope = registry.StagePlan(Plan());
        context.Snapshot = context.Snapshot with { DocumentRuntimeSerialNumber = 99 };
        Assert.Throws<InvalidOperationException>(() => registry.ApprovePlan(envelope.PlanId));
    }

    [Fact]
    public async Task ReturnedAndOriginalPlansCannotChangeTheApprovedWork()
    {
        var context = new Context();
        var registry = new AutomationPlanRegistry(context, context);
        var plan = Plan();
        var envelope = registry.StagePlan(plan);
        ((OperationChange[])plan.Changes)[0] = new RenameSheetChange(Guid.NewGuid(), "old", "new");
        ((OperationChange[])envelope.Plan.Changes)[0] = new RenameSheetChange(Guid.NewGuid(), "old", "new");
        await registry.ApplyApprovedPlanAsync(registry.ApprovePlan(envelope.PlanId), CancellationToken.None);
        Assert.IsType<SetAppearanceStateAssignmentChange>(context.LastPlan!.Changes[0]);
    }

    private static OperationPlan Plan() => new(42, 1, "Assign appearance",
        new OperationChange[] { new SetAppearanceStateAssignmentChange(
            new HierarchyScope(HierarchyScopeKind.Sheet, TestSnapshots.SheetOneId), null, null) }, []);

    private sealed class Context : IDocumentSnapshotProvider, IDocumentMutationService
    {
        internal DocumentSnapshot Snapshot = TestSnapshots.Create();
        internal int Applied;
        internal OperationPlan? LastPlan;
        public DocumentSnapshot Capture() => Snapshot;
        public Task<OperationResult> ApplyAsync(OperationPlan plan, CancellationToken cancellationToken)
        {
            Applied++;
            LastPlan = plan;
            return Task.FromResult(new OperationResult(true, []));
        }
    }

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}

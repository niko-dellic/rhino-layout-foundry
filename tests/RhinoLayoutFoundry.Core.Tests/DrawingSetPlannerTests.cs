using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Extensibility;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class DrawingSetPlannerTests
{
    private static DrawingSetSpecification Proposal() => new(1, Guid.NewGuid(), 42, 1,
        TestSnapshots.Create().RootFolderId, new DrawingSheetSpecification[]
        {
            new("sheet", "Proposed set", 420, 297, new DrawingViewSpecification[]
            {
                new("view", "Site", "site_plan", 200, new(10,10,400,280),
                    new(0,0,100), new(0,0,0), new(0,1,0), null),
            }),
        });

    [Fact]
    public void CompleteSetCompilesToOneChangeWithReadableApprovalSummary()
    {
        var plan = DrawingSetPlanner.Plan(Proposal(), TestSnapshots.Create());
        Assert.True(plan.CanApply);
        Assert.IsType<CreateDrawingSetChange>(Assert.Single(plan.Changes));
        Assert.Contains("420 × 297 mm", plan.UndoDescription);
        Assert.Contains("Site, 1:200", plan.UndoDescription);
    }

    [Fact]
    public void AppliedReceiptPreventsRecreatingProposal()
    {
        var proposal = Proposal();
        var snapshot = TestSnapshots.Create() with
        { Metadata = new Dictionary<string, string> { [DrawingSetPlanner.ReceiptKey(proposal.ProposalId)] = "receipt" } };
        var plan = DrawingSetPlanner.Plan(proposal, snapshot);
        Assert.False(plan.CanApply);
        Assert.Empty(plan.Changes);
        Assert.Contains(plan.Diagnostics, d => d.Code == "drawing_set.already_applied");
    }

    [Fact]
    public void InvalidProposalProducesNoPartialWork()
    {
        var proposal = Proposal();
        proposal = proposal with { Sheets = [proposal.Sheets[0] with { WidthMm = -1 }] };
        Assert.Empty(DrawingSetPlanner.Plan(proposal, TestSnapshots.Create()).Changes);
    }

    [Fact]
    public async Task NestedProposalIsFrozenAndApprovalIsSingleUse()
    {
        var context = new Context();
        var registry = new AutomationPlanRegistry(context, context);
        var proposal = Proposal();
        var plan = DrawingSetPlanner.Plan(proposal, context.Snapshot);
        var expected = DrawingSetPlanner.Digest(proposal);
        var envelope = registry.StagePlan(plan);
        ((DrawingSheetSpecification[])proposal.Sheets)[0] = proposal.Sheets[0] with { Name = "tampered" };
        var returned = Assert.IsType<CreateDrawingSetChange>(envelope.Plan.Changes[0]);
        ((IList<DrawingViewSpecification>)returned.Specification.Sheets[0].Views)[0] =
            returned.Specification.Sheets[0].Views[0] with { ScaleDenominator = 999 };
        Assert.Null(context.Applied);
        var approval = registry.ApprovePlan(envelope.PlanId);
        Assert.True((await registry.ApplyApprovedPlanAsync(approval, CancellationToken.None)).Succeeded);
        var applied = Assert.IsType<CreateDrawingSetChange>(context.Applied!.Changes[0]);
        Assert.Equal(expected, DrawingSetPlanner.Digest(applied.Specification));
        Assert.Equal(expected, applied.Digest);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            registry.ApplyApprovedPlanAsync(approval, CancellationToken.None));
    }

    [Fact]
    public async Task ChangedDocumentRejectsWholeBatch()
    {
        var context = new Context();
        var registry = new AutomationPlanRegistry(context, context);
        var envelope = registry.StagePlan(DrawingSetPlanner.Plan(Proposal(), context.Snapshot));
        var approval = registry.ApprovePlan(envelope.PlanId);
        context.Snapshot = context.Snapshot with { Revision = 2 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.ApplyApprovedPlanAsync(approval, CancellationToken.None));
        Assert.Null(context.Applied);
    }

    private sealed class Context : IDocumentSnapshotProvider, IDocumentMutationService
    {
        public DocumentSnapshot Snapshot = TestSnapshots.Create();
        public OperationPlan? Applied;
        public DocumentSnapshot Capture() => Snapshot;
        public Task<OperationResult> ApplyAsync(OperationPlan plan, CancellationToken cancellationToken)
        { Applied = plan; return Task.FromResult(new OperationResult(true, [])); }
    }
}

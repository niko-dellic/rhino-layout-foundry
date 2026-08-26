using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class RenameSheetPlannerTests
{
    private readonly RenameSheetPlanner _planner = new();

    [Fact]
    public void ValidRenameProducesFrozenChange()
    {
        var snapshot = TestSnapshots.Create();
        var request = Request(snapshot, "Ground Floor");

        var plan = _planner.Plan(request, snapshot);

        Assert.True(plan.CanApply);
        Assert.Empty(plan.Diagnostics);
        Assert.Single(plan.Changes);
        Assert.Equal(
            new RenameSheetChange(TestSnapshots.SheetOneId, "A-001", "Ground Floor"),
            plan.Changes[0]);
    }

    [Fact]
    public void RenameTrimsOuterWhitespace()
    {
        var snapshot = TestSnapshots.Create();

        var plan = _planner.Plan(Request(snapshot, "  Ground Floor  "), snapshot);

        Assert.True(plan.CanApply);
        Assert.Equal(
            new RenameSheetChange(TestSnapshots.SheetOneId, "A-001", "Ground Floor"),
            plan.Changes[0]);
    }

    [Fact]
    public void EmptyNameIsRejected()
    {
        var snapshot = TestSnapshots.Create();

        var plan = _planner.Plan(Request(snapshot, "   "), snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "rename.empty_name");
    }

    [Fact]
    public void DuplicateNameIsRejectedCaseInsensitively()
    {
        var snapshot = TestSnapshots.Create();

        var plan = _planner.Plan(Request(snapshot, "a-002"), snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "rename.duplicate_name");
    }

    [Fact]
    public void MissingSheetIsRejected()
    {
        var snapshot = TestSnapshots.Create();
        var request = Request(snapshot, "Ground Floor") with { PageViewId = Guid.NewGuid() };

        var plan = _planner.Plan(request, snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "rename.sheet_missing");
    }

    [Fact]
    public void StaleRevisionIsRejected()
    {
        var snapshot = TestSnapshots.Create();
        var request = Request(snapshot, "Ground Floor") with { SourceRevision = snapshot.Revision - 1 };

        var plan = _planner.Plan(request, snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "rename.stale_revision");
    }

    [Fact]
    public void ChangedBeforeValueIsRejected()
    {
        var snapshot = TestSnapshots.Create();
        var request = Request(snapshot, "Ground Floor") with { ExpectedName = "Old Name" };

        var plan = _planner.Plan(request, snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "rename.before_value_changed");
    }

    [Fact]
    public void DifferentDocumentIsRejected()
    {
        var snapshot = TestSnapshots.Create();
        var request = Request(snapshot, "Ground Floor") with
        {
            DocumentRuntimeSerialNumber = snapshot.DocumentRuntimeSerialNumber + 1,
        };

        var plan = _planner.Plan(request, snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "rename.document_mismatch");
    }

    [Fact]
    public void NoOpRenameHasNoChangeAndCannotApply()
    {
        var snapshot = TestSnapshots.Create();

        var plan = _planner.Plan(Request(snapshot, "A-001"), snapshot);

        Assert.False(plan.CanApply);
        Assert.Empty(plan.Changes);
        Assert.Contains(
            plan.Diagnostics,
            item => item.Code == "rename.no_change" &&
                    item.Severity == DiagnosticSeverity.Information);
    }

    private static RenameSheetRequest Request(
        Core.Domain.DocumentSnapshot snapshot,
        string newName)
    {
        return new RenameSheetRequest(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            TestSnapshots.SheetOneId,
            "A-001",
            newName);
    }
}

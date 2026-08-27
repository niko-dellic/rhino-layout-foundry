using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class BatchPropertiesSessionTests
{
    [Fact]
    public void StagingAndInclusionRemainLocalUntilApply()
    {
        var session = CreateSession();
        session.Stage(BatchPropertyKind.Tags, " Issue A ");
        session.SetIncluded(
            new OverviewNodeKey(OverviewNodeKind.Sheet, TestSnapshots.SheetTwoId),
            false);

        Assert.True(session.IsDirty);
        Assert.Equal("Issue A", session.StagedValues[BatchPropertyKind.Tags]);
        Assert.Single(session.Targets.Where(target => target.Included));
    }

    [Fact]
    public void ConflictAndMissingUndoCapabilityBlockApply()
    {
        var session = CreateSession();
        session.Stage(BatchPropertyKind.PaperSize, "A3");
        session.Revalidate(42, currentRevision: 2);

        var validation = session.Validate(mutationCapabilityAvailable: false);

        Assert.False(validation.CanApply);
        Assert.Contains(validation.Errors, error => error.Contains("changed", StringComparison.Ordinal));
        Assert.Contains(validation.Warnings, warning => warning.Contains("Undo", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidStagedChangeCanApplyWhenCapabilityExists()
    {
        var session = CreateSession();
        session.Stage(BatchPropertyKind.Orientation, "Landscape");

        var validation = session.Validate(mutationCapabilityAvailable: true);

        Assert.True(validation.CanApply);
        Assert.Empty(validation.Errors);
    }

    private static BatchPropertiesSession CreateSession()
    {
        return new BatchPropertiesSession(
            42,
            1,
            [
                new BatchTarget(
                    new OverviewNodeKey(OverviewNodeKind.Sheet, TestSnapshots.SheetOneId),
                    "A-001"),
                new BatchTarget(
                    new OverviewNodeKey(OverviewNodeKind.Sheet, TestSnapshots.SheetTwoId),
                    "A-002"),
            ]);
    }
}

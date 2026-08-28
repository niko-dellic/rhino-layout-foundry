using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class OverviewInvalidationTests
{
    [Fact]
    public void MergeUnionsKindsAndEntityIds()
    {
        var first = new OverviewInvalidation(
            42,
            OverviewInvalidationKind.Hierarchy,
            new HashSet<Guid> { TestSnapshots.SheetOneId });
        var second = new OverviewInvalidation(
            42,
            OverviewInvalidationKind.Thumbnails,
            new HashSet<Guid> { TestSnapshots.SheetTwoId });

        var merged = first.Merge(second);

        Assert.Equal(42u, merged.DocumentRuntimeSerialNumber);
        Assert.True((merged.Kind & OverviewInvalidationKind.Hierarchy) != 0);
        Assert.True((merged.Kind & OverviewInvalidationKind.Thumbnails) != 0);
        Assert.Equal(2, merged.AffectedEntityIds.Count);
    }

    [Fact]
    public void MergeAcrossDocumentsDropsSpecificSerial()
    {
        var merged = new OverviewInvalidation(1, OverviewInvalidationKind.Metadata)
            .Merge(new OverviewInvalidation(2, OverviewInvalidationKind.Diagnostics));

        Assert.Null(merged.DocumentRuntimeSerialNumber);
    }
}

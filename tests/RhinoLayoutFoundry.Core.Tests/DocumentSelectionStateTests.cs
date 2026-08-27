using RhinoLayoutFoundry.Core.Observer;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class DocumentSelectionStateTests
{
    [Fact]
    public void SelectionPublishesStableIdsAndSuppressesEquivalentUpdates()
    {
        var selection = new DocumentSelectionState();
        var key = new OverviewNodeKey(OverviewNodeKind.Sheet, Guid.NewGuid());
        var events = 0;
        selection.Changed += (_, eventArgs) =>
        {
            events++;
            Assert.Equal((uint)42, eventArgs.DocumentRuntimeSerialNumber);
            Assert.Contains(key, eventArgs.Selection);
        };

        selection.Replace(42, [key], key);
        selection.Replace(42, [key], key);

        Assert.Equal(1, events);
        Assert.Equal(key, selection.Anchor);
    }

    [Fact]
    public void MixedFolderSheetAndDetailSelectionIsPreservedExactly()
    {
        var selection = new DocumentSelectionState();
        var keys = new[]
        {
            new OverviewNodeKey(OverviewNodeKind.Folder, Guid.NewGuid()),
            new OverviewNodeKey(OverviewNodeKind.Sheet, Guid.NewGuid()),
            new OverviewNodeKey(OverviewNodeKind.Detail, Guid.NewGuid()),
        };

        selection.Replace(7, keys, keys[2]);

        Assert.Equal(3, selection.Selected.Count);
        Assert.All(keys, key => Assert.Contains(key, selection.Selected));
        Assert.Equal(keys[2], selection.Anchor);
    }
}

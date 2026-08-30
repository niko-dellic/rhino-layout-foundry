using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class NavigatorDropResolverTests
{
    private readonly Guid _root = Guid.Parse("81000000-0000-0000-0000-000000000001");
    private readonly Guid _folder = Guid.Parse("81000000-0000-0000-0000-000000000002");
    private readonly Guid _sheet = Guid.Parse("82000000-0000-0000-0000-000000000001");

    [Fact]
    public void FolderCenterResolvesToDestinationHighlight()
    {
        var result = Resolve(35, [OverviewNodeKind.Folder]);

        Assert.True(result.IsValid);
        Assert.Equal(_folder, result.HighlightFolderId);
        Assert.Equal(HierarchyPlacementKind.IntoFolder, result.Target!.Kind);
    }

    [Fact]
    public void SheetEdgeResolvesToInsertionLine()
    {
        var result = Resolve(51, [OverviewNodeKind.Sheet]);

        Assert.True(result.IsValid);
        Assert.Equal(HierarchyPlacementKind.BeforeSibling, result.Target!.Kind);
        Assert.Equal(50, result.InsertionLineY);
    }

    [Fact]
    public void MixedInsertionIsRejectedButEmptySpaceMeansRoot()
    {
        var mixed = Resolve(51, [OverviewNodeKind.Folder, OverviewNodeKind.Sheet]);
        var root = Resolve(95, [OverviewNodeKind.Folder, OverviewNodeKind.Sheet]);

        Assert.False(mixed.IsValid);
        Assert.True(root.IsValid);
        Assert.Equal(_root, root.HighlightFolderId);
    }

    private NavigatorDropResolution Resolve(double y, IReadOnlyCollection<OverviewNodeKind> kinds)
    {
        var rows = new[]
        {
            new NavigatorDropRow(new(OverviewNodeKind.Folder, _root), _root, 0, 20),
            new NavigatorDropRow(new(OverviewNodeKind.Folder, _folder), _root, 25, 20),
            new NavigatorDropRow(new(OverviewNodeKind.Sheet, _sheet), _root, 50, 20),
        };
        return new NavigatorDropResolver().Resolve(rows, y, 0, 100, kinds, _root);
    }
}

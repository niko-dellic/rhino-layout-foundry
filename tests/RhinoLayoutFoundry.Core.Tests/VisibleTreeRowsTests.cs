using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class VisibleTreeRowsTests
{
    [Fact]
    public void FlattenSkipsChildrenOfCollapsedRows()
    {
        var hidden = new Node("hidden", true, []);
        var collapsed = new Node("collapsed", false, [hidden]);
        var visible = new Node("visible", true, []);

        var result = VisibleTreeRows.Flatten(
                [collapsed, visible],
                node => node.Children,
                node => node.Expanded)
            .Select(node => node.Name)
            .ToArray();

        Assert.Equal(["collapsed", "visible"], result);
    }

    [Fact]
    public void FlattenIncludesExpandedDescendantsInDisplayOrder()
    {
        var leaf = new Node("leaf", false, []);
        var child = new Node("child", true, [leaf]);
        var root = new Node("root", true, [child]);

        var result = VisibleTreeRows.Flatten(
                [root],
                node => node.Children,
                node => node.Expanded)
            .Select(node => node.Name)
            .ToArray();

        Assert.Equal(["root", "child", "leaf"], result);
    }

    private sealed record Node(string Name, bool Expanded, IReadOnlyList<Node> Children);
}

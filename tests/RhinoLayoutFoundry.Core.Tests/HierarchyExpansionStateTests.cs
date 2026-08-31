using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class HierarchyExpansionStateTests
{
    [Fact]
    public void ExplicitSheetExpansionSurvivesTreeRebuild()
    {
        var sheet = new OverviewNodeKey(OverviewNodeKind.Sheet, Guid.NewGuid());
        var state = new HierarchyExpansionState();

        Assert.False(state.Resolve(sheet, expandedByDefault: false));

        state.Record(sheet, expanded: true);

        Assert.True(state.Resolve(sheet, expandedByDefault: false));
    }

    [Fact]
    public void ExplicitCollapseOverridesFolderDefaultUntilStateIsCleared()
    {
        var folder = new OverviewNodeKey(OverviewNodeKind.Folder, Guid.NewGuid());
        var state = new HierarchyExpansionState();

        state.Record(folder, expanded: false);
        Assert.False(state.Resolve(folder, expandedByDefault: true));

        state.Clear();
        Assert.True(state.Resolve(folder, expandedByDefault: true));
    }
}

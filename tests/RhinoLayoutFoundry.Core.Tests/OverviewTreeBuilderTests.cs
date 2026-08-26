using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class OverviewTreeBuilderTests
{
    [Fact]
    public void BuildsFolderSheetDetailHierarchyInConfiguredOrder()
    {
        var overview = CreateOverview();

        var roots = OverviewTreeBuilder.Build(overview);

        Assert.Single(roots);
        Assert.Equal("Unorganized", roots[0].Label);
        Assert.Equal("Plans", roots[0].Children[0].Label);
        Assert.Equal("A-001", roots[0].Children[0].Children[0].Label);
        Assert.Equal("Main Plan", roots[0].Children[0].Children[0].Children[0].Label);
        Assert.Equal("A-100", roots[0].Children[1].Label);
    }

    [Fact]
    public void FolderMatchIncludesAllDescendants()
    {
        var roots = OverviewTreeBuilder.Build(CreateOverview(), "plans");

        Assert.Single(roots);
        Assert.Single(roots[0].Children);
        Assert.Equal("Plans", roots[0].Children[0].Label);
        Assert.Single(roots[0].Children[0].Children);
        Assert.Equal(2, roots[0].Children[0].Children[0].Children.Count);
    }

    [Fact]
    public void TagMatchKeepsAncestorsAndSheetDetails()
    {
        var roots = OverviewTreeBuilder.Build(CreateOverview(), "issue-a");

        Assert.Single(roots);
        Assert.Single(roots[0].Children);
        var sheet = roots[0].Children[0].Children[0];
        Assert.Equal("A-001", sheet.Label);
        Assert.Equal(2, sheet.Children.Count);
    }

    [Fact]
    public void DetailMatchKeepsOnlyMatchingDetailAndAncestors()
    {
        var roots = OverviewTreeBuilder.Build(CreateOverview(), "ceiling");

        var sheet = roots[0].Children[0].Children[0];
        Assert.Single(sheet.Children);
        Assert.Equal("Ceiling Plan", sheet.Children[0].Label);
    }

    [Fact]
    public void SheetWithMissingFolderFallsBackToRoot()
    {
        var overview = CreateOverview();
        var missingFolderSheet = overview.Sheets[1] with { FolderId = Guid.NewGuid() };
        overview = overview with { Sheets = [overview.Sheets[0], missingFolderSheet] };

        var root = OverviewTreeBuilder.Build(overview)[0];

        Assert.Contains(root.Children, child => child.Label == "A-100");
    }

    private static DocumentOverview CreateOverview()
    {
        var rootId = Guid.Parse("60000000-0000-0000-0000-000000000001");
        var plansId = Guid.Parse("60000000-0000-0000-0000-000000000002");
        var firstSheet = new SheetOverview(
            TestSnapshots.SheetOneId,
            plansId,
            "A-001",
            0,
            ["Issue-A", "Plans"],
            [
                new DetailOverview(TestSnapshots.DetailOneId, "Main Plan", 0),
                new DetailOverview(Guid.Parse("30000000-0000-0000-0000-000000000003"), "Ceiling Plan", 1),
            ]);
        var secondSheet = new SheetOverview(
            TestSnapshots.SheetTwoId,
            rootId,
            "A-100",
            1,
            [],
            []);

        return new DocumentOverview(
            42,
            "Test",
            rootId,
            [
                new FolderOverview(rootId, null, "Unorganized", 0),
                new FolderOverview(plansId, rootId, "Plans", 0),
            ],
            [firstSheet, secondSheet]);
    }
}

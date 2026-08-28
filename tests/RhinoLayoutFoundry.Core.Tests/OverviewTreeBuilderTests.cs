using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class OverviewTreeBuilderTests
{
    [Fact]
    public void WrapsTopLevelFoldersAndSheetsInDocumentRoot()
    {
        var roots = OverviewTreeBuilder.Build(CreateOverview());

        var root = Assert.Single(roots);
        Assert.True(root.IsDocumentRoot);
        Assert.Equal("Test.3dm", root.Label);
        Assert.Equal("Plans", root.Children[0].Label);
        Assert.Equal("A-001", root.Children[0].Children[0].Label);
        Assert.Equal("Main Plan", root.Children[0].Children[0].Children[0].Label);
        Assert.Equal("A-100", root.Children[1].Label);
        Assert.DoesNotContain(Flatten(roots), node => node.Label == "Unorganized");
    }

    [Fact]
    public void FolderMatchIncludesAllDescendants()
    {
        var roots = OverviewTreeBuilder.Build(CreateOverview(), "plans");

        Assert.Single(roots);
        Assert.Equal("Test.3dm", roots[0].Label);
        Assert.Single(roots[0].Children);
        Assert.Equal("Plans", roots[0].Children[0].Label);
        Assert.Equal(2, roots[0].Children[0].Children[0].Children.Count);
    }

    [Fact]
    public void HiddenRootNameNeverMatchesSearch()
    {
        var roots = OverviewTreeBuilder.Build(CreateOverview(), "unorganized");

        Assert.Empty(roots);
    }

    [Fact]
    public void TextSearchDoesNotMatchLegacyTags()
    {
        var roots = OverviewTreeBuilder.Build(CreateOverview(), "issue-a");

        Assert.Empty(roots);
    }

    [Fact]
    public void DetailMatchKeepsOnlyMatchingDetailAndAncestors()
    {
        var roots = OverviewTreeBuilder.Build(CreateOverview(), "ceiling");

        var sheet = Flatten(roots).Single(node => node.Key.Kind == OverviewNodeKind.Sheet);
        Assert.Single(sheet.Children);
        Assert.Equal("Ceiling Plan", sheet.Children[0].Label);
    }

    [Fact]
    public void SheetsFilterHidesDetailRows()
    {
        var roots = OverviewTreeBuilder.Build(
            CreateOverview(),
            new OverviewTreeFilter(null, OverviewFilterKind.Sheets));

        Assert.Equal(2, Flatten(roots).Count(node => node.Key.Kind == OverviewNodeKind.Sheet));
        Assert.DoesNotContain(Flatten(roots), node => node.Key.Kind == OverviewNodeKind.Detail);
    }

    [Fact]
    public void DetailsFilterExcludesSheetsWithoutDetails()
    {
        var roots = OverviewTreeBuilder.Build(
            CreateOverview(),
            new OverviewTreeFilter(null, OverviewFilterKind.Details));

        Assert.Single(Flatten(roots), node => node.Key.Kind == OverviewNodeKind.Sheet);
        Assert.Equal(2, Flatten(roots).Count(node => node.Key.Kind == OverviewNodeKind.Detail));
    }

    [Fact]
    public void TagFiltersSeparateTaggedAndUntaggedSheets()
    {
        var tagged = OverviewTreeBuilder.Build(
            CreateOverview(),
            new OverviewTreeFilter(null, OverviewFilterKind.Tagged));
        var untagged = OverviewTreeBuilder.Build(
            CreateOverview(),
            new OverviewTreeFilter(null, OverviewFilterKind.Untagged));

        Assert.Equal("A-001", Flatten(tagged).Single(node => node.Key.Kind == OverviewNodeKind.Sheet).Label);
        Assert.Equal("A-100", Flatten(untagged).Single(node => node.Key.Kind == OverviewNodeKind.Sheet).Label);
    }

    [Fact]
    public void SheetAndDetailRowsCarryStableNavigationTargets()
    {
        var nodes = Flatten(OverviewTreeBuilder.Build(CreateOverview())).ToArray();
        var sheet = nodes.Single(node => node.Key.Id == TestSnapshots.SheetOneId);
        var detail = nodes.Single(node => node.Key.Id == TestSnapshots.DetailOneId);

        Assert.Equal(new OverviewNavigationTarget(TestSnapshots.SheetOneId), sheet.NavigationTarget);
        Assert.Equal(
            new OverviewNavigationTarget(TestSnapshots.SheetOneId, TestSnapshots.DetailOneId),
            detail.NavigationTarget);
    }

    [Fact]
    public void SheetWithMissingFolderFallsBackToTopLevel()
    {
        var overview = CreateOverview();
        var missingFolderSheet = overview.Sheets[1] with { FolderId = Guid.NewGuid() };
        overview = overview with { Sheets = [overview.Sheets[0], missingFolderSheet] };

        var roots = OverviewTreeBuilder.Build(overview);

        Assert.Contains(roots[0].Children, child => child.Label == "A-100");
    }

    [Fact]
    public void DocumentRootDoesNotDuplicateExistingThreeDmExtension()
    {
        var overview = CreateOverview() with { DocumentName = "Test.3DM" };

        var root = Assert.Single(OverviewTreeBuilder.Build(overview));

        Assert.Equal("Test.3DM", root.Label);
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

    private static IEnumerable<OverviewTreeNode> Flatten(IEnumerable<OverviewTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }
}

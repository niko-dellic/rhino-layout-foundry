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
    public void FolderAndSheetNotesParticipateInSearch()
    {
        var overview = CreateOverview() with
        {
            Folders = CreateOverview().Folders.Select(folder => folder.Name == "Plans"
                ? folder with { Notes = "Permit coordination" }
                : folder).ToArray(),
            Sheets = CreateOverview().Sheets.Select(sheet => sheet.Name == "A-100"
                ? sheet with { Notes = "Demolition package" }
                : sheet).ToArray(),
        };

        var folderMatch = Flatten(OverviewTreeBuilder.Build(overview, "permit")).ToArray();
        var sheetMatch = Flatten(OverviewTreeBuilder.Build(overview, "demolition")).ToArray();

        Assert.Contains(folderMatch, node => node.Label == "Plans");
        Assert.Contains(folderMatch, node => node.Label == "A-001");
        Assert.Contains(sheetMatch, node => node.Label == "A-100");
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
            PageViewId: TestSnapshots.SheetOneId,
            FolderId: plansId,
            Name: "A-001",
            Order: 0,
            Details: [
                new DetailOverview(DetailViewportId: TestSnapshots.DetailOneId, Name: "Main Plan", Order: 0),
                new DetailOverview(DetailViewportId: Guid.Parse("30000000-0000-0000-0000-000000000003"), Name: "Ceiling Plan", Order: 1),
            ]);
        var secondSheet = new SheetOverview(
            PageViewId: TestSnapshots.SheetTwoId,
            FolderId: rootId,
            Name: "A-100",
            Order: 1,
            Details: []);

        return new DocumentOverview(
            DocumentRuntimeSerialNumber: 42,
            DocumentName: "Test",
            RootFolderId: rootId,
            Folders: [
                new FolderOverview(Id: rootId, ParentId: null, Name: "Unorganized", Order: 0),
                new FolderOverview(Id: plansId, ParentId: rootId, Name: "Plans", Order: 0),
            ],
                    Sheets: [firstSheet, secondSheet]);
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

using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class OverviewFilterProjectionTests
{
    private static readonly Guid RootId = Guid.Parse("61000000-0000-0000-0000-000000000001");
    private static readonly Guid PlansId = Guid.Parse("61000000-0000-0000-0000-000000000002");
    private static readonly Guid SheetOneId = Guid.Parse("61000000-0000-0000-0000-000000000003");
    private static readonly Guid SheetTwoId = Guid.Parse("61000000-0000-0000-0000-000000000004");
    private static readonly Guid DetailId = Guid.Parse("61000000-0000-0000-0000-000000000005");

    [Fact]
    public void InactiveFilterMatchesEverySheet()
    {
        var projection = OverviewFilterProjector.Resolve(CreateOverview(), new OverviewTreeFilter(null));

        Assert.False(projection.IsActive);
        Assert.True(projection.MatchesSheet(SheetOneId));
        Assert.True(projection.MatchesSheet(SheetTwoId));
    }

    [Fact]
    public void FolderMatchEmphasizesFolderAndEveryDescendant()
    {
        var projection = OverviewFilterProjector.Resolve(CreateOverview(), new OverviewTreeFilter("plans"));

        Assert.True(projection.Emphasizes(new OverviewNodeKey(OverviewNodeKind.Folder, PlansId)));
        Assert.True(projection.Emphasizes(new OverviewNodeKey(OverviewNodeKind.Sheet, SheetOneId)));
        Assert.True(projection.Emphasizes(new OverviewNodeKey(OverviewNodeKind.Detail, DetailId)));
        Assert.False(projection.MatchesSheet(SheetTwoId));
    }

    [Fact]
    public void DetailMatchEmphasizesContainingSheetAndAncestorsCaseInsensitively()
    {
        var projection = OverviewFilterProjector.Resolve(CreateOverview(), new OverviewTreeFilter("CEILING"));

        Assert.True(projection.Emphasizes(new OverviewNodeKey(OverviewNodeKind.Folder, RootId)));
        Assert.True(projection.Emphasizes(new OverviewNodeKey(OverviewNodeKind.Folder, PlansId)));
        Assert.True(projection.Emphasizes(new OverviewNodeKey(OverviewNodeKind.Sheet, SheetOneId)));
        Assert.True(projection.Emphasizes(new OverviewNodeKey(OverviewNodeKind.Detail, DetailId)));
        Assert.False(projection.MatchesSheet(SheetTwoId));
    }

    [Fact]
    public void DetailsKindExcludesSheetsWithoutDetails()
    {
        var projection = OverviewFilterProjector.Resolve(
            CreateOverview(),
            new OverviewTreeFilter(null, OverviewFilterKind.Details));

        Assert.True(projection.MatchesSheet(SheetOneId));
        Assert.False(projection.MatchesSheet(SheetTwoId));
    }

    [Fact]
    public void NoMatchProducesEmptyEmphasisAndSheetSets()
    {
        var projection = OverviewFilterProjector.Resolve(CreateOverview(), new OverviewTreeFilter("missing"));

        Assert.True(projection.IsActive);
        Assert.Empty(projection.EmphasizedKeys);
        Assert.Empty(projection.MatchingSheetIds);
    }

    private static DocumentOverview CreateOverview() => new(
        42,
        "Test",
        RootId,
        [
            new FolderOverview(Id: RootId, ParentId: null, Name: "Root", Order: 0),
            new FolderOverview(Id: PlansId, ParentId: RootId, Name: "Plans", Order: 0),
        ],
        [
            new SheetOverview(
                PageViewId:                 SheetOneId,
                FolderId:                 PlansId,
                Name:                 "A-001",
                Order:                 0,
                Details:                 [new DetailOverview(DetailViewportId: DetailId, Name: "Ceiling Plan", Order: 0)]),
            new SheetOverview(PageViewId: SheetTwoId, FolderId: RootId, Name: "A-100", Order: 1, Details: []),
        ]);
}

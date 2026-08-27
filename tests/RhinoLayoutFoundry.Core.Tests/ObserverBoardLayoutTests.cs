using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Observer;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class ObserverBoardLayoutTests
{
    [Fact]
    public void MixedPaperUnitsPreservePhysicalProportions()
    {
        Assert.True(Math.Abs(215.9 - PaperUnitConverter.ToMillimeters(8.5, "Inches")) < 1e-8);
        Assert.True(Math.Abs(297 - PaperUnitConverter.ToMillimeters(29.7, "Centimeters")) < 1e-8);
    }

    [Fact]
    public void AutomaticLayoutIsDeterministicAndNonOverlapping()
    {
        var snapshot = Snapshot();
        var planner = new ObserverPlacementPlanner();

        var first = planner.Arrange(snapshot);
        var second = planner.Arrange(snapshot);

        Assert.Equal(first.Bounds, second.Bounds);
        Assert.Equal(first.Sheets.Keys.Order(), second.Sheets.Keys.Order());
        var cards = first.Sheets.Values.ToArray();
        for (var left = 0; left < cards.Length; left++)
        for (var right = left + 1; right < cards.Length; right++)
            Assert.False(cards[left].Bounds.Intersects(cards[right].Bounds));
        Assert.True(cards[1].Bounds.Width > cards[0].Bounds.Width);
    }

    [Fact]
    public void ManualMoveChangesOnlySpatialPlacement()
    {
        var snapshot = Snapshot();
        var planner = new ObserverPlacementPlanner();
        var layout = planner.Arrange(snapshot);
        var sheet = snapshot.Sheets[0];
        var beforeOrder = sheet.Order;

        var state = planner.MoveSheets(snapshot, layout, [sheet.PageViewId], new ObserverPoint(75, -20));
        var moved = planner.Arrange(snapshot with { CanvasState = state });

        Assert.Equal(beforeOrder, moved.Sheets[sheet.PageViewId].Sheet.Order);
        Assert.True(Math.Abs(layout.Sheets[sheet.PageViewId].Bounds.X + 75 - moved.Sheets[sheet.PageViewId].Bounds.X) < 1e-8);
        Assert.True(Math.Abs(layout.Sheets[sheet.PageViewId].Bounds.Y - 20 - moved.Sheets[sheet.PageViewId].Bounds.Y) < 1e-8);
    }

    [Fact]
    public void SpatialIndexReturnsOnlyIntersectingCards()
    {
        var layout = new ObserverPlacementPlanner().Arrange(Snapshot());
        var first = layout.Sheets.Values.First();
        var index = new ObserverSpatialIndex(layout);

        var visible = index.QuerySheets(first.Bounds.Inflate(1));

        Assert.Contains(visible, card => card.Sheet.PageViewId == first.Sheet.PageViewId);
        Assert.False(visible.Any(card =>
            card.Sheet.PageViewId != first.Sheet.PageViewId && !card.Bounds.Intersects(first.Bounds.Inflate(1))));
    }

    [Fact]
    public void MovingParentFolderMovesItsEntireNestedGroup()
    {
        var snapshot = NestedSnapshot();
        var planner = new ObserverPlacementPlanner();
        var before = planner.Arrange(snapshot);
        var rootId = snapshot.RootFolderId;

        var state = planner.MoveFolder(snapshot, rootId, new ObserverPoint(80, 35));
        var after = planner.Arrange(snapshot with { CanvasState = state });

        foreach (var sheet in snapshot.Sheets)
        {
            Assert.Equal(before.Sheets[sheet.PageViewId].Bounds.X + 80, after.Sheets[sheet.PageViewId].Bounds.X);
            Assert.Equal(before.Sheets[sheet.PageViewId].Bounds.Y + 35, after.Sheets[sheet.PageViewId].Bounds.Y);
        }
    }

    [Fact]
    public void TidyingFolderClearsDescendantManualPlacements()
    {
        var snapshot = NestedSnapshot();
        var nestedFolder = snapshot.Folders.Single(folder => folder.ParentId != snapshot.RootFolderId && folder.ParentId is not null);
        var nestedSheet = snapshot.Sheets.Single(sheet => sheet.FolderId == nestedFolder.Id);
        var state = snapshot.CanvasState with
        {
            FolderOrigins = new Dictionary<Guid, ObserverPointRecord>
            {
                [nestedFolder.Id] = new(10, 20),
            },
            SheetPlacements = new Dictionary<Guid, ObserverPointRecord>
            {
                [nestedSheet.PageViewId] = new(30, 40),
            },
        };
        snapshot = snapshot with { CanvasState = state };
        var parent = snapshot.Folders.Single(folder => folder.Id == nestedFolder.ParentId);

        var tidy = new ObserverPlacementPlanner().Tidy(snapshot, folderIds: new HashSet<Guid> { parent.Id });

        Assert.False(tidy.FolderOrigins.ContainsKey(nestedFolder.Id));
        Assert.False(tidy.SheetPlacements.ContainsKey(nestedSheet.PageViewId));
    }

    [Fact]
    public void MissingPersistedIdsAreIgnoredWithoutAffectingKnownCards()
    {
        var snapshot = Snapshot();
        var missingFolder = Guid.NewGuid();
        var missingSheet = Guid.NewGuid();
        snapshot = snapshot with
        {
            CanvasState = new ObserverCanvasState(
                1,
                new Dictionary<Guid, ObserverPointRecord> { [missingFolder] = new(500, 500) },
                new Dictionary<Guid, ObserverPointRecord> { [missingSheet] = new(600, 600) }),
        };

        var layout = new ObserverPlacementPlanner().Arrange(snapshot);

        Assert.Equal(snapshot.Sheets.Count, layout.Sheets.Count);
        Assert.False(layout.Sheets.ContainsKey(missingSheet));
        Assert.False(layout.Folders.ContainsKey(missingFolder));
    }

    private static ObserverSnapshot Snapshot()
    {
        var root = Guid.NewGuid();
        var folder = Guid.NewGuid();
        return new ObserverSnapshot(
            42,
            8,
            "Museum",
            root,
            [
                new ObserverFolderSnapshot(root, null, "Museum", 0),
                new ObserverFolderSnapshot(folder, root, "Plans", 0),
            ],
            [
                Sheet(folder, "A3", 0, 420, 297),
                Sheet(folder, "A2", 1, 594, 420),
                Sheet(root, "Cover", 0, 210, 297),
            ],
            ObserverCanvasState.Empty);
    }

    private static ObserverSnapshot NestedSnapshot()
    {
        var root = Guid.NewGuid();
        var parent = Guid.NewGuid();
        var nested = Guid.NewGuid();
        return new ObserverSnapshot(
            43,
            9,
            "Nested",
            root,
            [
                new ObserverFolderSnapshot(root, null, "Nested", 0),
                new ObserverFolderSnapshot(parent, root, "Plans", 0),
                new ObserverFolderSnapshot(nested, parent, "Details", 0),
            ],
            [
                Sheet(root, "Cover", 0, 210, 297),
                Sheet(parent, "Plans", 0, 420, 297),
                Sheet(nested, "Detail", 0, 297, 210),
            ],
            ObserverCanvasState.Empty);
    }

    private static ObserverSheetSnapshot Sheet(
        Guid folder,
        string name,
        int order,
        double width,
        double height) => new(
        Guid.NewGuid(), folder, name, order, width, height, "Millimeters", [], true, 1);
}

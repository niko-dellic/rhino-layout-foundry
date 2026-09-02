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
    public void DetailPageBoundsConvertBottomOriginRhinoCoordinatesToTopOriginCanvasCoordinates()
    {
        var bounds = ObserverDetailBounds.FromPageCoordinates(10, 20, 30, 40, 100, 100);

        Assert.True(Math.Abs(0.1 - bounds.X) < 1e-8);
        Assert.True(Math.Abs(0.6 - bounds.Y) < 1e-8);
        Assert.True(Math.Abs(0.2 - bounds.Width) < 1e-8);
        Assert.True(Math.Abs(0.2 - bounds.Height) < 1e-8);
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
        Assert.DoesNotContain(visible, card =>
            card.Sheet.PageViewId != first.Sheet.PageViewId && !card.Bounds.Intersects(first.Bounds.Inflate(1)));
    }

    [Fact]
    public void SpatialIndexKeepsCardVisibleWhenViewportIsEntirelyInsideIt()
    {
        var layout = new ObserverPlacementPlanner().Arrange(Snapshot());
        var first = layout.Sheets.Values.First();
        var index = new ObserverSpatialIndex(layout);
        var closeZoomViewport = new ObserverRect(
            first.Bounds.Center.X - 1,
            first.Bounds.Center.Y - 1,
            2,
            2);

        var visible = index.QuerySheets(closeZoomViewport);

        Assert.Contains(visible, card => card.Sheet.PageViewId == first.Sheet.PageViewId);
    }

    [Fact]
    public void AppearanceStatesArePlacedAsSelectableCardsInsideTheirFolder()
    {
        var snapshot = Snapshot();
        var folderId = snapshot.Folders.Single(folder => folder.Name == "Plans").Id;
        var state = new AppearanceStateRecord(
            Guid.NewGuid(), folderId, 0, "Presentation", [], []);
        snapshot = snapshot with { AppearanceStateResources = [state] };

        var layout = new ObserverPlacementPlanner().Arrange(snapshot);
        var card = Assert.Single(layout.AppearanceStates).Value;
        var index = new ObserverSpatialIndex(layout);

        Assert.Equal(state.Id, card.State.Id);
        Assert.True(layout.Folders[folderId].Bounds.Contains(card.Bounds));
        Assert.DoesNotContain(layout.Sheets.Values, sheet => sheet.Bounds.Intersects(card.Bounds));
        Assert.Equal(state.Id, index.HitAppearanceState(card.Bounds.Center)?.State.Id);
        Assert.Contains(index.QueryAppearanceStates(card.Bounds.Inflate(1)), item => item.State.Id == state.Id);
    }

    [Fact]
    public void AppearanceStateCardsCanBeMovedAndPersistManualPlacement()
    {
        var snapshot = Snapshot();
        var folderId = snapshot.Folders.Single(folder => folder.Name == "Plans").Id;
        var firstState = new AppearanceStateRecord(Guid.NewGuid(), folderId, 0, "Presentation", [], []);
        var secondState = new AppearanceStateRecord(Guid.NewGuid(), folderId, 1, "Diagram", [], []);
        snapshot = snapshot with { AppearanceStateResources = [firstState, secondState] };
        var planner = new ObserverPlacementPlanner();
        var before = planner.Arrange(snapshot);

        var canvasState = planner.MoveAppearanceStates(
            snapshot,
            before,
            [firstState.Id, secondState.Id],
            new ObserverPoint(75, -20));
        var after = planner.Arrange(snapshot with { CanvasState = canvasState });

        Assert.Equal(2, canvasState.StatePlacements.Count);
        foreach (var stateId in new[] { firstState.Id, secondState.Id })
        {
            Assert.True(after.AppearanceStates[stateId].HasManualPlacement);
            Assert.Equal(before.AppearanceStates[stateId].Bounds.X + 75,
                after.AppearanceStates[stateId].Bounds.X);
            Assert.Equal(before.AppearanceStates[stateId].Bounds.Y - 20,
                after.AppearanceStates[stateId].Bounds.Y);
        }
    }

    [Fact]
    public void MovingParentFolderMovesAppearanceStateCards()
    {
        var snapshot = NestedSnapshot();
        var folderId = snapshot.Folders.Single(folder => folder.Name == "Details").Id;
        var appearanceState = new AppearanceStateRecord(
            Guid.NewGuid(), folderId, 0, "Presentation", [], []);
        snapshot = snapshot with { AppearanceStateResources = [appearanceState] };
        var planner = new ObserverPlacementPlanner();
        var before = planner.Arrange(snapshot);

        var state = planner.MoveFolder(snapshot, snapshot.RootFolderId, new ObserverPoint(80, 35));
        var after = planner.Arrange(snapshot with { CanvasState = state });

        Assert.Equal(before.AppearanceStates[appearanceState.Id].Bounds.X + 80,
            after.AppearanceStates[appearanceState.Id].Bounds.X);
        Assert.Equal(before.AppearanceStates[appearanceState.Id].Bounds.Y + 35,
            after.AppearanceStates[appearanceState.Id].Bounds.Y);
    }

    [Fact]
    public void AssignmentBadgeModeDoesNotCreateOrReserveAppearanceStateCards()
    {
        var snapshot = Snapshot();
        var folderId = snapshot.Folders.Single(folder => folder.Name == "Plans").Id;
        var appearanceState = new AppearanceStateRecord(
            Guid.NewGuid(), folderId, 0, "Presentation", [], []);
        snapshot = snapshot with { AppearanceStateResources = [appearanceState] };
        var planner = new ObserverPlacementPlanner();

        var cards = planner.Arrange(
            snapshot,
            ObserverPackingMode.NestedFolders,
            ObserverAppearancePresentationMode.Cards);
        var badges = planner.Arrange(
            snapshot,
            ObserverPackingMode.NestedFolders,
            ObserverAppearancePresentationMode.AssignmentBadges);

        Assert.Single(cards.AppearanceStates);
        Assert.Empty(badges.AppearanceStates);
        Assert.True(badges.Folders[folderId].Bounds.Width < cards.Folders[folderId].Bounds.Width);
    }

    [Fact]
    public void SpatialIndexHitsAndQueriesIndividualDetailsInsideASheet()
    {
        var snapshot = Snapshot();
        var detailId = Guid.NewGuid();
        var firstSheet = snapshot.Sheets[0] with
        {
            Details =
            [
                new ObserverDetailSnapshot(
                    detailId,
                    "Plan detail",
                    new ObserverRect(0.1, 0.2, 0.35, 0.4),
                    Guid.NewGuid(),
                    "Wireframe"),
            ],
        };
        snapshot = snapshot with
        {
            Sheets = [firstSheet, .. snapshot.Sheets.Skip(1)],
        };
        var layout = new ObserverPlacementPlanner().Arrange(snapshot);
        var card = layout.Sheets[firstSheet.PageViewId];
        var expected = ObserverSpatialIndex.DetailBounds(card.Bounds, firstSheet.Details[0].NormalizedBounds);
        var index = new ObserverSpatialIndex(layout);

        var hit = index.HitDetail(expected.Center);
        var queried = index.QueryDetails(expected.Inflate(1));

        Assert.NotNull(hit);
        Assert.Equal(detailId, hit!.Detail.DetailViewportId);
        Assert.Equal(firstSheet.PageViewId, hit.SheetPageViewId);
        Assert.Equal(expected, hit.Bounds);
        Assert.Contains(queried, target => target.Detail.DetailViewportId == detailId);
        Assert.Null(index.HitDetail(new ObserverPoint(card.Bounds.Right - 1, card.Bounds.Bottom - 1)));
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
    public void NestedPackingContainsChildFoldersInsideTheirParents()
    {
        var snapshot = NestedSnapshot();
        var layout = new ObserverPlacementPlanner().Arrange(
            snapshot,
            ObserverPackingMode.NestedFolders);
        var root = layout.Folders[snapshot.RootFolderId];
        var parent = snapshot.Folders.Single(folder => folder.ParentId == snapshot.RootFolderId);
        var nested = snapshot.Folders.Single(folder => folder.ParentId == parent.Id);

        Assert.True(root.Bounds.Contains(layout.Folders[parent.Id].Bounds));
        Assert.True(layout.Folders[parent.Id].Bounds.Contains(layout.Folders[nested.Id].Bounds));
        Assert.True(root.Bounds.Contains(layout.Sheets.Values.Single(card =>
            card.Sheet.FolderId == nested.Id).Bounds));
    }

    [Fact]
    public void CompactPackingRemovesFolderFramesAndIgnoresManualBoardPlacements()
    {
        var snapshot = NestedSnapshot();
        var movedSheet = snapshot.Sheets[0];
        snapshot = snapshot with
        {
            CanvasState = snapshot.CanvasState with
            {
                SheetPlacements = new Dictionary<Guid, ObserverPointRecord>
                {
                    [movedSheet.PageViewId] = new(9000, 9000),
                },
            },
        };
        var layout = new ObserverPlacementPlanner().Arrange(
            snapshot,
            ObserverPackingMode.CompactSheets);

        Assert.Empty(layout.Folders);
        Assert.Equal(snapshot.Sheets.Count, layout.Sheets.Count);
        Assert.False(layout.Sheets[movedSheet.PageViewId].HasManualPlacement);
        Assert.True(layout.Bounds.Right < 9000);
        var cards = layout.Sheets.Values.ToArray();
        for (var left = 0; left < cards.Length; left++)
        for (var right = left + 1; right < cards.Length; right++)
            Assert.False(cards[left].Bounds.Intersects(cards[right].Bounds));
    }

    [Fact]
    public void TidyingFolderClearsDescendantManualPlacements()
    {
        var snapshot = NestedSnapshot();
        var nestedFolder = snapshot.Folders.Single(folder => folder.ParentId != snapshot.RootFolderId && folder.ParentId is not null);
        var nestedSheet = snapshot.Sheets.Single(sheet => sheet.FolderId == nestedFolder.Id);
        var appearanceState = new AppearanceStateRecord(
            Guid.NewGuid(), nestedFolder.Id, 0, "Presentation", [], []);
        snapshot = snapshot with { AppearanceStateResources = [appearanceState] };
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
            AppearanceStatePlacements = new Dictionary<Guid, ObserverPointRecord>
            {
                [appearanceState.Id] = new(50, 60),
            },
        };
        snapshot = snapshot with { CanvasState = state };
        var parent = snapshot.Folders.Single(folder => folder.Id == nestedFolder.ParentId);

        var tidy = new ObserverPlacementPlanner().Tidy(snapshot, folderIds: new HashSet<Guid> { parent.Id });

        Assert.False(tidy.FolderOrigins.ContainsKey(nestedFolder.Id));
        Assert.False(tidy.SheetPlacements.ContainsKey(nestedSheet.PageViewId));
        Assert.False(tidy.StatePlacements.ContainsKey(appearanceState.Id));
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

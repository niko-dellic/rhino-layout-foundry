using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Observer;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class ObserverCanvasLodTests
{
    [Theory]
    [InlineData(1, 40)]
    [InlineData(0.5, 80)]
    [InlineData(0.25, 160)]
    [InlineData(0.125, 320)]
    public void GridSpacingCoarsensByStablePowersOfTwo(double zoom, double expectedWorldSpacing)
    {
        Assert.Equal(expectedWorldSpacing, ObserverCanvasGridPolicy.EffectiveWorldSpacing(zoom));
    }

    [Theory]
    [InlineData(0.18)]
    [InlineData(0.25)]
    [InlineData(0.4)]
    [InlineData(0.7)]
    public void FarZoomGridMaintainsBoundedScreenDensity(double zoom)
    {
        var projectedSpacing = ObserverCanvasGridPolicy.EffectiveWorldSpacing(zoom) * zoom;

        Assert.InRange(
            projectedSpacing,
            ObserverCanvasGridPolicy.MinimumProjectedSpacingPixels,
            ObserverCanvasGridPolicy.MinimumProjectedSpacingPixels * 2);
    }

    [Fact]
    public void TierSelectionUsesBothHysteresisBands()
    {
        Assert.Equal(ObserverCanvasLodTier.Detail,
            ObserverCanvasLodPolicy.SelectTier(72, ObserverCanvasLodTier.Detail));
        Assert.Equal(ObserverCanvasLodTier.Sheet,
            ObserverCanvasLodPolicy.SelectTier(71.999, ObserverCanvasLodTier.Detail));
        Assert.Equal(ObserverCanvasLodTier.Sheet,
            ObserverCanvasLodPolicy.SelectTier(87, ObserverCanvasLodTier.Sheet));
        Assert.Equal(ObserverCanvasLodTier.Detail,
            ObserverCanvasLodPolicy.SelectTier(89, ObserverCanvasLodTier.Sheet));
        Assert.Equal(ObserverCanvasLodTier.Sheet,
            ObserverCanvasLodPolicy.SelectTier(28, ObserverCanvasLodTier.Sheet));
        Assert.Equal(ObserverCanvasLodTier.Folder,
            ObserverCanvasLodPolicy.SelectTier(27, ObserverCanvasLodTier.Sheet));
        Assert.Equal(ObserverCanvasLodTier.Folder,
            ObserverCanvasLodPolicy.SelectTier(35, ObserverCanvasLodTier.Folder));
        Assert.Equal(ObserverCanvasLodTier.Sheet,
            ObserverCanvasLodPolicy.SelectTier(37, ObserverCanvasLodTier.Folder));
    }

    [Fact]
    public void MixedPaperSizesChooseLodFromTheirProjectedShortEdges()
    {
        var snapshot = Snapshot();
        var layout = new ObserverBoardLayout(
            new Dictionary<Guid, ObserverSheetCard>
            {
                [snapshot.Sheets[0].PageViewId] = new(
                    snapshot.Sheets[0], new ObserverRect(0, 0, 100, 100), false),
                [snapshot.Sheets[1].PageViewId] = new(
                    snapshot.Sheets[1], new ObserverRect(200, 0, 40, 40), false),
            },
            new Dictionary<Guid, ObserverFolderFrame>(),
            new ObserverRect(0, 0, 240, 100));

        var result = new ObserverCanvasLodPolicy().Evaluate(
            snapshot,
            layout,
            new ObserverCamera(new ObserverPoint(120, 50), 1),
            new ObserverSize(800, 600),
            ObserverPackingMode.CompactSheets);

        Assert.Equal(ObserverCanvasLodTier.Detail, result.TierForSheet(snapshot.Sheets[0].PageViewId));
        Assert.Equal(ObserverCanvasLodTier.Sheet, result.TierForSheet(snapshot.Sheets[1].PageViewId));
        Assert.Contains(snapshot.Sheets[0].PageViewId, result.PreviewEligibleSheetIds);
        Assert.DoesNotContain(snapshot.Sheets[1].PageViewId, result.PreviewEligibleSheetIds);
    }

    [Fact]
    public void CollidingFolderSummariesCollapseToTheirCommonParent()
    {
        var snapshot = Snapshot();
        var layout = new ObserverPlacementPlanner().Arrange(snapshot, ObserverPackingMode.CompactSheets);

        var result = new ObserverCanvasLodPolicy().Evaluate(
            snapshot,
            layout,
            new ObserverCamera(layout.Bounds.Center, 0.05),
            new ObserverSize(800, 600),
            ObserverPackingMode.CompactSheets);

        var summary = Assert.Single(result.FolderSummaries);
        Assert.Equal(snapshot.RootFolderId, summary.FolderId);
        Assert.Equal(2, summary.LayoutCount);
        Assert.All(result.SheetTiers.Values,
            tier => Assert.Equal(ObserverCanvasLodTier.Folder, tier));
    }

    [Fact]
    public void FolderSummaryBoundsDoNotOverlap()
    {
        var snapshot = Snapshot();
        var first = snapshot.Sheets[0];
        var second = snapshot.Sheets[1];
        var layout = new ObserverBoardLayout(
            new Dictionary<Guid, ObserverSheetCard>
            {
                [first.PageViewId] = new(first, new ObserverRect(0, 0, 20, 20), false),
                [second.PageViewId] = new(second, new ObserverRect(5000, 0, 20, 20), false),
            },
            new Dictionary<Guid, ObserverFolderFrame>(),
            new ObserverRect(0, 0, 5020, 20));

        var result = new ObserverCanvasLodPolicy().Evaluate(
            snapshot,
            layout,
            new ObserverCamera(new ObserverPoint(2510, 10), 1),
            new ObserverSize(800, 600),
            ObserverPackingMode.CompactSheets);

        Assert.Equal(2, result.FolderSummaries.Count);
        Assert.False(result.FolderSummaries[0].ScreenBounds
            .Intersects(result.FolderSummaries[1].ScreenBounds));
    }

    [Fact]
    public void CompactPackingProvidesDeterministicAnchorForEmptyLeafFolder()
    {
        var snapshot = Snapshot();
        var emptyFolderId = Guid.NewGuid();
        snapshot = snapshot with
        {
            Folders = snapshot.Folders.Concat([
                new ObserverFolderSnapshot(emptyFolderId, snapshot.RootFolderId, "Specifications", 2),
            ]).ToArray(),
        };
        var layout = new ObserverBoardLayout(
            new Dictionary<Guid, ObserverSheetCard>
            {
                [snapshot.Sheets[0].PageViewId] = new(
                    snapshot.Sheets[0], new ObserverRect(0, 0, 20, 20), false),
                [snapshot.Sheets[1].PageViewId] = new(
                    snapshot.Sheets[1], new ObserverRect(5000, 0, 20, 20), false),
            },
            new Dictionary<Guid, ObserverFolderFrame>(),
            new ObserverRect(0, 0, 5020, 20));
        var policy = new ObserverCanvasLodPolicy();
        var camera = new ObserverCamera(new ObserverPoint(2510, 10), 1);
        var viewport = new ObserverSize(800, 600);

        var first = policy.Evaluate(
            snapshot, layout, camera, viewport, ObserverPackingMode.CompactSheets);
        var second = policy.Evaluate(
            snapshot, layout, camera, viewport, ObserverPackingMode.CompactSheets);

        var firstEmpty = Assert.Single(first.FolderSummaries, summary => summary.FolderId == emptyFolderId);
        var secondEmpty = Assert.Single(second.FolderSummaries, summary => summary.FolderId == emptyFolderId);
        Assert.Equal(0, firstEmpty.LayoutCount);
        Assert.Equal(firstEmpty.WorldBounds, secondEmpty.WorldBounds);
        Assert.Equal(firstEmpty.ScreenBounds, secondEmpty.ScreenBounds);
    }

    private static ObserverSnapshot Snapshot()
    {
        var root = Guid.NewGuid();
        var firstFolder = Guid.NewGuid();
        var secondFolder = Guid.NewGuid();
        return new ObserverSnapshot(
            DocumentRuntimeSerialNumber: 42,
            Revision: 1,
            DocumentName: "LOD",
            RootFolderId: root,
            Folders: [
                new ObserverFolderSnapshot(root, null, "LOD", 0),
                new ObserverFolderSnapshot(firstFolder, root, "Plans", 0),
                new ObserverFolderSnapshot(secondFolder, root, "Details", 1),
            ],
                    Sheets: [
                Sheet(firstFolder, "A-001", 0),
                Sheet(secondFolder, "A-002", 1),
            ],
                    CanvasState: ObserverCanvasState.Empty);
    }

    private static ObserverSheetSnapshot Sheet(Guid folderId, string name, int order) => new(
        Guid.NewGuid(),
        folderId,
        name,
        order,
        420,
        297,
        "Millimeters",
        [],
        true,
        1);
}

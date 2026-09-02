using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class ObserverCanvasStatePlannerTests
{
    [Fact]
    public void PlannerFreezesExpectedAndNewBoardState()
    {
        var snapshot = Snapshot();
        var sheetId = snapshot.Sheets.Keys.Single();
        var state = snapshot.Canvas with
        {
            SheetPlacements = new Dictionary<Guid, ObserverPointRecord> { [sheetId] = new(20, 40) },
        };

        var plan = new SetObserverCanvasStatePlanner().Plan(
            new SetObserverCanvasStateRequest(10, 5, state), snapshot);

        Assert.True(plan.CanApply);
        Assert.True(plan.Changes.Single() is SetObserverCanvasStateChange);
        var change = (SetObserverCanvasStateChange)plan.Changes.Single();
        Assert.True(ObserverCanvasStateComparer.ContentEquals(snapshot.Canvas, change.ExpectedState));
        Assert.Equal(new ObserverPointRecord(20, 40), change.NewState.SheetPlacements[sheetId]);
    }

    [Fact]
    public void NonFinitePlacementIsRejected()
    {
        var snapshot = Snapshot();
        var state = snapshot.Canvas with
        {
            FolderOrigins = new Dictionary<Guid, ObserverPointRecord>
            {
                [snapshot.RootFolderId] = new(double.NaN, 0),
            },
        };

        var plan = new SetObserverCanvasStatePlanner().Plan(
            new SetObserverCanvasStateRequest(10, 5, state), snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "observer.invalid_placement");
    }

    [Fact]
    public void NonFiniteAppearanceStatePlacementIsRejected()
    {
        var snapshot = Snapshot();
        var state = snapshot.Canvas with
        {
            AppearanceStatePlacements = new Dictionary<Guid, ObserverPointRecord>
            {
                [Guid.NewGuid()] = new(0, double.PositiveInfinity),
            },
        };

        var plan = new SetObserverCanvasStatePlanner().Plan(
            new SetObserverCanvasStateRequest(10, 5, state), snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "observer.invalid_placement");
    }

    private static DocumentSnapshot Snapshot()
    {
        var root = Guid.NewGuid();
        var sheetId = Guid.NewGuid();
        return new DocumentSnapshot(
            10,
            5,
            root,
            new Dictionary<Guid, FolderRecord> { [root] = new(root, null, "Root", 0) },
            new Dictionary<Guid, SheetSnapshot>
            {
                [sheetId] = new(sheetId, root, 0, "Page", [], new Dictionary<string, string>()),
            },
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            ObserverCanvas: ObserverCanvasState.Empty);
    }
}

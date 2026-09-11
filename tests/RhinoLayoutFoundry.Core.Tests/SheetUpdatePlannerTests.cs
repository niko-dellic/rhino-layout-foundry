using System.Text.Json;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Extensibility;
namespace RhinoLayoutFoundry.Core.Tests;
public sealed class SheetUpdatePlannerTests
{
    private static SheetUpdateSpecification Proposal() => new(Guid.NewGuid(), 42, 1,
        [new(TestSnapshots.SheetOneId, null, null, [], [], [])], []);
    [Fact]
    public void UpdateRetainsSheetIdentityAndFreezesInput()
    {
        var snapshot = TestSnapshots.Create();
        var updates = new List<SheetUpdate>(Proposal().Sheets); var spec = Proposal() with { Sheets = updates };
        var plan = SheetUpdatePlanner.Plan(spec, snapshot);
        Assert.True(plan.CanApply);
        updates.Clear();
        var change = Assert.IsType<UpdateSheetsChange>(Assert.Single(plan.Changes));
        Assert.Equal(TestSnapshots.SheetOneId, Assert.Single(change.Specification.Sheets).SheetId);
        Assert.Equal(2, snapshot.Sheets.Count);
    }
    [Fact]
    public void StaleAndReplayedProposalsAreRejected()
    {
        var snapshot = TestSnapshots.Create(); var spec = Proposal();
        Assert.False(SheetUpdatePlanner.Plan(spec with { SourceRevision = 0 }, snapshot).CanApply);
        Assert.False(SheetUpdatePlanner.Plan(spec, snapshot with { Metadata = new Dictionary<string,string> { [DrawingSetPlanner.ReceiptKey(spec.ProposalId)] = "{}" } }).CanApply);
    }
    [Fact]
    public void ForeignSheetAndDuplicateTargetsAreRejected()
    {
        var spec = Proposal(); var sheet = spec.Sheets[0];
        Assert.False(SheetUpdatePlanner.Plan(spec with { Sheets = [sheet with { SheetId = Guid.NewGuid() }] }, TestSnapshots.Create()).CanApply);
        Assert.False(SheetUpdatePlanner.Plan(spec with { Sheets = [sheet, sheet] }, TestSnapshots.Create()).CanApply);
    }
    [Fact]
    public void NewPresentationAndIsometricFieldsRoundTrip()
    {
        var snapshot = TestSnapshots.Create();
        var spec = new DrawingSetSpecification(2, Guid.NewGuid(), 42, 1, snapshot.RootFolderId,
            [new("s", "New", 420, 297, [new("iso", "Upper", "isometric", 20, new(20, 30, 200, 150), new(10, 10, 10), new(0, 0, 0), new(0, 0, 1), null) { HiddenLayerIds = [] }])]);
        Assert.True(DrawingSetSpecificationValidator.Validate(spec, snapshot).IsValid);
        var json = JsonSerializer.SerializeToElement(spec, SheetUpdatePlanner.Json);
        Assert.Equal("off", DrawingSetSpecificationValidator.Parse(json).TitleBlock);
        Assert.False(DrawingSetSpecificationValidator.Validate(spec with { DisplayModeId = Guid.NewGuid() }, snapshot).IsValid);
    }
    [Fact]
    public void StyleAndTitleblockChangesAreIncludedInDigest()
    {
        var spec = Proposal();
        Assert.NotEqual(SheetUpdatePlanner.Digest(spec), SheetUpdatePlanner.Digest(spec with
            { Sheets = [spec.Sheets[0] with { TitleBlock = "bottom", DimensionStyleId = Guid.NewGuid() }] }));
    }
}

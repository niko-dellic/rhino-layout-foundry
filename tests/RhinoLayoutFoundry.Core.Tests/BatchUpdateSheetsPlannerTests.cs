using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class BatchUpdateSheetsPlannerTests
{
    private static readonly Guid TitleBlockInstanceId = Guid.Parse("60000000-0000-0000-0000-000000000001");
    private static readonly Guid TitleBlockDefinitionId = Guid.Parse("60000000-0000-0000-0000-000000000002");

    [Fact]
    public void FolderSelectionResolvesAllDescendantSheetsWithProperties()
    {
        var snapshot = EnrichedSnapshot();
        var targets = BatchTargetResolver.Resolve(snapshot,
            [new OverviewNodeKey(OverviewNodeKind.Folder, TestSnapshots.RootFolderId)]);

        Assert.Equal(2, targets.Count);
        Assert.Equal(420d, targets.Single(item => item.Key.Id == TestSnapshots.SheetOneId).PageWidth);
        Assert.Equal("Rendered", targets.Single(item => item.Key.Id == TestSnapshots.SheetTwoId).DisplayModeSummary);
        Assert.Equal("A3 Title Block", targets.Single(item => item.Key.Id == TestSnapshots.SheetOneId).TitleBlockSummary);
    }

    [Fact]
    public void BatchStagesNamesPaperAndDisplayModeTogether()
    {
        var snapshot = EnrichedSnapshot();
        var request = new BatchUpdateSheetsRequest(42, 1,
            [TestSnapshots.SheetOneId, TestSnapshots.SheetTwoId],
            "L-{index:00}", 3, 1, 11, 17, "Inches", TestSnapshots.DisplayModeTwoId);
        var plan = new BatchUpdateSheetsPlanner().Plan(request, snapshot);

        Assert.True(plan.CanApply);
        var change = (BatchUpdateSheetsChange)plan.Changes.Single();
        Assert.Equal("L-03", change.NewNames[TestSnapshots.SheetOneId]);
        Assert.Equal(11d, change.PaperWidth);
        Assert.Equal(TestSnapshots.DisplayModeTwoId, change.DetailDisplayModeId);
    }

    [Fact]
    public void InvalidDimensionsAndModeBlockApply()
    {
        var snapshot = EnrichedSnapshot();
        var plan = new BatchUpdateSheetsPlanner().Plan(new BatchUpdateSheetsRequest(
            42, 1, [TestSnapshots.SheetOneId], null, 1, 1,
            0, 297, "Millimeters", Guid.NewGuid()), snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "batch.paper_invalid");
        Assert.Contains(plan.Diagnostics, item => item.Code == "batch.display_mode_missing");
    }

    [Fact]
    public void TitleBlockInstanceCanBeAssignedOrRemovedAcrossBatch()
    {
        var snapshot = EnrichedSnapshot();
        var assign = new BatchUpdateSheetsPlanner().Plan(new BatchUpdateSheetsRequest(
            42, 1, [TestSnapshots.SheetOneId, TestSnapshots.SheetTwoId], null, 1, 1,
            null, null, null, null, true, TitleBlockInstanceId), snapshot);
        var remove = new BatchUpdateSheetsPlanner().Plan(new BatchUpdateSheetsRequest(
            42, 1, [TestSnapshots.SheetOneId], null, 1, 1,
            null, null, null, null, true, null), snapshot);

        Assert.True(assign.CanApply);
        Assert.Equal(TitleBlockInstanceId,
            ((BatchUpdateSheetsChange)assign.Changes.Single()).TitleBlockSourceInstanceObjectId);
        Assert.True(remove.CanApply);
        Assert.True(((BatchUpdateSheetsChange)remove.Changes.Single()).ChangeTitleBlock);
    }

    [Fact]
    public void MissingTitleBlockInstanceBlocksApply()
    {
        var snapshot = EnrichedSnapshot();
        var plan = new BatchUpdateSheetsPlanner().Plan(new BatchUpdateSheetsRequest(
            42, 1, [TestSnapshots.SheetOneId], null, 1, 1,
            null, null, null, null, true, Guid.NewGuid()), snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "batch.title_block_missing");
    }

    private static DocumentSnapshot EnrichedSnapshot()
    {
        var source = TestSnapshots.Create();
        var wireframe = new DetailSnapshot(TestSnapshots.DetailOneId, "Plan",
            TestSnapshots.DisplayModeOneId, "Wireframe");
        var rendered = new DetailSnapshot(TestSnapshots.DetailTwoId, "Section",
            TestSnapshots.DisplayModeTwoId, "Rendered");
        return source with
        {
            Sheets = new Dictionary<Guid, SheetSnapshot>
            {
                [TestSnapshots.SheetOneId] = source.Sheets[TestSnapshots.SheetOneId] with
                {
                    PageWidth = 420, PageHeight = 297, PageUnitSystem = "Millimeters",
                    DetailSettings = [wireframe],
                    TitleBlockInstanceObjectId = TitleBlockInstanceId,
                    TitleBlockDefinitionName = "A3 Title Block",
                },
                [TestSnapshots.SheetTwoId] = source.Sheets[TestSnapshots.SheetTwoId] with
                {
                    PageWidth = 11, PageHeight = 17, PageUnitSystem = "Inches",
                    DetailSettings = [rendered],
                },
            },
            DisplayModeNames = new Dictionary<Guid, string>
            {
                [TestSnapshots.DisplayModeOneId] = "Wireframe",
                [TestSnapshots.DisplayModeTwoId] = "Rendered",
            },
            TitleBlockInstanceChoices = new Dictionary<Guid, TitleBlockInstanceSnapshot>
            {
                [TitleBlockInstanceId] = new(
                    TitleBlockInstanceId,
                    TitleBlockDefinitionId,
                    "A3 Title Block",
                    TestSnapshots.SheetOneId,
                    "A-001"),
            },
        };
    }
}

using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class SheetTemplatePlannerTests
{
    [Fact]
    public void CaptureCreatesVersionedTemplateChange()
    {
        var snapshot = WithTemplates(TestSnapshots.Create(), []);
        var source = snapshot.Sheets.Values.First();
        var request = new CaptureSheetTemplateRequest(42, 1, Guid.NewGuid(), source.PageViewId,
            "A3 Plan", "A-{index:000}", null);

        var plan = new CaptureSheetTemplatePlanner().Plan(request, snapshot);

        Assert.True(plan.CanApply);
        Assert.Equal("A3 Plan", ((CaptureSheetTemplateChange)plan.Changes.Single()).Name);
    }

    [Fact]
    public void CaptureRejectsAnAlreadyRegisteredSourceLayout()
    {
        var snapshot = TestSnapshots.Create();
        var source = snapshot.Sheets.Values.First();
        var existing = Template("Existing", 420, 297) with
        {
            SourcePageViewId = source.PageViewId,
        };
        snapshot = WithTemplates(snapshot, [existing]);

        var plan = new CaptureSheetTemplatePlanner().Plan(new CaptureSheetTemplateRequest(
            42, 1, Guid.NewGuid(), source.PageViewId, "Another", "{index:00}", null), snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, diagnostic => diagnostic.Code == "template.source_registered");
    }

    [Fact]
    public void MixedTemplateBatchProducesOrderedUniqueNames()
    {
        var a3 = Template("A3", 420, 297);
        var a1 = Template("A1", 841, 594);
        var snapshot = WithTemplates(TestSnapshots.Create(), [a3, a1]);
        var request = new BatchCreateSheetsRequest(42, 1, TestSnapshots.OtherFolderId,
            [new TemplateQuantity(a3.Id, 2), new TemplateQuantity(a1.Id, 1)],
            "S-{index:000}", 3, 2);

        var plan = new BatchCreateSheetsPlanner().Plan(request, snapshot);

        Assert.True(plan.CanApply);
        Assert.Equal(["S-003", "S-005", "S-007"],
            plan.Changes.Cast<CreateSheetFromTemplateChange>().Select(item => item.Name));
        Assert.Equal(["A3", "A3", "A1"],
            plan.Changes.Cast<CreateSheetFromTemplateChange>().Select(item => item.Template.Name));
    }

    [Fact]
    public void ExistingNameBlocksWholeBatch()
    {
        var template = Template("A3", 420, 297);
        var snapshot = WithTemplates(TestSnapshots.Create(), [template]);
        var plan = new BatchCreateSheetsPlanner().Plan(new BatchCreateSheetsRequest(
            42, 1, TestSnapshots.RootFolderId, [new TemplateQuantity(template.Id, 2)],
            "A-{index:000}", 1, 1), snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "batch.name_exists");
        Assert.Empty(plan.Changes);
    }

    [Fact]
    public void InvalidDetailRectangleBlocksBatch()
    {
        var invalid = Template("Broken", 420, 297) with
        {
            DetailSlots = [new DetailSlotRecipe(Guid.NewGuid(), "Bad", 10, 10, 5, 20,
                "Top", null, false, null, null)],
        };
        var snapshot = WithTemplates(TestSnapshots.Create(), [invalid]);
        var plan = new BatchCreateSheetsPlanner().Plan(new BatchCreateSheetsRequest(
            42, 1, TestSnapshots.RootFolderId, [new TemplateQuantity(invalid.Id, 1)],
            "X-{index}", 1, 1), snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "template.detail_bounds_invalid");
    }

    [Fact]
    public void MissingNamedViewBlocksBatchBeforeRhinoMutation()
    {
        var template = Template("A3", 420, 297);
        var snapshot = WithTemplates(TestSnapshots.Create(), [template]);
        var slot = template.DetailSlots.Single();
        var plan = new BatchCreateSheetsPlanner().Plan(new BatchCreateSheetsRequest(
            42, 1, TestSnapshots.RootFolderId, [new TemplateQuantity(template.Id, 1)],
            "X-{index}", 1, 1, new Dictionary<Guid, string> { [slot.Id] = "Deleted View" }), snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "template.named_view_unresolved");
    }

    [Fact]
    public void BuiltInGridBatchAppliesPaperAndDisplayModeToEveryDetail()
    {
        var snapshot = WithTemplates(TestSnapshots.Create(), []) with
        {
            DisplayModeNames = new Dictionary<Guid, string>
            {
                [TestSnapshots.DisplayModeOneId] = "Technical",
            },
        };
        var request = new BatchCreateSheetsRequest(
            42,
            1,
            TestSnapshots.RootFolderId,
            [],
            "Page {index}",
            1,
            1,
            CreationSpecs:
            [
                new LayoutCreationSpec(
                    3,
                    new PaperRecipe(594, 420, "Millimeters"),
                    BuiltInLayoutKind.FourDetailsGrid,
                    DetailDisplayModeId: TestSnapshots.DisplayModeOneId,
                    UseTemplateTitleBlock: false),
            ]);

        var plan = new BatchCreateSheetsPlanner().Plan(request, snapshot);

        Assert.True(plan.CanApply);
        var changes = plan.Changes.Cast<CreateSheetFromTemplateChange>().ToArray();
        Assert.Equal(["Page 1", "Page 2", "Page 3"], changes.Select(change => change.Name));
        Assert.All(changes, change => Assert.Equal(4, change.Template.DetailSlots.Count));
        Assert.All(changes, change => Assert.True(change.UseDedicatedDetailLayer));
        Assert.All(changes.SelectMany(change => change.Template.DetailSlots), detail =>
            Assert.Equal(TestSnapshots.DisplayModeOneId, detail.DisplayModeId));
    }

    [Fact]
    public void DedicatedDetailLayerChoiceIsPreservedPerCreationSpec()
    {
        var snapshot = WithTemplates(TestSnapshots.Create(), []);
        var plan = new BatchCreateSheetsPlanner().Plan(new BatchCreateSheetsRequest(
            42,
            1,
            TestSnapshots.RootFolderId,
            [],
            "Layer {index}",
            1,
            1,
            CreationSpecs:
            [
                new LayoutCreationSpec(
                    1,
                    new PaperRecipe(594, 420, "Millimeters"),
                    UseDedicatedDetailLayer: false),
                new LayoutCreationSpec(
                    1,
                    new PaperRecipe(594, 420, "Millimeters"),
                    UseDedicatedDetailLayer: true),
            ]), snapshot);

        Assert.True(plan.CanApply);
        Assert.Equal([false, true], plan.Changes.Cast<CreateSheetFromTemplateChange>()
            .Select(change => change.UseDedicatedDetailLayer));
    }

    [Fact]
    public void CapturedTemplateIsScaledToSelectedPaperSize()
    {
        var template = Template("A3", 420, 297);
        var snapshot = WithTemplates(TestSnapshots.Create(), [template]);
        var plan = new BatchCreateSheetsPlanner().Plan(new BatchCreateSheetsRequest(
            42,
            1,
            TestSnapshots.RootFolderId,
            [],
            "Scaled {index}",
            1,
            1,
            CreationSpecs:
            [
                new LayoutCreationSpec(
                    1,
                    new PaperRecipe(840, 594, "Millimeters"),
                    TemplateId: template.Id),
            ]), snapshot);

        Assert.True(plan.CanApply);
        var created = Assert.IsType<CreateSheetFromTemplateChange>(Assert.Single(plan.Changes));
        var detail = Assert.Single(created.Template.DetailSlots);
        Assert.Equal(20, detail.Left);
        Assert.Equal(820, detail.Right);
        Assert.Equal(20, detail.Bottom);
        Assert.Equal(554, detail.Top);
    }

    [Fact]
    public void SelectedTitleBlockInstanceBecomesCreationRecipe()
    {
        var instanceId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        var transform = Enumerable.Range(0, 16).Select(index => (double)index).ToArray();
        var snapshot = WithTemplates(TestSnapshots.Create(), []) with
        {
            InstanceDefinitionIds = new HashSet<Guid> { definitionId },
            TitleBlockInstanceChoices = new Dictionary<Guid, TitleBlockInstanceSnapshot>
            {
                [instanceId] = new(
                    instanceId,
                    definitionId,
                    "A2 Title Block",
                    TestSnapshots.SheetOneId,
                    "A-001",
                    transform,
                    "Bottom right"),
            },
        };
        var plan = new BatchCreateSheetsPlanner().Plan(new BatchCreateSheetsRequest(
            42,
            1,
            TestSnapshots.RootFolderId,
            [],
            "TB {index}",
            1,
            1,
            CreationSpecs:
            [
                new LayoutCreationSpec(
                    1,
                    new PaperRecipe(594, 420, "Millimeters"),
                    TitleBlockSourceInstanceObjectId: instanceId,
                    UseTemplateTitleBlock: false),
            ]), snapshot);

        Assert.True(plan.CanApply);
        var created = Assert.IsType<CreateSheetFromTemplateChange>(Assert.Single(plan.Changes));
        Assert.NotNull(created.Template.TitleBlock);
        Assert.Equal(definitionId, created.Template.TitleBlock.InstanceDefinitionId);
        Assert.Equal("Bottom right", created.Template.TitleBlock.AnchorName);
        Assert.Equal(transform, created.Template.TitleBlock.Transform);
    }

    private static SheetTemplateRecipe Template(string name, double width, double height) => new(
        Guid.NewGuid(), SheetTemplateRecipe.CurrentRecipeVersion, name,
        new PaperRecipe(width, height, "Millimeters"),
        [new DetailSlotRecipe(Guid.NewGuid(), "Plan", 10, 10, width - 10, height - 20,
            "Top", 0.02, true, null, null)],
        null, [], new Dictionary<string, string>(), "{index:000}");

    private static DocumentSnapshot WithTemplates(
        DocumentSnapshot snapshot,
        IReadOnlyList<SheetTemplateRecipe> templates) => snapshot with
        {
            SheetTemplates = templates,
            DocumentMetadata = new Dictionary<string, string> { ["project"] = "Foundry" },
            NamedViewNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            InstanceDefinitionIds = new HashSet<Guid>(),
        };
}

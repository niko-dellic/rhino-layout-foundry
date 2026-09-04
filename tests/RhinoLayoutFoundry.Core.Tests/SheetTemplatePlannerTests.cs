using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class SheetTemplatePlannerTests
{

    [Fact]
    public void MixedTemplateBatchProducesOrderedUniqueNames()
    {
        var a3 = Template("A3", 420, 297);
        var a1 = Template("A1", 841, 594);
        var snapshot = WithTemplates(TestSnapshots.Create(), [a3, a1]);
        var request = new BatchCreateSheetsRequest(DocumentRuntimeSerialNumber: 42, SourceRevision: 1, DestinationFolderId: TestSnapshots.OtherFolderId,
            NamingPattern: "S-{index:000}", Start: 3, Step: 2, CreationSpecs: [new LayoutCreationSpec(2, a3.Paper, TemplateId: a3.Id), new LayoutCreationSpec(1, a1.Paper, TemplateId: a1.Id)]);

        var plan = new BatchCreateSheetsPlanner().Plan(request, snapshot);

        Assert.True(plan.CanApply);
        Assert.Equal(["S-002", "S-003", "S-004"],
            plan.Changes.Cast<CreateSheetFromTemplateChange>().Select(item => item.Name));
        Assert.Equal(["A3", "A3", "A1"],
            plan.Changes.Cast<CreateSheetFromTemplateChange>().Select(item => item.Template.Name));
        Assert.All(plan.Changes.Cast<CreateSheetFromTemplateChange>(),
            item => Assert.Equal("S-{index:000}", item.NamingPattern));
        Assert.Equal([2, 3, 4],
            plan.Changes.Cast<CreateSheetFromTemplateChange>().Select(item => item.NamingIndex));
    }

    [Fact]
    public void ExistingNamesAreSkippedAutomaticallyAcrossTheDocument()
    {
        var template = Template("A3", 420, 297);
        var snapshot = WithTemplates(TestSnapshots.Create(), [template]);
        var plan = new BatchCreateSheetsPlanner().Plan(new BatchCreateSheetsRequest(
            DocumentRuntimeSerialNumber: 42, SourceRevision: 1, DestinationFolderId: TestSnapshots.RootFolderId, NamingPattern: "A-{index:000}", Start: 1, Step: 1, CreationSpecs: [new LayoutCreationSpec(2, template.Paper, TemplateId: template.Id)]), snapshot);

        Assert.True(plan.CanApply);
        var changes = plan.Changes.Cast<CreateSheetFromTemplateChange>().ToArray();
        Assert.Equal(["A-003", "A-004"], changes.Select(change => change.Name));
        Assert.Equal([3, 4], changes.Select(change => change.NamingIndex));
    }

    [Fact]
    public void InvalidDetailRectangleBlocksBatch()
    {
        var invalid = Template("Broken", 420, 297) with
        {
            DetailSlots = [new DetailSlotRecipe(Id: Guid.NewGuid(), Name: "Bad", Left: 10, Bottom: 10, Right: 5, Top: 20,
                Projection:                 "Top", PageToModelRatio: null, ProjectionLocked: false, DisplayModeId: null, DefaultNamedView: null)],
        };
        var snapshot = WithTemplates(TestSnapshots.Create(), [invalid]);
        var plan = new BatchCreateSheetsPlanner().Plan(new BatchCreateSheetsRequest(
            DocumentRuntimeSerialNumber: 42, SourceRevision: 1, DestinationFolderId: TestSnapshots.RootFolderId, NamingPattern: "X-{index}", Start: 1, Step: 1, CreationSpecs: [new LayoutCreationSpec(1, invalid.Paper, TemplateId: invalid.Id)]), snapshot);

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
            DocumentRuntimeSerialNumber: 42, SourceRevision: 1, DestinationFolderId: TestSnapshots.RootFolderId, NamingPattern: "X-{index}", Start: 1, Step: 1, CreationSpecs: [new LayoutCreationSpec(1, template.Paper, TemplateId: template.Id, NamedViewsByDetail: ["Deleted View"])]), snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "template.named_view_unresolved");
    }

    [Fact]
    public void BuiltInGridBatchAppliesPaperAndDisplayModeToEveryDetail()
    {
        var snapshot = WithTemplates(TestSnapshots.Create(), []) with
        {
            DisplayModes = new Dictionary<Guid, string>
            {
                [TestSnapshots.DisplayModeOneId] = "Technical",
            },
        };
        var request = new BatchCreateSheetsRequest(
            DocumentRuntimeSerialNumber: 42,
            SourceRevision: 1,
            DestinationFolderId: TestSnapshots.RootFolderId,
            NamingPattern: "Page {index}",
            Start: 1,
            Step: 1,
            CreationSpecs:
            [
                new LayoutCreationSpec(
                    3,
                    new PaperRecipe(594, 420, "Millimeters"),
                    BuiltInLayoutKind.FourDetailsGrid,
                    DetailDisplayModeId: TestSnapshots.DisplayModeOneId),
            ]);

        var plan = new BatchCreateSheetsPlanner().Plan(request, snapshot);

        Assert.True(plan.CanApply);
        var changes = plan.Changes.Cast<CreateSheetFromTemplateChange>().ToArray();
        Assert.Equal(["Page 1", "Page 2", "Page 3"], changes.Select(change => change.Name));
        Assert.All(changes, change => Assert.Equal(4, change.Template.DetailSlots.Count));
        Assert.All(changes, change => Assert.True(change.UseDedicatedDetailLayer));
        Assert.All(changes, change =>
        {
            Assert.Equal(10.5, change.Template.DetailSlots.Min(detail => detail.Left), 3);
            Assert.Equal(583.5, change.Template.DetailSlots.Max(detail => detail.Right), 3);
            Assert.Equal(10.5, change.Template.DetailSlots.Min(detail => detail.Bottom), 3);
            Assert.Equal(409.5, change.Template.DetailSlots.Max(detail => detail.Top), 3);
        });
        Assert.All(changes.SelectMany(change => change.Template.DetailSlots), detail =>
            Assert.Equal(TestSnapshots.DisplayModeOneId, detail.DisplayModeId));
    }

    [Fact]
    public void BuiltInSingleDetailUsesSingleSpreadLabel()
    {
        var snapshot = WithTemplates(TestSnapshots.Create(), []);
        var plan = new BatchCreateSheetsPlanner().Plan(new BatchCreateSheetsRequest(
            DocumentRuntimeSerialNumber: 42,
            SourceRevision: 1,
            DestinationFolderId: TestSnapshots.RootFolderId,
            NamingPattern: "Page {index}",
            Start: 1,
            Step: 1,
            CreationSpecs:
            [
                new LayoutCreationSpec(
                    1,
                    new PaperRecipe(594, 420, "Millimeters"),
                    BuiltInLayoutKind.SingleDetail),
            ]), snapshot);

        Assert.True(plan.CanApply);
        var change = Assert.IsType<CreateSheetFromTemplateChange>(Assert.Single(plan.Changes));
        Assert.Equal("1 Detail — Single spread", change.Template.Name);
    }

    [Fact]
    public void PerDetailDisplayModeOverridesTakePrecedenceOverPageDefault()
    {
        var snapshot = WithTemplates(TestSnapshots.Create(), []);
        var plan = new BatchCreateSheetsPlanner().Plan(new BatchCreateSheetsRequest(
            DocumentRuntimeSerialNumber: 42,
            SourceRevision: 1,
            DestinationFolderId: TestSnapshots.RootFolderId,
            NamingPattern: "Page {index}",
            Start: 1,
            Step: 1,
            CreationSpecs:
            [
                new LayoutCreationSpec(
                    1,
                    new PaperRecipe(594, 420, "Millimeters"),
                    BuiltInLayoutKind.TwoDetailsVertical,
                    DetailDisplayModeId: TestSnapshots.DisplayModeOneId,
                    DetailDisplayModesByDetail: [null, TestSnapshots.DisplayModeTwoId]),
            ]), snapshot);

        Assert.True(plan.CanApply);
        var details = Assert.Single(plan.Changes.Cast<CreateSheetFromTemplateChange>()).Template.DetailSlots;
        Assert.Equal(TestSnapshots.DisplayModeOneId, details[0].DisplayModeId);
        Assert.Equal(TestSnapshots.DisplayModeTwoId, details[1].DisplayModeId);
    }

    [Fact]
    public void BatchCreationAppliesUnifiedAppearanceState()
    {
        var appearanceStateId = Guid.NewGuid();
        var layerId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        var layerRule = new LayerVisibilityRule(
            new LayerReference(layerId, "Architecture::Notes"),
            LayerVisibilityOverride.Hidden);
        var objectRule = new ObjectDisplayRule(
            new ObjectDisplaySelector(ObjectDisplaySelectorKind.ExactObject, ObjectId: objectId),
            TestSnapshots.DisplayModeOneId,
            "Technical");
        var snapshot = WithTemplates(TestSnapshots.Create(), []) with
        {
            AppearanceStates =
            [
                new AppearanceStateRecord(appearanceStateId, TestSnapshots.RootFolderId, 0,
                    "Presentation appearance", [layerRule], [objectRule]),
            ],
        };
        var plan = new BatchCreateSheetsPlanner().Plan(new BatchCreateSheetsRequest(
            DocumentRuntimeSerialNumber: 42,
            SourceRevision: 1,
            DestinationFolderId: TestSnapshots.RootFolderId,
            NamingPattern: "Page {index}",
            Start: 1,
            Step: 1,
            CreationSpecs:
            [
                new LayoutCreationSpec(
                    1,
                    new PaperRecipe(594, 420, "Millimeters"),
                    BuiltInLayoutKind.SingleDetail,
                    AppearanceStateId: appearanceStateId),
            ]), snapshot);

        Assert.True(plan.CanApply);
        var change = Assert.IsType<CreateSheetFromTemplateChange>(Assert.Single(plan.Changes));
        Assert.Equal(appearanceStateId, change.AppearanceStateId);
        var detail = Assert.Single(change.Template.DetailSlots);
        Assert.Empty(detail.LayerRules);
        Assert.Empty(detail.ObjectDisplayRules);
    }

    [Fact]
    public void BatchCreationMapsAppearanceStatesToResolvedDetailSlots()
    {
        var firstStateId = Guid.NewGuid();
        var secondStateId = Guid.NewGuid();
        var snapshot = WithTemplates(TestSnapshots.Create(), []) with
        {
            AppearanceStates =
            [
                new AppearanceStateRecord(firstStateId, TestSnapshots.RootFolderId, 0, "First", [], []),
                new AppearanceStateRecord(secondStateId, TestSnapshots.RootFolderId, 1, "Second", [], []),
            ],
        };
        var plan = new BatchCreateSheetsPlanner().Plan(new BatchCreateSheetsRequest(
            DocumentRuntimeSerialNumber: 42, SourceRevision: 1, DestinationFolderId: TestSnapshots.RootFolderId, NamingPattern: "Page {index}", Start: 1, Step: 1,
            CreationSpecs:
            [
                new LayoutCreationSpec(
                    1,
                    new PaperRecipe(594, 420, "Millimeters"),
                    BuiltInLayoutKind.TwoDetailsHorizontal,
                    AppearanceStatesByDetail: [firstStateId, secondStateId]),
            ]), snapshot);

        Assert.True(plan.CanApply);
        var change = Assert.IsType<CreateSheetFromTemplateChange>(Assert.Single(plan.Changes));
        Assert.Equal(
            [firstStateId, secondStateId],
            change.Template.DetailSlots.Select(slot =>
                change.DetailAppearanceStateAssignments!.GetValueOrDefault(slot.Id)));
    }

    [Fact]
    public void WrongPerDetailDisplayModeCountBlocksBatch()
    {
        var snapshot = WithTemplates(TestSnapshots.Create(), []);
        var plan = new BatchCreateSheetsPlanner().Plan(new BatchCreateSheetsRequest(
            DocumentRuntimeSerialNumber: 42,
            SourceRevision: 1,
            DestinationFolderId: TestSnapshots.RootFolderId,
            NamingPattern: "Page {index}",
            Start: 1,
            Step: 1,
            CreationSpecs:
            [
                new LayoutCreationSpec(
                    1,
                    new PaperRecipe(594, 420, "Millimeters"),
                    BuiltInLayoutKind.TwoDetailsHorizontal,
                    DetailDisplayModesByDetail: [TestSnapshots.DisplayModeOneId]),
            ]), snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, diagnostic =>
            diagnostic.Code == "template.display_mode_assignment_count");
    }

    [Fact]
    public void MissingPerDetailDisplayModeBlocksBatch()
    {
        var snapshot = WithTemplates(TestSnapshots.Create(), []);
        var missingId = Guid.Parse("50000000-0000-0000-0000-000000000099");
        var plan = new BatchCreateSheetsPlanner().Plan(new BatchCreateSheetsRequest(
            DocumentRuntimeSerialNumber: 42,
            SourceRevision: 1,
            DestinationFolderId: TestSnapshots.RootFolderId,
            NamingPattern: "Page {index}",
            Start: 1,
            Step: 1,
            CreationSpecs:
            [
                new LayoutCreationSpec(
                    1,
                    new PaperRecipe(594, 420, "Millimeters"),
                    BuiltInLayoutKind.SingleDetail,
                    DetailDisplayModesByDetail: [missingId]),
            ]), snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, diagnostic => diagnostic.Code == "template.display_mode_unresolved");
    }

    [Fact]
    public void DedicatedDetailLayerChoiceIsPreservedPerCreationSpec()
    {
        var snapshot = WithTemplates(TestSnapshots.Create(), []);
        var plan = new BatchCreateSheetsPlanner().Plan(new BatchCreateSheetsRequest(
            DocumentRuntimeSerialNumber: 42,
            SourceRevision: 1,
            DestinationFolderId: TestSnapshots.RootFolderId,
            NamingPattern: "Layer {index}",
            Start: 1,
            Step: 1,
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
    public void SelectedDetailLayerIsPreservedByIdentity()
    {
        var layerId = Guid.Parse("60000000-0000-0000-0000-000000000001");
        var snapshot = WithTemplates(TestSnapshots.Create(), []) with
        {
            Layers = new Dictionary<Guid, string> { [layerId] = "Documentation::Details" },
        };
        var plan = new BatchCreateSheetsPlanner().Plan(new BatchCreateSheetsRequest(
            DocumentRuntimeSerialNumber: 42,
            SourceRevision: 1,
            DestinationFolderId: TestSnapshots.RootFolderId,
            NamingPattern: "Layer {index}",
            Start: 1,
            Step: 1,
            CreationSpecs:
            [
                new LayoutCreationSpec(
                    1,
                    new PaperRecipe(594, 420, "Millimeters"),
                    UseDedicatedDetailLayer: false,
                    DetailLayerId: layerId),
            ]), snapshot);

        Assert.True(plan.CanApply);
        var change = Assert.IsType<CreateSheetFromTemplateChange>(Assert.Single(plan.Changes));
        Assert.False(change.UseDedicatedDetailLayer);
        Assert.Equal(layerId, change.DetailLayerId);
    }

    [Fact]
    public void MissingSelectedDetailLayerBlocksBatchBeforeMutation()
    {
        var snapshot = WithTemplates(TestSnapshots.Create(), []);
        var plan = new BatchCreateSheetsPlanner().Plan(new BatchCreateSheetsRequest(
            DocumentRuntimeSerialNumber: 42,
            SourceRevision: 1,
            DestinationFolderId: TestSnapshots.RootFolderId,
            NamingPattern: "Layer {index}",
            Start: 1,
            Step: 1,
            CreationSpecs:
            [
                new LayoutCreationSpec(
                    1,
                    new PaperRecipe(594, 420, "Millimeters"),
                    UseDedicatedDetailLayer: false,
                    DetailLayerId: Guid.NewGuid()),
            ]), snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, diagnostic => diagnostic.Code == "batch.detail_layer_missing");
        Assert.Empty(plan.Changes);
    }

    [Fact]
    public void InitialRevisionScheduleIsPreservedForEveryCreatedLayout()
    {
        var snapshot = WithTemplates(TestSnapshots.Create(), []);
        var revisions = new[]
        {
            new SheetRevisionRecord("P01", "2026-08-28", "Planning issue", "ND", "QA"),
            new SheetRevisionRecord("P02", "2026-09-02", "Client issue", "ND", "QA"),
        };
        var plan = new BatchCreateSheetsPlanner().Plan(new BatchCreateSheetsRequest(
            DocumentRuntimeSerialNumber: 42,
            SourceRevision: 1,
            DestinationFolderId: TestSnapshots.RootFolderId,
            NamingPattern: "Page {index}",
            Start: 1,
            Step: 1,
            CreationSpecs:
            [
                new LayoutCreationSpec(2, new PaperRecipe(594, 420, "Millimeters")),
            ],
            InitialRevisions: revisions), snapshot);

        Assert.True(plan.CanApply);
        var changes = plan.Changes.Cast<CreateSheetFromTemplateChange>().ToArray();
        Assert.Equal(2, changes.Length);
        Assert.All(changes, change => Assert.Equal(revisions, change.InitialRevisions));
    }

    [Fact]
    public void PerDetailNamedViewsAreMappedToResolvedDetailSlots()
    {
        var snapshot = WithTemplates(TestSnapshots.Create(), []) with
        {
            NamedViews = new HashSet<string>(["North", "South", "East", "West"],
                StringComparer.OrdinalIgnoreCase),
        };
        var plan = new BatchCreateSheetsPlanner().Plan(new BatchCreateSheetsRequest(
            DocumentRuntimeSerialNumber: 42, SourceRevision: 1, DestinationFolderId: TestSnapshots.RootFolderId, NamingPattern: "Views {index}", Start: 1, Step: 1,
            CreationSpecs:
            [
                new LayoutCreationSpec(
                    1,
                    new PaperRecipe(594, 420, "Millimeters"),
                    BuiltInLayoutKind.FourDetailsGrid,
                    NamedViewsByDetail: ["North", "South", "East", "West"]),
            ]), snapshot);

        Assert.True(plan.CanApply);
        var change = Assert.IsType<CreateSheetFromTemplateChange>(Assert.Single(plan.Changes));
        Assert.Equal(["North", "South", "East", "West"], change.Template.DetailSlots
            .Select(slot => change.NamedViewAssignments.GetValueOrDefault(slot.Id)));
    }

    [Fact]
    public void NullPerDetailAssignmentInheritsTemplateCameraAndRepeatedViewsAreAllowed()
    {
        var template = Template("Captured", 420, 297);
        var first = template.DetailSlots.Single() with { DefaultNamedView = "Captured camera" };
        var second = first with { Id = Guid.NewGuid(), Name = "Section", DefaultNamedView = null };
        template = template with { DetailSlots = [first, second] };
        var snapshot = WithTemplates(TestSnapshots.Create(), [template]) with
        {
            NamedViews = new HashSet<string>(["Captured camera", "Perspective"],
                StringComparer.OrdinalIgnoreCase),
        };
        var plan = new BatchCreateSheetsPlanner().Plan(new BatchCreateSheetsRequest(
            DocumentRuntimeSerialNumber: 42, SourceRevision: 1, DestinationFolderId: TestSnapshots.RootFolderId, NamingPattern: "Views {index}", Start: 1, Step: 1,
            CreationSpecs:
            [
                new LayoutCreationSpec(
                    1,
                    new PaperRecipe(420, 297, "Millimeters"),
                    TemplateId: template.Id,
                    NamedViewsByDetail: [null, "Perspective"]),
            ]), snapshot);

        Assert.True(plan.CanApply);
        var change = Assert.IsType<CreateSheetFromTemplateChange>(Assert.Single(plan.Changes));
        Assert.False(change.NamedViewAssignments.ContainsKey(change.Template.DetailSlots[0].Id));
        Assert.Equal("Captured camera", change.Template.DetailSlots[0].DefaultNamedView);
        Assert.Equal("Perspective", change.NamedViewAssignments[change.Template.DetailSlots[1].Id]);

        var repeated = new BatchCreateSheetsPlanner().Plan(new BatchCreateSheetsRequest(
            DocumentRuntimeSerialNumber: 42, SourceRevision: 1, DestinationFolderId: TestSnapshots.RootFolderId, NamingPattern: "Repeated {index}", Start: 1, Step: 1,
            CreationSpecs:
            [
                new LayoutCreationSpec(
                    1,
                    new PaperRecipe(420, 297, "Millimeters"),
                    TemplateId: template.Id,
                    NamedViewsByDetail: ["Perspective", "Perspective"]),
            ]), snapshot);
        Assert.True(repeated.CanApply);
    }

    [Fact]
    public void PerDetailAssignmentCountAndMissingViewsBlockBeforeMutation()
    {
        var snapshot = WithTemplates(TestSnapshots.Create(), []) with
        {
            NamedViews = new HashSet<string>(["Existing"], StringComparer.OrdinalIgnoreCase),
        };
        BatchCreateSheetsRequest Request(IReadOnlyList<string?> assignments) => new(
            42, 1, TestSnapshots.RootFolderId, NamingPattern: "Views {index}", Start: 1, Step: 1,
            CreationSpecs:
            [
                new LayoutCreationSpec(
                    1,
                    new PaperRecipe(594, 420, "Millimeters"),
                    BuiltInLayoutKind.TwoDetailsVertical,
                    NamedViewsByDetail: assignments),
            ]);

        var wrongCount = new BatchCreateSheetsPlanner().Plan(Request(["Existing"]), snapshot);
        Assert.False(wrongCount.CanApply);
        Assert.Empty(wrongCount.Changes);
        Assert.Contains(wrongCount.Diagnostics,
            diagnostic => diagnostic.Code == "template.named_view_assignment_count");

        var missing = new BatchCreateSheetsPlanner().Plan(Request(["Existing", "Deleted"]), snapshot);
        Assert.False(missing.CanApply);
        Assert.Empty(missing.Changes);
        Assert.Contains(missing.Diagnostics,
            diagnostic => diagnostic.Code == "template.named_view_unresolved");
    }

    [Fact]
    public void CapturedTemplateIsScaledToSelectedPaperSize()
    {
        var template = Template("A3", 420, 297);
        var snapshot = WithTemplates(TestSnapshots.Create(), [template]);
        var plan = new BatchCreateSheetsPlanner().Plan(new BatchCreateSheetsRequest(
            DocumentRuntimeSerialNumber: 42,
            SourceRevision: 1,
            DestinationFolderId: TestSnapshots.RootFolderId,
            NamingPattern: "Scaled {index}",
            Start: 1,
            Step: 1,
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
    public void AdaptiveTitleBlockReservesSpaceAndCarriesSheetNumber()
    {
        var snapshot = WithTemplates(TestSnapshots.Create(), []);
        var project = ProjectInformation.Empty with
        {
            ProjectName = "Civic Library",
            FirmName = "Foundry Architects",
        };
        var plan = new BatchCreateSheetsPlanner().Plan(new BatchCreateSheetsRequest(
            DocumentRuntimeSerialNumber: 42,
            SourceRevision: 1,
            DestinationFolderId: TestSnapshots.RootFolderId,
            NamingPattern: "A-{index:000}",
            Start: 101,
            Step: 1,
            CreationSpecs:
            [
                new LayoutCreationSpec(
                    1,
                    new PaperRecipe(594, 420, "Millimeters"),
                    BuiltInLayoutKind.SingleDetail,
                    BuiltInTitleBlock: BuiltInTitleBlockKind.RightSidebar),
            ],
                        ProjectInfo: project), snapshot);

        Assert.True(plan.CanApply);
        var change = Assert.IsType<CreateSheetFromTemplateChange>(Assert.Single(plan.Changes));
        Assert.Equal("3", change.SheetNumber);
        Assert.Equal("Civic Library", change.ProjectInfo!.ProjectName);
        Assert.Equal(BuiltInTitleBlockKind.RightSidebar, change.Template.TitleBlock!.BuiltInKind);
        var geometry = AdaptiveTitleBlockLayoutSolver.Solve(
            BuiltInTitleBlockKind.RightSidebar,
            change.Template.Paper);
        var detail = Assert.Single(change.Template.DetailSlots);
        Assert.True(detail.Bottom >= geometry.Content.Bottom);
        Assert.True(detail.Top <= geometry.Content.Top);
        Assert.True(detail.Right <= geometry.Content.Right);
    }

    [Fact]
    public void DetailEnvelopeFitsReplacementTitleBlockContentWithoutDoubleMargins()
    {
        var paper = new PaperRecipe(594, 420, "Millimeters");
        var sourceLayout = AdaptiveTitleBlockLayoutSolver.Solve(
            BuiltInTitleBlockKind.RightSidebar,
            paper);
        var sourceContent = sourceLayout.Content;
        var sourceDetail = new DetailSlotRecipe(
            Id: Guid.NewGuid(),
            Name: "Plan",
            Left: sourceContent.Left + 8,
            Bottom: sourceContent.Bottom + 6,
            Right: sourceContent.Right - 10,
            Top: sourceContent.Top - 12,
            Projection: "Top",
            PageToModelRatio: null,
            ProjectionLocked: false,
            DisplayModeId: null,
            DefaultNamedView: null);
        var template = Template("Managed source", paper.Width, paper.Height) with
        {
            DetailSlots = [sourceDetail],
            TitleBlock = new TitleBlockTemplateRecipe(
                BuiltInTitleBlockKind.RightSidebar),
        };
        var snapshot = WithTemplates(TestSnapshots.Create(), [template]);
        var targetLayout = AdaptiveTitleBlockLayoutSolver.Solve(
            BuiltInTitleBlockKind.FullWidthBottom,
            paper);
        var targetContent = targetLayout.Content;

        var plan = new BatchCreateSheetsPlanner().Plan(new BatchCreateSheetsRequest(
            DocumentRuntimeSerialNumber: 42,
            SourceRevision: 1,
            DestinationFolderId: TestSnapshots.RootFolderId,
            NamingPattern: "Page {index}",
            Start: 1,
            Step: 1,
            CreationSpecs:
            [
                new LayoutCreationSpec(
                    1,
                    paper,
                    TemplateId: template.Id,
                    BuiltInTitleBlock: BuiltInTitleBlockKind.FullWidthBottom),
            ]), snapshot);

        Assert.True(plan.CanApply);
        var detail = Assert.Single(Assert.IsType<CreateSheetFromTemplateChange>(
            Assert.Single(plan.Changes)).Template.DetailSlots);
        Assert.Equal(targetContent.Left, detail.Left, 3);
        Assert.Equal(targetContent.Bottom, detail.Bottom, 3);
        Assert.Equal(targetContent.Right, detail.Right, 3);
        Assert.Equal(targetContent.Top, detail.Top, 3);
    }

    [Fact]
    public void BuiltInGridSharesTheTitleBlockPageMargins()
    {
        var paper = new PaperRecipe(594, 420, "Millimeters");
        var snapshot = WithTemplates(TestSnapshots.Create(), []);
        var plan = new BatchCreateSheetsPlanner().Plan(new BatchCreateSheetsRequest(
            DocumentRuntimeSerialNumber: 42,
            SourceRevision: 1,
            DestinationFolderId: TestSnapshots.RootFolderId,
            NamingPattern: "Page {index}",
            Start: 1,
            Step: 1,
            CreationSpecs:
            [
                new LayoutCreationSpec(
                    1,
                    paper,
                    BuiltInLayoutKind.FourDetailsGrid,
                    BuiltInTitleBlock: BuiltInTitleBlockKind.RightSidebar),
            ]), snapshot);

        Assert.True(plan.CanApply);
        var details = Assert.IsType<CreateSheetFromTemplateChange>(Assert.Single(plan.Changes))
            .Template.DetailSlots;
        var content = AdaptiveTitleBlockLayoutSolver.Solve(
            BuiltInTitleBlockKind.RightSidebar,
            paper).Content;
        Assert.Equal(content.Left, details.Min(detail => detail.Left), 3);
        Assert.Equal(content.Bottom, details.Min(detail => detail.Bottom), 3);
        Assert.Equal(content.Right, details.Max(detail => detail.Right), 3);
        Assert.Equal(content.Top, details.Max(detail => detail.Top), 3);
    }

    private static SheetTemplateRecipe Template(string name, double width, double height) => new(
        Guid.NewGuid(), name,
        new PaperRecipe(width, height, "Millimeters"),
        [new DetailSlotRecipe(Id: Guid.NewGuid(), Name: "Plan", Left: 10, Bottom: 10, Right: width - 10, Top: height - 20,
            Projection:             "Top", PageToModelRatio: 0.02, ProjectionLocked: true, DisplayModeId: null, DefaultNamedView: null)],
        null, new Dictionary<string, string>(), "{index:000}");

    private static DocumentSnapshot WithTemplates(
        DocumentSnapshot snapshot,
        IReadOnlyList<SheetTemplateRecipe> templates) => snapshot with
        {
            Templates = templates,
            Metadata = new Dictionary<string, string> { ["project"] = "Foundry" },
            NamedViews = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        };
}

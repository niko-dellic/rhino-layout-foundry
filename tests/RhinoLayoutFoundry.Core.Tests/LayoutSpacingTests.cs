using System.Text.Json.Nodes;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Persistence;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class LayoutSpacingTests
{
    public static IEnumerable<object?[]> Layouts()
    {
        foreach (var kind in new[] { BuiltInLayoutKind.SingleDetail, BuiltInLayoutKind.TwoDetailsHorizontal,
                     BuiltInLayoutKind.TwoDetailsVertical, BuiltInLayoutKind.FourDetailsGrid })
        foreach (var title in new BuiltInTitleBlockKind?[] { null, BuiltInTitleBlockKind.RightSidebar,
                     BuiltInTitleBlockKind.FullWidthBottom })
        foreach (var unit in new[] { "Millimeters", "Inches" })
            yield return [kind, title, unit];
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void GeneratedLayoutsHaveExactPhysicalEdgesAndGaps(
        BuiltInLayoutKind kind, BuiltInTitleBlockKind? title, string unit)
    {
        var scale = unit == "Inches" ? 1 / 25.4 : 1;
        var paper = new PaperRecipe(594 * scale, 420 * scale, unit);
        var spacing = new LayoutSpacing(10, 7, 13, 18, "Millimeters");
        var template = Create(new(1, paper, kind, BuiltInTitleBlock: title, Spacing: spacing));
        var details = template.DetailSlots;
        Assert.Equal(10 * scale, details.Min(d => d.Left), 8);
        Assert.Equal((420 - 10) * scale, details.Max(d => d.Top), 8);
        if (title is { } titleKind)
        {
            Assert.Equal(spacing.InUnits(unit), template.TitleBlock!.Spacing);
            var layout = AdaptiveTitleBlockLayoutSolver.Solve(titleKind, paper, ProjectInformation.Empty,
                details.Count, template.TitleBlock.Spacing);
            Assert.Equal(18 * scale, layout.Block.Bottom, 8);
            Assert.Equal(18 * scale, layout.Margin, 8);
            if (titleKind == BuiltInTitleBlockKind.RightSidebar)
            {
                Assert.Equal((594 - 18) * scale, layout.Block.Right, 8);
                Assert.Equal(13 * scale, layout.Block.Left - details.Max(d => d.Right), 8);
                Assert.Equal(10 * scale, details.Min(d => d.Bottom), 8);
            }
            else
            {
                Assert.Equal(18 * scale, layout.Block.Left, 8);
                Assert.Equal(13 * scale, details.Min(d => d.Bottom) - layout.Block.Top, 8);
                Assert.Equal((594 - 10) * scale, details.Max(d => d.Right), 8);
            }
        }
        else
        {
            Assert.Equal((594 - 10) * scale, details.Max(d => d.Right), 8);
            Assert.Equal(10 * scale, details.Min(d => d.Bottom), 8);
        }
        if (kind is BuiltInLayoutKind.TwoDetailsVertical or BuiltInLayoutKind.FourDetailsGrid)
            Assert.Equal(7 * scale, details[1].Left - details[0].Right, 8);
        if (kind == BuiltInLayoutKind.TwoDetailsHorizontal)
            Assert.Equal(7 * scale, details[0].Bottom - details[1].Top, 8);
        if (kind == BuiltInLayoutKind.FourDetailsGrid)
            Assert.Equal(7 * scale, details[0].Bottom - details[2].Top, 8);
    }

    [Theory]
    [InlineData("Millimeters", 10)]
    [InlineData("Centimeters", 1)]
    [InlineData("Meters", 0.01)]
    [InlineData("Inches", 0.25)]
    [InlineData("Feet", 0.0208333333333333)]
    public void DefaultsAreUniformInPaperUnits(string unit, double expected)
    {
        var spacing = LayoutSpacing.Default(unit);
        Assert.Equal(expected, spacing.PageEdge, 10);
        Assert.Equal(spacing.PageEdge, spacing.DetailGap);
        Assert.Equal(spacing.PageEdge, spacing.TitleBlockGap);
        Assert.Equal(spacing.PageEdge, spacing.TitleBlockEdge);
        Assert.Equal(unit, spacing.UnitSystem);
    }

    [Fact]
    public void UnitChangesPreservePhysicalDistances()
    {
        var spacing = new LayoutSpacing(10, 3, 12, 5, "Millimeters");
        var roundTrip = spacing.InUnits("Inches").InUnits("Feet").InUnits("Meters").InUnits("Millimeters");
        Assert.Equal(spacing.PageEdge, roundTrip.PageEdge, 10);
        Assert.Equal(spacing.DetailGap, roundTrip.DetailGap, 10);
        Assert.Equal(spacing.TitleBlockGap, roundTrip.TitleBlockGap, 10);
        Assert.Equal(spacing.TitleBlockEdge, roundTrip.TitleBlockEdge, 10);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidValuesInEveryFieldBlockTheWholeBatch(double value)
    {
        var normal = LayoutSpacing.Default("Millimeters");
        foreach (var spacing in new[] { normal with { PageEdge = value }, normal with { DetailGap = value },
                     normal with { TitleBlockGap = value }, normal with { TitleBlockEdge = value } })
        {
            var plan = Plan([new(1, Paper), new(1, Paper, Spacing: spacing)]);
            Assert.False(plan.CanApply);
            Assert.Empty(plan.Changes);
            Assert.Contains(plan.Diagnostics, d => d.Code == "batch.spacing_invalid");
        }
    }

    [Fact]
    public void ZeroSpacingPreservesHorizontalBleedButReservesCaptionSpace()
    {
        var template = Create(new(1, Paper, BuiltInLayoutKind.FourDetailsGrid,
            Spacing: LayoutSpacing.Uniform(0, "Millimeters")));
        Assert.Equal(0, template.DetailSlots[0].Left);
        Assert.Equal(Paper.Height, template.DetailSlots[0].Top);
        Assert.Equal(template.DetailSlots[0].Right, template.DetailSlots[1].Left);
        Assert.Equal(6, template.DetailSlots[0].Bottom - template.DetailSlots[2].Top, 8);
        Assert.Equal(6, template.DetailSlots.Min(d => d.Bottom), 8);
    }

    [Fact]
    public void ExcessiveEdgesGapsAndTitleBlockClearanceBlockCreation()
    {
        var normal = LayoutSpacing.Default("Millimeters");
        foreach (var spacing in new[] { normal with { PageEdge = 300 }, normal with { DetailGap = 600 },
                     normal with { TitleBlockGap = 600 }, normal with { TitleBlockEdge = 300 } })
        {
            var plan = Plan([new(1, Paper, BuiltInLayoutKind.FourDetailsGrid,
                BuiltInTitleBlock: BuiltInTitleBlockKind.RightSidebar, Spacing: spacing)]);
            Assert.False(plan.CanApply);
            Assert.Empty(plan.Changes);
        }
    }

    [Fact]
    public void LegacyCallersKeepPercentageSpacing()
    {
        var template = Create(new(1, Paper, BuiltInLayoutKind.FourDetailsGrid));
        Assert.Equal(10.5, template.DetailSlots[0].Left, 8);
        Assert.Equal(11.88, template.DetailSlots[1].Left - template.DetailSlots[0].Right, 8);
        Assert.Equal(8.4, template.DetailSlots[0].Bottom - template.DetailSlots[2].Top, 8);
    }

    [Fact]
    public void SavedTemplatesIgnoreGeneratedSpacingOverrides()
    {
        var saved = Create(new(1, Paper, BuiltInLayoutKind.FourDetailsGrid));
        var snapshot = TestSnapshots.Create() with { Templates = new[] { saved } };
        var plan = Plan([new(1, Paper, TemplateId: saved.Id,
            Spacing: LayoutSpacing.Uniform(1000, "Millimeters"))], snapshot);
        Assert.True(plan.CanApply);
        Assert.Equal(saved.DetailSlots, Assert.Single(plan.Changes.OfType<CreateSheetFromTemplateChange>()).Template.DetailSlots);
    }

    [Fact]
    public void MixedBatchUsesEachSpecificationsSpacingAndUnits()
    {
        var plan = Plan([new(1, Paper, Spacing: LayoutSpacing.Default("Millimeters")),
            new(1, new(22, 17, "Inches"), Spacing: LayoutSpacing.Default("Inches"))]);
        Assert.True(plan.CanApply);
        var changes = plan.Changes.OfType<CreateSheetFromTemplateChange>().ToArray();
        Assert.Equal(10, changes[0].Template.DetailSlots[0].Left);
        Assert.Equal(0.25, changes[1].Template.DetailSlots[0].Left, 10);
    }

    [Fact]
    public void SpacingRoundTripsAndRegenerationRetainsGeometry()
    {
        var state = DocumentState.Empty();
        var id = Guid.NewGuid();
        var spacing = new LayoutSpacing(10, 7, 13, 18, "Millimeters");
        state = state with { Sheets = new Dictionary<Guid, SheetRecord>
        {
            [id] = new(id, state.RootFolderId, 0, new Dictionary<string, string>(),
                new(Guid.NewGuid(), Guid.NewGuid(), BuiltInTitleBlockKind.RightSidebar, spacing)),
        }};
        var payload = DocumentStateSerializer.Serialize(state);
        var restored = DocumentStateSerializer.Deserialize(payload);
        var role = restored.Sheets[id].TitleBlock!;
        Assert.Equal(spacing, role.Spacing);
        var before = AdaptiveTitleBlockLayoutSolver.Solve(role.BuiltInKind, Paper, state.ProjectInfo, 4, spacing);
        var after = AdaptiveTitleBlockLayoutSolver.Solve(role.BuiltInKind, Paper,
            state.ProjectInfo with { ProjectName = "Renamed project" }, 4, role.Spacing);
        Assert.Equal(before.Block, after.Block);
        Assert.Equal(before.Content, after.Content);
        Assert.Equal(before.Signature, after.Signature);
        var json = JsonNode.Parse(payload)!;
        json["Sheets"]![id.ToString()]!["TitleBlock"]!.AsObject().Remove("Spacing");
        Assert.Null(DocumentStateSerializer.Deserialize(json.ToJsonString()).Sheets[id].TitleBlock!.Spacing);
    }

    [Fact]
    public void GeometrySignaturesIncludeExactSpacingWhileInternalPaddingStaysUnchanged()
    {
        var spacing = LayoutSpacing.Default("Millimeters");
        var first = AdaptiveTitleBlockLayoutSolver.Solve(BuiltInTitleBlockKind.RightSidebar, Paper,
            ProjectInformation.Empty, 4, spacing);
        var next = AdaptiveTitleBlockLayoutSolver.Solve(BuiltInTitleBlockKind.RightSidebar, Paper,
            ProjectInformation.Empty, 4, spacing with { TitleBlockGap = 20 });
        Assert.NotEqual(first.Signature, next.Signature);
        Assert.Equal(first.Block, next.Block);
        Assert.Equal(first.Fields, next.Fields);
        Assert.Equal(first.BodyTextHeight, next.BodyTextHeight);
    }

    [Theory]
    [InlineData(BuiltInTitleBlockKind.RightSidebar)]
    [InlineData(BuiltInTitleBlockKind.FullWidthBottom)]
    public void ZeroSpacingWorksWithTitleBlocks(BuiltInTitleBlockKind kind)
    {
        var spacing = LayoutSpacing.Uniform(0, "Millimeters");
        var template = Create(new(1, Paper, BuiltInLayoutKind.FourDetailsGrid,
            BuiltInTitleBlock: kind, Spacing: spacing));
        var block = AdaptiveTitleBlockLayoutSolver.Solve(kind, Paper, ProjectInformation.Empty, 4, spacing);
        Assert.Equal(0, block.Margin);
        Assert.Equal(template.DetailSlots[0].Right, template.DetailSlots[1].Left);
        if (kind == BuiltInTitleBlockKind.RightSidebar)
            Assert.Equal(block.Block.Left, template.DetailSlots.Max(d => d.Right));
        else
            Assert.Equal(6, template.DetailSlots.Min(d => d.Bottom) - block.Block.Top, 8);
    }

    [Theory]
    [InlineData("Millimeters", -1)]
    [InlineData("Unsupported", 10)]
    public void InvalidPersistedSpacingIsRejected(string units, double margin)
    {
        var state = DocumentState.Empty();
        var id = Guid.NewGuid();
        state = state with { Sheets = new Dictionary<Guid, SheetRecord>
        {
            [id] = new(id, state.RootFolderId, 0, new Dictionary<string, string>(),
                new(Guid.NewGuid(), Guid.NewGuid(), BuiltInTitleBlockKind.RightSidebar,
                    LayoutSpacing.Uniform(margin, units))),
        }};
        Assert.Throws<System.Text.Json.JsonException>(() => DocumentStateSerializer.Serialize(state));
    }

    [Fact]
    public void ResizeValidationUsesPersistedCustomSpacing()
    {
        var snapshot = TestSnapshots.Create();
        var sheet = snapshot.Sheets.Values.First();
        snapshot = snapshot with { Sheets = new Dictionary<Guid, SheetSnapshot>(snapshot.Sheets)
        {
            [sheet.PageViewId] = sheet with
            {
                TitleBlockBuiltInKind = BuiltInTitleBlockKind.RightSidebar,
                TitleBlockSpacing = LayoutSpacing.Uniform(200, "Millimeters"),
            },
        }};
        var plan = new BatchUpdateSheetsPlanner().Plan(new(
            42, 1, new[] { sheet.PageViewId }, NamingPattern: null, Start: 1, Step: 1,
            PaperWidth: 297, PaperHeight: 210, PaperUnitSystem: "Millimeters", DetailDisplayModeId: null), snapshot);
        Assert.False(plan.CanApply);
    }

    private static readonly PaperRecipe Paper = new(594, 420, "Millimeters");
    private static OperationPlan Plan(IReadOnlyList<LayoutCreationSpec> specs, DocumentSnapshot? snapshot = null) =>
        new BatchCreateSheetsPlanner().Plan(new(42, 1, TestSnapshots.RootFolderId, specs, "Spacing {index}", 1, 1),
            snapshot ?? TestSnapshots.Create());

    private static SheetTemplateRecipe Create(LayoutCreationSpec spec)
    {
        var plan = Plan([spec]);
        Assert.True(plan.CanApply, string.Join(" ", plan.Diagnostics.Select(d => d.Message)));
        return Assert.Single(plan.Changes.OfType<CreateSheetFromTemplateChange>()).Template;
    }
}

using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class AdaptiveTitleBlockLayoutSolverTests
{
    public static TheoryData<PaperRecipe> Papers => new()
    {
        new PaperRecipe(1189, 841, "Millimeters"),
        new PaperRecipe(841, 594, "Millimeters"),
        new PaperRecipe(594, 420, "Millimeters"),
        new PaperRecipe(420, 297, "Millimeters"),
        new PaperRecipe(297, 210, "Millimeters"),
        new PaperRecipe(34, 22, "Inches"),
        new PaperRecipe(22, 17, "Inches"),
        new PaperRecipe(17, 11, "Inches"),
        new PaperRecipe(11, 8.5, "Inches"),
    };

    [Theory]
    [MemberData(nameof(Papers))]
    public void EveryFamilyFitsPageAndReservesDrawingArea(PaperRecipe paper)
    {
        foreach (var kind in Enum.GetValues<BuiltInTitleBlockKind>())
        {
            var layout = AdaptiveTitleBlockLayoutSolver.Solve(kind, paper);

            Assert.True(layout.Block.Left >= 0);
            Assert.True(layout.Block.Bottom >= 0);
            Assert.True(layout.Block.Right <= paper.Width + 0.001);
            Assert.True(layout.Block.Top <= paper.Height + 0.001);
            Assert.True(layout.Content.Width > 0);
            Assert.True(layout.Content.Height > 0);
            Assert.False(Intersects(layout.Block, layout.Content));
            Assert.Equal(0, layout.VisibleRevisionRows);
            Assert.All(layout.Fields, field => Assert.True(Contains(layout.Block, field.Bounds)));
            for (var first = 0; first < layout.Fields.Count; first++)
            for (var second = first + 1; second < layout.Fields.Count; second++)
                Assert.False(Intersects(layout.Fields[first].Bounds, layout.Fields[second].Bounds),
                    $"{kind}: {layout.Fields[first].Key} overlaps {layout.Fields[second].Key}");
        }
    }

    [Fact]
    public void MetricAndImperialEquivalentPapersProduceEquivalentPhysicalGeometry()
    {
        var metric = AdaptiveTitleBlockLayoutSolver.Solve(
            BuiltInTitleBlockKind.CompactLowerRight,
            new PaperRecipe(431.8, 279.4, "Millimeters"));
        var imperial = AdaptiveTitleBlockLayoutSolver.Solve(
            BuiltInTitleBlockKind.CompactLowerRight,
            new PaperRecipe(17, 11, "Inches"));

        Assert.Equal(metric.Block.Width, imperial.Block.Width * 25.4, 5);
        Assert.Equal(metric.Block.Height, imperial.Block.Height * 25.4, 5);
        Assert.Equal(metric.BodyTextHeight, imperial.BodyTextHeight * 25.4, 5);
    }

    [Fact]
    public void SignatureIsDeterministicAndFamilySpecific()
    {
        var paper = new PaperRecipe(594, 420, "Millimeters");
        var first = AdaptiveTitleBlockLayoutSolver.Solve(BuiltInTitleBlockKind.FullWidthBottom, paper);
        var second = AdaptiveTitleBlockLayoutSolver.Solve(BuiltInTitleBlockKind.FullWidthBottom, paper);
        var other = AdaptiveTitleBlockLayoutSolver.Solve(BuiltInTitleBlockKind.RightSidebar, paper);

        Assert.Equal(first.Signature, second.Signature);
        Assert.NotEqual(first.Signature, other.Signature);
    }

    [Fact]
    public void TinyCustomPaperIsRejectedInsteadOfOverlapping()
    {
        Assert.Throws<InvalidOperationException>(() => AdaptiveTitleBlockLayoutSolver.Solve(
            BuiltInTitleBlockKind.RightSidebar,
            new PaperRecipe(40, 40, "Millimeters")));
    }

    [Theory]
    [InlineData(BuiltInTitleBlockKind.RightSidebar)]
    [InlineData(BuiltInTitleBlockKind.FullWidthBottom)]
    public void VisibilityControlsProjectFieldsButNotSheetIdentity(BuiltInTitleBlockKind kind)
    {
        var project = ProjectInformation.Empty with
        {
            ProjectName = string.Empty,
            FirmName = "Hidden firm",
            TitleBlockOptions = new TitleBlockContentOptions(
                [TitleBlockContentField.ProjectName], [], false),
        };

        var layout = AdaptiveTitleBlockLayoutSolver.Solve(
            kind, new PaperRecipe(594, 420, "Millimeters"), project);

        Assert.Contains(layout.Fields, field => field.Key == "project.name");
        Assert.DoesNotContain(layout.Fields, field => field.Key == "firm.name");
        Assert.Contains(layout.Fields, field => field.Key == "sheet.title");
        Assert.Contains(layout.Fields, field => field.Key == "sheet.number");
        Assert.Contains(layout.Fields, field => field.Key == "sheet.scale");
    }

    [Theory]
    [InlineData(BuiltInTitleBlockKind.RightSidebar)]
    [InlineData(BuiltInTitleBlockKind.FullWidthBottom)]
    public void RevisionOptionCreatesOnlyABlankBay(BuiltInTitleBlockKind kind)
    {
        var project = ProjectInformation.Empty with
        {
            TitleBlockOptions = new TitleBlockContentOptions([], [], true),
            DefaultRevision = new SheetRevisionRecord("P01", "2026-08-31", "Permit", "ND", "QA"),
        };

        var layout = AdaptiveTitleBlockLayoutSolver.Solve(
            kind, new PaperRecipe(594, 420, "Millimeters"), project);

        Assert.NotNull(layout.RevisionRegion);
        Assert.DoesNotContain(layout.Fields, field => field.Key.StartsWith("revision.", StringComparison.Ordinal));
    }

    [Fact]
    public void ContentChangesAffectDefinitionSignatureAndGrowTheBlock()
    {
        var custom = Enumerable.Range(1, 20).ToDictionary(index => $"Custom {index}", _ => "Value");
        var expanded = ProjectInformation.Empty with
        {
            CustomFields = custom,
            TitleBlockOptions = new TitleBlockContentOptions(
                [], custom.Keys.Select(key => new CustomTitleBlockFieldOption(key)).ToArray(), false),
        };
        var compact = ProjectInformation.Empty with
        {
            TitleBlockOptions = new TitleBlockContentOptions([], [], false),
        };
        var paper = new PaperRecipe(297, 210, "Millimeters");

        var compactRight = AdaptiveTitleBlockLayoutSolver.Solve(BuiltInTitleBlockKind.RightSidebar, paper, compact);
        var expandedRight = AdaptiveTitleBlockLayoutSolver.Solve(BuiltInTitleBlockKind.RightSidebar, paper, expanded);
        var compactBottom = AdaptiveTitleBlockLayoutSolver.Solve(BuiltInTitleBlockKind.FullWidthBottom, paper, compact);
        var expandedBottom = AdaptiveTitleBlockLayoutSolver.Solve(BuiltInTitleBlockKind.FullWidthBottom, paper, expanded);

        Assert.True(expandedRight.Block.Width > compactRight.Block.Width);
        Assert.True(expandedBottom.Block.Height >= compactBottom.Block.Height);
        Assert.NotEqual(compactRight.Signature, expandedRight.Signature);
        Assert.NotEqual(compactBottom.Signature, expandedBottom.Signature);
    }

    private static bool Intersects(TitleBlockRectangle left, TitleBlockRectangle right) =>
        left.Left < right.Right - 0.001 && left.Right > right.Left + 0.001 &&
        left.Bottom < right.Top - 0.001 && left.Top > right.Bottom + 0.001;

    private static bool Contains(TitleBlockRectangle outer, TitleBlockRectangle inner) =>
        inner.Left >= outer.Left - 0.001 && inner.Right <= outer.Right + 0.001 &&
        inner.Bottom >= outer.Bottom - 0.001 && inner.Top <= outer.Top + 0.001;
}

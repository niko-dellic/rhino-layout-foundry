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
            Assert.InRange(layout.VisibleRevisionRows, 1, 6);
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

    private static bool Intersects(TitleBlockRectangle left, TitleBlockRectangle right) =>
        left.Left < right.Right && left.Right > right.Left &&
        left.Bottom < right.Top && left.Top > right.Bottom;
}

using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class ExistingSheetLayoutClassifierTests
{
    [Fact]
    public void SideBySideDetailsAreVerticalLayout()
    {
        var details = new[]
        {
            Detail(new DetailPageBounds(10, 10, 145, 287)),
            Detail(new DetailPageBounds(155, 10, 290, 287)),
        };

        Assert.Equal(
            BuiltInLayoutKind.TwoDetailsVertical,
            ExistingSheetLayoutClassifier.Classify(details));
    }

    [Fact]
    public void StackedDetailsAreHorizontalLayout()
    {
        var details = new[]
        {
            Detail(new DetailPageBounds(10, 155, 290, 287)),
            Detail(new DetailPageBounds(10, 10, 290, 145)),
        };

        Assert.Equal(
            BuiltInLayoutKind.TwoDetailsHorizontal,
            ExistingSheetLayoutClassifier.Classify(details));
    }

    [Fact]
    public void TwoDetailsWithoutPageBoundsAreNotMisclassified()
    {
        Assert.Null(ExistingSheetLayoutClassifier.Classify([Detail(), Detail()]));
    }

    [Fact]
    public void SideBySideDetailsAreOrderedLeftToRightIndependentlyOfRhinoEnumeration()
    {
        var right = Detail(new DetailPageBounds(155, 10, 290, 287));
        var left = Detail(new DetailPageBounds(10, 10, 145, 287));

        var ordered = ExistingSheetLayoutClassifier.OrderForLayout(
            [right, left],
            BuiltInLayoutKind.TwoDetailsVertical);

        Assert.Equal(left.DetailViewportId, ordered[0].DetailViewportId);
        Assert.Equal(right.DetailViewportId, ordered[1].DetailViewportId);
    }

    [Fact]
    public void StackedDetailsAreOrderedTopToBottomIndependentlyOfRhinoEnumeration()
    {
        var bottom = Detail(new DetailPageBounds(10, 10, 290, 145));
        var top = Detail(new DetailPageBounds(10, 155, 290, 287));

        var ordered = ExistingSheetLayoutClassifier.OrderForLayout(
            [bottom, top],
            BuiltInLayoutKind.TwoDetailsHorizontal);

        Assert.Equal(top.DetailViewportId, ordered[0].DetailViewportId);
        Assert.Equal(bottom.DetailViewportId, ordered[1].DetailViewportId);
    }

    private static DetailSnapshot Detail(DetailPageBounds? bounds = null) =>
        new(Guid.NewGuid(), "Detail", Guid.NewGuid(), "Wireframe", PageBounds: bounds);
}

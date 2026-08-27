using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class ThumbnailGridLayoutTests
{
    [Fact]
    public void LargerRequestedCardsProduceFewerColumns()
    {
        var compact = ThumbnailGridLayout.Create(40, 1200, 140);
        var large = ThumbnailGridLayout.Create(40, 1200, 300);

        Assert.True(compact.Columns > large.Columns);
        Assert.Equal(40, compact.ItemCount);
        Assert.Equal(40, large.ItemCount);
    }

    [Fact]
    public void GridFillsAvailableWidthWithoutOverlappingCells()
    {
        var layout = ThumbnailGridLayout.Create(12, 1000, 210);
        var first = layout.CellBounds(0);
        var second = layout.CellBounds(1);

        Assert.True(second.X >= first.X + first.Width + layout.Gap - 0.001);
        var lastColumn = layout.CellBounds(layout.Columns - 1);
        Assert.True(lastColumn.X + lastColumn.Width <= 1000 - layout.Padding + 0.001);
    }

    [Fact]
    public void VisibleQueryReturnsOnlyViewportRowsAndOverscan()
    {
        var layout = ThumbnailGridLayout.Create(100, 900, 180);
        var visible = layout.VisibleIndices(
            layout.RowHeight * 4,
            layout.RowHeight * 5,
            overscanRows: 1);

        Assert.True(visible.Count < layout.ItemCount);
        Assert.True(visible.All(index => index >= layout.Columns * 2));
        Assert.True(visible.All(index => index < layout.Columns * 7));
        Assert.Empty(layout.VisibleIndices(layout.ContentHeight + 100, layout.ContentHeight + 300));
    }
}

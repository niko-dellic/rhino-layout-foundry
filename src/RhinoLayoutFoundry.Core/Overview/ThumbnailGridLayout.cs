using SharedLayout = RhinoFoundry.UI.Primitives.FoundryThumbnailGridLayout;

namespace RhinoLayoutFoundry.Core.Overview;

public readonly record struct ThumbnailGridRect(double X, double Y, double Width, double Height)
{
    public double Bottom => Y + Height;
}

/// <summary>
/// Deterministic geometry for the page-thumbnail view. The UI draws a single
/// virtual surface and asks this type which rows intersect the scroll viewport.
/// </summary>
public sealed record ThumbnailGridLayout(
    int ItemCount,
    int Columns,
    int Rows,
    double CardWidth,
    double ImageAreaHeight,
    double RowHeight,
    double ContentHeight,
    double Padding,
    double Gap)
{
    public IReadOnlyList<double> RowTops { get; init; } = [];
    public IReadOnlyList<double> RowImageAreaHeights { get; init; } = [];
    public IReadOnlyList<double> RowHeights { get; init; } = [];
    private RhinoFoundry.UI.Primitives.FoundryThumbnailGridLayout Shared() =>
        new(ItemCount, Columns, Rows, CardWidth, ImageAreaHeight, RowHeight, ContentHeight, Padding, Gap)
        { RowTops = RowTops, RowImageAreaHeights = RowImageAreaHeights, RowHeights = RowHeights };
    private static ThumbnailGridLayout FromShared(RhinoFoundry.UI.Primitives.FoundryThumbnailGridLayout value) =>
        new(value.ItemCount, value.Columns, value.Rows, value.CardWidth, value.ImageAreaHeight, value.RowHeight, value.ContentHeight, value.Padding, value.Gap)
        { RowTops = value.RowTops, RowImageAreaHeights = value.RowImageAreaHeights, RowHeights = value.RowHeights };
    public static ThumbnailGridLayout Create(int itemCount, double availableWidth, double requestedCardWidth, double padding = 16, double gap = 16) =>
        FromShared(SharedLayout.Create(itemCount, availableWidth, requestedCardWidth, padding, gap));
    public static ThumbnailGridLayout CreateForDensity(int itemCount, double availableWidth, double density, double padding = 16, double gap = 16) =>
        FromShared(SharedLayout.CreateForDensity(itemCount, availableWidth, density, padding, gap));
    public static ThumbnailGridLayout CreateForDensity(IReadOnlyList<double> paperHeightToWidthRatios, double availableWidth, double density, double padding = 16, double gap = 16) =>
        FromShared(SharedLayout.CreateForDensity(paperHeightToWidthRatios, availableWidth, density, padding, gap));
    public static int MaximumColumns(int itemCount, double availableWidth, double padding = 16, double gap = 16) =>
        SharedLayout.MaximumColumns(itemCount, availableWidth, padding, gap);
    public ThumbnailGridRect CellBounds(int index)
    {
        var rect = Shared().CellBounds(index);
        return new(rect.X, rect.Y, rect.Width, rect.Height);
    }
    public double ImageAreaHeightForIndex(int index) => Shared().ImageAreaHeightForIndex(index);
    public int? RowAt(double y) => Shared().RowAt(y);
    public IReadOnlyList<int> VisibleIndices(double visibleTop, double visibleBottom, int overscanRows = 1) =>
        Shared().VisibleIndices(visibleTop, visibleBottom, overscanRows);
}

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
    public static ThumbnailGridLayout Create(
        int itemCount,
        double availableWidth,
        double requestedCardWidth,
        double padding = 16,
        double gap = 16)
    {
        itemCount = Math.Max(0, itemCount);
        availableWidth = Math.Max(1, availableWidth);
        requestedCardWidth = Math.Clamp(requestedCardWidth, 104, 420);
        var usableWidth = Math.Max(1, availableWidth - padding * 2);
        var columns = Math.Max(1, (int)Math.Floor((usableWidth + gap) / (requestedCardWidth + gap)));
        columns = Math.Min(Math.Max(1, itemCount), columns);
        var cardWidth = Math.Max(1, (usableWidth - Math.Max(0, columns - 1) * gap) / columns);
        var imageAreaHeight = cardWidth * 0.78;
        var rowHeight = imageAreaHeight + 42 + gap;
        var rows = itemCount == 0 ? 0 : (int)Math.Ceiling(itemCount / (double)columns);
        var contentHeight = rows == 0 ? padding * 2 : padding * 2 + rows * rowHeight - gap;
        return new ThumbnailGridLayout(
            itemCount,
            columns,
            rows,
            cardWidth,
            imageAreaHeight,
            rowHeight,
            contentHeight,
            padding,
            gap);
    }

    public ThumbnailGridRect CellBounds(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (index >= ItemCount) throw new ArgumentOutOfRangeException(nameof(index));
        var row = index / Columns;
        var column = index % Columns;
        return new ThumbnailGridRect(
            Padding + column * (CardWidth + Gap),
            Padding + row * RowHeight,
            CardWidth,
            ImageAreaHeight + 42);
    }

    public IReadOnlyList<int> VisibleIndices(double visibleTop, double visibleBottom, int overscanRows = 1)
    {
        if (ItemCount == 0 || visibleBottom < visibleTop ||
            visibleBottom < Padding || visibleTop > ContentHeight) return [];
        overscanRows = Math.Max(0, overscanRows);
        var firstRow = Math.Clamp(
            (int)Math.Floor((visibleTop - Padding) / RowHeight) - overscanRows,
            0,
            Math.Max(0, Rows - 1));
        var lastRow = Math.Clamp(
            (int)Math.Floor((visibleBottom - Padding) / RowHeight) + overscanRows,
            0,
            Math.Max(0, Rows - 1));
        var firstIndex = firstRow * Columns;
        var lastIndex = Math.Min(ItemCount, (lastRow + 1) * Columns);
        return Enumerable.Range(firstIndex, Math.Max(0, lastIndex - firstIndex)).ToArray();
    }
}

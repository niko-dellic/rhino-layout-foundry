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
    private const double MinimumCardWidth = 104;

    public static ThumbnailGridLayout Create(
        int itemCount,
        double availableWidth,
        double requestedCardWidth,
        double padding = 16,
        double gap = 16)
    {
        itemCount = Math.Max(0, itemCount);
        availableWidth = Math.Max(1, availableWidth);
        requestedCardWidth = double.IsFinite(requestedCardWidth)
            ? Math.Max(MinimumCardWidth, requestedCardWidth)
            : MinimumCardWidth;
        var usableWidth = Math.Max(1, availableWidth - padding * 2);
        var columns = Math.Max(1, (int)Math.Floor((usableWidth + gap) / (requestedCardWidth + gap)));
        columns = Math.Min(Math.Max(1, itemCount), columns);
        return CreateWithColumns(itemCount, availableWidth, columns, padding, gap);
    }

    /// <summary>
    /// Maps a normalized size control to the useful column-count states instead
    /// of raw pixels. This prevents the one-column state from occupying most of
    /// the slider on wide viewports.
    /// </summary>
    public static ThumbnailGridLayout CreateForDensity(
        int itemCount,
        double availableWidth,
        double density,
        double padding = 16,
        double gap = 16)
    {
        itemCount = Math.Max(0, itemCount);
        availableWidth = Math.Max(1, availableWidth);
        density = double.IsFinite(density) ? Math.Clamp(density, 0, 1) : 0;
        var maximumColumns = MaximumColumns(itemCount, availableWidth, padding, gap);
        var columns = Math.Clamp(
            (int)Math.Round(
                maximumColumns - density * (maximumColumns - 1),
                MidpointRounding.AwayFromZero),
            1,
            maximumColumns);
        return CreateWithColumns(itemCount, availableWidth, columns, padding, gap);
    }

    public static int MaximumColumns(
        int itemCount,
        double availableWidth,
        double padding = 16,
        double gap = 16)
    {
        itemCount = Math.Max(0, itemCount);
        availableWidth = Math.Max(1, availableWidth);
        var usableWidth = Math.Max(1, availableWidth - padding * 2);
        var columns = Math.Max(
            1,
            (int)Math.Floor((usableWidth + gap) / (MinimumCardWidth + gap)));
        return Math.Min(Math.Max(1, itemCount), columns);
    }

    private static ThumbnailGridLayout CreateWithColumns(
        int itemCount,
        double availableWidth,
        int columns,
        double padding,
        double gap)
    {
        var usableWidth = Math.Max(1, availableWidth - padding * 2);
        columns = Math.Clamp(columns, 1, Math.Max(1, itemCount));
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

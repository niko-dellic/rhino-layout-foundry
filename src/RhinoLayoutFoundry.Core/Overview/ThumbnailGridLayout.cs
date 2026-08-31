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
    private const double MaximumImageAreaHeightRatio = 0.78;
    private const double PaperInset = 12;
    private const double MetadataHeight = 42;

    public IReadOnlyList<double> RowTops { get; init; } = [];
    public IReadOnlyList<double> RowImageAreaHeights { get; init; } = [];
    public IReadOnlyList<double> RowHeights { get; init; } = [];

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
        return CreateWithColumns(itemCount, availableWidth, columns, null, padding, gap);
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
        return CreateWithColumns(itemCount, availableWidth, columns, null, padding, gap);
    }

    public static ThumbnailGridLayout CreateForDensity(
        IReadOnlyList<double> paperHeightToWidthRatios,
        double availableWidth,
        double density,
        double padding = 16,
        double gap = 16)
    {
        ArgumentNullException.ThrowIfNull(paperHeightToWidthRatios);
        var itemCount = paperHeightToWidthRatios.Count;
        availableWidth = Math.Max(1, availableWidth);
        density = double.IsFinite(density) ? Math.Clamp(density, 0, 1) : 0;
        var maximumColumns = MaximumColumns(itemCount, availableWidth, padding, gap);
        var columns = Math.Clamp(
            (int)Math.Round(
                maximumColumns - density * (maximumColumns - 1),
                MidpointRounding.AwayFromZero),
            1,
            maximumColumns);
        return CreateWithColumns(
            itemCount,
            availableWidth,
            columns,
            paperHeightToWidthRatios,
            padding,
            gap);
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
        IReadOnlyList<double>? paperHeightToWidthRatios,
        double padding,
        double gap)
    {
        var usableWidth = Math.Max(1, availableWidth - padding * 2);
        columns = Math.Clamp(columns, 1, Math.Max(1, itemCount));
        var cardWidth = Math.Max(1, (usableWidth - Math.Max(0, columns - 1) * gap) / columns);
        var rows = itemCount == 0 ? 0 : (int)Math.Ceiling(itemCount / (double)columns);
        var maximumImageAreaHeight = cardWidth * MaximumImageAreaHeightRatio;
        var rowTops = new double[rows];
        var rowImageAreaHeights = new double[rows];
        var rowHeights = new double[rows];
        var nextTop = padding;
        for (var row = 0; row < rows; row++)
        {
            rowTops[row] = nextTop;
            var firstIndex = row * columns;
            var lastIndex = Math.Min(itemCount, firstIndex + columns);
            var renderedPaperHeight = 1d;
            for (var index = firstIndex; index < lastIndex; index++)
            {
                var ratio = PaperRatio(paperHeightToWidthRatios, index);
                var widthLimitedHeight = Math.Max(1, cardWidth - PaperInset) * ratio;
                renderedPaperHeight = Math.Max(
                    renderedPaperHeight,
                    Math.Min(widthLimitedHeight, Math.Max(1, maximumImageAreaHeight - PaperInset)));
            }

            rowImageAreaHeights[row] = Math.Min(
                maximumImageAreaHeight,
                renderedPaperHeight + PaperInset);
            rowHeights[row] = rowImageAreaHeights[row] + MetadataHeight + gap;
            nextTop += rowHeights[row];
        }

        var imageAreaHeight = rows == 0 ? maximumImageAreaHeight : rowImageAreaHeights.Max();
        var rowHeight = rows == 0 ? imageAreaHeight + MetadataHeight + gap : rowHeights.Max();
        var contentHeight = rows == 0 ? padding * 2 : nextTop + padding - gap;
        return new ThumbnailGridLayout(
            itemCount,
            columns,
            rows,
            cardWidth,
            imageAreaHeight,
            rowHeight,
            contentHeight,
            padding,
            gap)
        {
            RowTops = rowTops,
            RowImageAreaHeights = rowImageAreaHeights,
            RowHeights = rowHeights,
        };
    }

    private static double PaperRatio(IReadOnlyList<double>? ratios, int index)
    {
        if (ratios is null || index < 0 || index >= ratios.Count) return 1 / 1.414;
        var ratio = ratios[index];
        return double.IsFinite(ratio) && ratio > 0 ? ratio : 1 / 1.414;
    }

    public ThumbnailGridRect CellBounds(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (index >= ItemCount) throw new ArgumentOutOfRangeException(nameof(index));
        var row = index / Columns;
        var column = index % Columns;
        var imageAreaHeight = ImageAreaHeightForRow(row);
        return new ThumbnailGridRect(
            Padding + column * (CardWidth + Gap),
            RowTop(row),
            CardWidth,
            imageAreaHeight + MetadataHeight);
    }

    public double ImageAreaHeightForIndex(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (index >= ItemCount) throw new ArgumentOutOfRangeException(nameof(index));
        return ImageAreaHeightForRow(index / Columns);
    }

    public int? RowAt(double y)
    {
        if (!double.IsFinite(y) || Rows == 0 || y < Padding || y > ContentHeight) return null;
        for (var row = 0; row < Rows; row++)
        {
            if (y < RowTop(row) + HeightForRow(row)) return row;
        }

        return null;
    }

    public IReadOnlyList<int> VisibleIndices(double visibleTop, double visibleBottom, int overscanRows = 1)
    {
        if (ItemCount == 0 || visibleBottom < visibleTop ||
            visibleBottom < Padding || visibleTop > ContentHeight) return [];
        overscanRows = Math.Max(0, overscanRows);
        var firstVisibleRow = 0;
        while (firstVisibleRow < Rows &&
               RowTop(firstVisibleRow) + HeightForRow(firstVisibleRow) < visibleTop)
            firstVisibleRow++;
        if (firstVisibleRow >= Rows) return [];
        var lastVisibleRow = firstVisibleRow;
        while (lastVisibleRow + 1 < Rows && RowTop(lastVisibleRow + 1) <= visibleBottom)
            lastVisibleRow++;
        var firstRow = Math.Max(0, firstVisibleRow - overscanRows);
        var lastRow = Math.Min(Rows - 1, lastVisibleRow + overscanRows);
        var firstIndex = firstRow * Columns;
        var lastIndex = Math.Min(ItemCount, (lastRow + 1) * Columns);
        return Enumerable.Range(firstIndex, Math.Max(0, lastIndex - firstIndex)).ToArray();
    }

    private double RowTop(int row) =>
        row >= 0 && row < RowTops.Count ? RowTops[row] : Padding + row * RowHeight;

    private double ImageAreaHeightForRow(int row) =>
        row >= 0 && row < RowImageAreaHeights.Count ? RowImageAreaHeights[row] : ImageAreaHeight;

    private double HeightForRow(int row) =>
        row >= 0 && row < RowHeights.Count ? RowHeights[row] : RowHeight;
}

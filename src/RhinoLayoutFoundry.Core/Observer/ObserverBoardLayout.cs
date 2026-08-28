using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Observer;

public sealed record ObserverSheetCard(
    ObserverSheetSnapshot Sheet,
    ObserverRect Bounds,
    bool HasManualPlacement);

public sealed record ObserverDetailTarget(
    Guid SheetPageViewId,
    ObserverDetailSnapshot Detail,
    ObserverRect Bounds);

public sealed record ObserverFolderFrame(
    ObserverFolderSnapshot Folder,
    ObserverRect Bounds,
    int Depth,
    int DirectSheetCount);

public sealed record ObserverBoardLayout(
    IReadOnlyDictionary<Guid, ObserverSheetCard> Sheets,
    IReadOnlyDictionary<Guid, ObserverFolderFrame> Folders,
    ObserverRect Bounds)
{
    public static ObserverBoardLayout Empty { get; } = new(
        new Dictionary<Guid, ObserverSheetCard>(),
        new Dictionary<Guid, ObserverFolderFrame>(),
        new ObserverRect());
}

public sealed class ObserverPlacementPlanner
{
    public const double PaperScale = 0.45;
    public const double FolderPadding = 24;
    public const double FolderHeaderHeight = 36;
    public const double SheetGap = 20;
    public const double FolderGap = 52;
    public const double MaximumRowWidth = 1200;

    public ObserverBoardLayout Arrange(ObserverSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.HasDocument || snapshot.Folders.Count == 0)
        {
            return ObserverBoardLayout.Empty;
        }

        var folderMap = snapshot.Folders.ToDictionary(folder => folder.Id);
        var orderedFolders = PreOrderFolders(snapshot.RootFolderId, folderMap);
        var sheetCards = new Dictionary<Guid, ObserverSheetCard>();
        var folderFrames = new Dictionary<Guid, ObserverFolderFrame>();
        var cumulativeFolderOffsets = new Dictionary<Guid, ObserverPoint>();
        var cursorY = 0d;

        foreach (var (folder, depth) in orderedFolders)
        {
            var directSheets = snapshot.Sheets
                .Where(sheet => sheet.FolderId == folder.Id)
                .OrderBy(sheet => sheet.Order)
                .ThenBy(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var automaticOrigin = new ObserverPoint(depth * 44, cursorY);
            var localFolderOffset = snapshot.CanvasState.FolderOrigins.TryGetValue(folder.Id, out var origin)
                ? new ObserverPoint(origin.X, origin.Y)
                : new ObserverPoint(0, 0);
            var parentFolderOffset = folder.ParentId is { } parentId &&
                                     cumulativeFolderOffsets.TryGetValue(parentId, out var inheritedOffset)
                ? inheritedOffset
                : new ObserverPoint(0, 0);
            var folderOffset = parentFolderOffset + localFolderOffset;
            cumulativeFolderOffsets[folder.Id] = folderOffset;
            var contentX = automaticOrigin.X + FolderPadding;
            var contentY = automaticOrigin.Y + FolderHeaderHeight + FolderPadding;
            var rowX = 0d;
            var rowY = 0d;
            var rowHeight = 0d;
            var contentBounds = new ObserverRect();

            foreach (var sheet in directSheets)
            {
                var width = Math.Max(1, sheet.PaperWidthMillimeters) * PaperScale;
                var height = Math.Max(1, sheet.PaperHeightMillimeters) * PaperScale;
                if (rowX > 0 && rowX + width > MaximumRowWidth)
                {
                    rowX = 0;
                    rowY += rowHeight + SheetGap;
                    rowHeight = 0;
                }

                var automatic = new ObserverPoint(contentX + rowX, contentY + rowY);
                var hasManual = snapshot.CanvasState.SheetPlacements.TryGetValue(sheet.PageViewId, out var placement);
                var basePoint = hasManual
                    ? new ObserverPoint(placement.X, placement.Y)
                    : automatic;
                var bounds = new ObserverRect(
                    basePoint.X + folderOffset.X,
                    basePoint.Y + folderOffset.Y,
                    width,
                    height);
                sheetCards[sheet.PageViewId] = new ObserverSheetCard(sheet, bounds, hasManual);
                contentBounds = ObserverRect.Union(contentBounds, bounds);
                rowX += width + SheetGap;
                rowHeight = Math.Max(rowHeight, height);
            }

            ObserverRect frameBounds;
            if (contentBounds.IsEmpty)
            {
                frameBounds = new ObserverRect(
                    automaticOrigin.X + folderOffset.X,
                    automaticOrigin.Y + folderOffset.Y,
                    300,
                    92);
            }
            else
            {
                frameBounds = new ObserverRect(
                    Math.Min(automaticOrigin.X + folderOffset.X, contentBounds.Left - FolderPadding),
                    Math.Min(automaticOrigin.Y + folderOffset.Y, contentBounds.Top - FolderHeaderHeight - FolderPadding),
                    Math.Max(300, contentBounds.Right - automaticOrigin.X - folderOffset.X + FolderPadding),
                    Math.Max(92, contentBounds.Bottom - automaticOrigin.Y - folderOffset.Y + FolderPadding));
            }

            folderFrames[folder.Id] = new ObserverFolderFrame(folder, frameBounds, depth, directSheets.Length);
            cursorY += Math.Max(132, frameBounds.Height) + FolderGap;
        }

        var boardBounds = folderFrames.Values
            .Select(frame => frame.Bounds)
            .Concat(sheetCards.Values.Select(card => card.Bounds))
            .Aggregate(new ObserverRect(), ObserverRect.Union);
        return new ObserverBoardLayout(sheetCards, folderFrames, boardBounds);
    }

    public ObserverCanvasState MoveSheets(
        ObserverSnapshot snapshot,
        ObserverBoardLayout layout,
        IEnumerable<Guid> sheetIds,
        ObserverPoint worldDelta)
    {
        var placements = snapshot.CanvasState.SheetPlacements.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var sheetId in sheetIds.Distinct())
        {
            if (!layout.Sheets.TryGetValue(sheetId, out var card)) continue;
            var folderDelta = CumulativeFolderOffset(snapshot, card.Sheet.FolderId);
            placements[sheetId] = new ObserverPointRecord(
                card.Bounds.X + worldDelta.X - folderDelta.X,
                card.Bounds.Y + worldDelta.Y - folderDelta.Y);
        }

        return snapshot.CanvasState with { SheetPlacements = placements };
    }

    public ObserverCanvasState MoveFolder(
        ObserverSnapshot snapshot,
        Guid folderId,
        ObserverPoint worldDelta)
    {
        var origins = snapshot.CanvasState.FolderOrigins.ToDictionary(pair => pair.Key, pair => pair.Value);
        var before = origins.GetValueOrDefault(folderId);
        origins[folderId] = new ObserverPointRecord(before.X + worldDelta.X, before.Y + worldDelta.Y);
        return snapshot.CanvasState with { FolderOrigins = origins };
    }

    public ObserverCanvasState Tidy(
        ObserverSnapshot snapshot,
        IReadOnlySet<Guid>? sheetIds = null,
        IReadOnlySet<Guid>? folderIds = null)
    {
        var placements = snapshot.CanvasState.SheetPlacements.ToDictionary(pair => pair.Key, pair => pair.Value);
        var origins = snapshot.CanvasState.FolderOrigins.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (sheetIds is null && folderIds is null)
        {
            placements.Clear();
            origins.Clear();
        }
        else
        {
            if (sheetIds is not null)
            {
                foreach (var sheetId in sheetIds) placements.Remove(sheetId);
            }

            if (folderIds is not null)
            {
                var descendants = DescendantFolderIds(snapshot, folderIds);
                foreach (var folderId in descendants)
                {
                    origins.Remove(folderId);
                    foreach (var sheet in snapshot.Sheets.Where(sheet => sheet.FolderId == folderId))
                    {
                        placements.Remove(sheet.PageViewId);
                    }
                }
            }
        }

        return snapshot.CanvasState with
        {
            LayoutAlgorithmVersion = ObserverCanvasState.CurrentLayoutAlgorithmVersion,
            FolderOrigins = origins,
            SheetPlacements = placements,
        };
    }

    private static ObserverPoint CumulativeFolderOffset(ObserverSnapshot snapshot, Guid folderId)
    {
        var folders = snapshot.Folders.ToDictionary(folder => folder.Id);
        var result = new ObserverPoint(0, 0);
        var visited = new HashSet<Guid>();
        Guid? current = folderId;
        while (current is { } id && visited.Add(id) && folders.TryGetValue(id, out var folder))
        {
            if (snapshot.CanvasState.FolderOrigins.TryGetValue(id, out var origin))
                result += new ObserverPoint(origin.X, origin.Y);
            current = folder.ParentId;
        }

        return result;
    }

    private static IReadOnlySet<Guid> DescendantFolderIds(
        ObserverSnapshot snapshot,
        IEnumerable<Guid> folderIds)
    {
        var result = folderIds.ToHashSet();
        var added = true;
        while (added)
        {
            added = false;
            foreach (var folder in snapshot.Folders)
            {
                if (folder.ParentId is { } parentId && result.Contains(parentId) && result.Add(folder.Id))
                    added = true;
            }
        }

        return result;
    }

    private static IReadOnlyList<(ObserverFolderSnapshot Folder, int Depth)> PreOrderFolders(
        Guid rootFolderId,
        IReadOnlyDictionary<Guid, ObserverFolderSnapshot> folders)
    {
        if (!folders.TryGetValue(rootFolderId, out var root)) return [];
        var result = new List<(ObserverFolderSnapshot, int)>();
        var seen = new HashSet<Guid>();

        void Visit(ObserverFolderSnapshot folder, int depth)
        {
            if (!seen.Add(folder.Id)) return;
            result.Add((folder, depth));
            foreach (var child in folders.Values
                         .Where(candidate => candidate.ParentId == folder.Id)
                         .OrderBy(candidate => candidate.Order)
                         .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase))
            {
                Visit(child, depth + 1);
            }
        }

        Visit(root, 0);
        foreach (var orphan in folders.Values.Where(folder => !seen.Contains(folder.Id)))
        {
            Visit(orphan, 0);
        }

        return result;
    }
}

public sealed class ObserverSpatialIndex
{
    private readonly ObserverBoardLayout _layout;

    public ObserverSpatialIndex(ObserverBoardLayout layout)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
    }

    public IReadOnlyList<ObserverSheetCard> QuerySheets(ObserverRect worldBounds) =>
        _layout.Sheets.Values
            .Where(card => card.Bounds.Intersects(worldBounds))
            .OrderBy(card => card.Sheet.Order)
            .ThenBy(card => card.Sheet.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyList<ObserverFolderFrame> QueryFolders(ObserverRect worldBounds) =>
        _layout.Folders.Values
            .Where(frame => frame.Bounds.Intersects(worldBounds))
            .OrderBy(frame => frame.Depth)
            .ThenBy(frame => frame.Folder.Order)
            .ToArray();

    public ObserverSheetCard? HitSheet(ObserverPoint worldPoint) =>
        _layout.Sheets.Values
            .Where(card => card.Bounds.Contains(worldPoint))
            .OrderBy(card => card.Bounds.Width * card.Bounds.Height)
            .FirstOrDefault();

    public IReadOnlyList<ObserverDetailTarget> QueryDetails(ObserverRect worldBounds) =>
        QuerySheets(worldBounds)
            .SelectMany(card => card.Sheet.Details.Select(detail => new ObserverDetailTarget(
                card.Sheet.PageViewId,
                detail,
                DetailBounds(card.Bounds, detail.NormalizedBounds))))
            .Where(target => target.Bounds.Intersects(worldBounds))
            .OrderBy(target => target.Bounds.Width * target.Bounds.Height)
            .ThenBy(target => target.Detail.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public ObserverDetailTarget? HitDetail(ObserverPoint worldPoint) =>
        _layout.Sheets.Values
            .Where(card => card.Bounds.Contains(worldPoint))
            .SelectMany(card => card.Sheet.Details.Select(detail => new ObserverDetailTarget(
                card.Sheet.PageViewId,
                detail,
                DetailBounds(card.Bounds, detail.NormalizedBounds))))
            .Where(target => target.Bounds.Contains(worldPoint))
            .OrderBy(target => target.Bounds.Width * target.Bounds.Height)
            .FirstOrDefault();

    public ObserverFolderFrame? HitFolderHeader(ObserverPoint worldPoint, double headerHeight) =>
        _layout.Folders.Values
            .Where(frame => new ObserverRect(
                frame.Bounds.X,
                frame.Bounds.Y,
                frame.Bounds.Width,
                headerHeight).Contains(worldPoint))
            .OrderByDescending(frame => frame.Depth)
            .FirstOrDefault();

    public static ObserverRect DetailBounds(ObserverRect sheetBounds, ObserverRect normalizedBounds) => new(
        sheetBounds.Left + normalizedBounds.Left * sheetBounds.Width,
        sheetBounds.Top + normalizedBounds.Top * sheetBounds.Height,
        normalizedBounds.Width * sheetBounds.Width,
        normalizedBounds.Height * sheetBounds.Height);
}

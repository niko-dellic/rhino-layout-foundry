using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Observer;

public enum ObserverPackingMode
{
    NestedFolders,
    CompactSheets,
}

public enum ObserverAppearancePresentationMode
{
    Cards,
    CardsWithConnections,
    AssignmentBadges,
}

public sealed record ObserverSheetCard(
    ObserverSheetSnapshot Sheet,
    ObserverRect Bounds,
    bool HasManualPlacement);

public sealed record ObserverAppearanceStateCard(
    AppearanceStateRecord State,
    ObserverRect Bounds,
    bool HasManualPlacement = false);

public sealed record ObserverDetailTarget(
    Guid SheetPageViewId,
    ObserverDetailSnapshot Detail,
    ObserverRect Bounds);

public sealed record ObserverFolderFrame(
    ObserverFolderSnapshot Folder,
    ObserverRect Bounds,
    int Depth,
    int DirectSheetCount,
    int DirectAppearanceStateCount = 0);

public sealed record ObserverBoardLayout(
    IReadOnlyDictionary<Guid, ObserverSheetCard> Sheets,
    IReadOnlyDictionary<Guid, ObserverFolderFrame> Folders,
    ObserverRect Bounds,
    IReadOnlyDictionary<Guid, ObserverAppearanceStateCard>? AppearanceStateCards = null)
{
    public IReadOnlyDictionary<Guid, ObserverAppearanceStateCard> AppearanceStates =>
        AppearanceStateCards ?? new Dictionary<Guid, ObserverAppearanceStateCard>();

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
    public static readonly ObserverSize AppearanceStateSize = new(210, 72);

    public ObserverBoardLayout Arrange(
        ObserverSnapshot snapshot,
        ObserverPackingMode packingMode = ObserverPackingMode.NestedFolders,
        ObserverAppearancePresentationMode appearanceMode = ObserverAppearancePresentationMode.Cards)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.HasDocument || snapshot.Folders.Count == 0)
        {
            return ObserverBoardLayout.Empty;
        }

        return packingMode == ObserverPackingMode.CompactSheets
            ? ArrangeCompact(snapshot, appearanceMode)
            : ArrangeNested(snapshot, appearanceMode);
    }

    private static ObserverBoardLayout ArrangeCompact(
        ObserverSnapshot snapshot,
        ObserverAppearancePresentationMode appearanceMode)
    {
        var folderMap = snapshot.Folders.ToDictionary(folder => folder.Id);
        var folderOrder = PreOrderFolders(snapshot.RootFolderId, folderMap)
            .Select((entry, index) => (entry.Folder.Id, index))
            .ToDictionary(entry => entry.Id, entry => entry.index);
        var orderedSheets = snapshot.Sheets
            .OrderBy(sheet => folderOrder.GetValueOrDefault(sheet.FolderId, int.MaxValue))
            .ThenBy(sheet => sheet.Order)
            .ThenBy(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var packed = Pack(orderedSheets.Select(sheet => (
            sheet.PageViewId,
            SheetSize(sheet))), SheetGap);
        var cards = orderedSheets.ToDictionary(
            sheet => sheet.PageViewId,
            sheet =>
            {
                var position = packed.Positions[sheet.PageViewId];
                var size = SheetSize(sheet);
                return new ObserverSheetCard(
                    sheet,
                    new ObserverRect(position.X, position.Y, size.Width, size.Height),
                    HasManualPlacement: false);
            });
        var orderedStates = appearanceMode == ObserverAppearancePresentationMode.AssignmentBadges
            ? []
            : snapshot.AppearanceStates
            .OrderBy(state => folderOrder.GetValueOrDefault(state.FolderId, int.MaxValue))
            .ThenBy(state => state.Order)
            .ThenBy(state => state.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var statePack = Pack(orderedStates.Select(state => (state.Id, AppearanceStateSize)), SheetGap);
        var stateTop = packed.Size.IsEmpty ? 0 : packed.Size.Height + SheetGap;
        var stateCards = orderedStates.ToDictionary(
            state => state.Id,
            state =>
            {
                var position = statePack.Positions[state.Id];
                return new ObserverAppearanceStateCard(state,
                    new ObserverRect(position.X, position.Y + stateTop,
                        AppearanceStateSize.Width, AppearanceStateSize.Height),
                    HasManualPlacement: false);
            });
        var bounds = cards.Values
            .Select(card => card.Bounds)
            .Concat(stateCards.Values.Select(card => card.Bounds))
            .Aggregate(new ObserverRect(), ObserverRect.Union);
        return new ObserverBoardLayout(
            cards,
            new Dictionary<Guid, ObserverFolderFrame>(),
            bounds,
            stateCards);
    }

    private static ObserverBoardLayout ArrangeNested(
        ObserverSnapshot snapshot,
        ObserverAppearancePresentationMode appearanceMode)
    {
        var folderMap = snapshot.Folders.ToDictionary(folder => folder.Id);
        var orderedFolders = PreOrderFolders(snapshot.RootFolderId, folderMap);
        var children = folderMap.Values
            .Where(folder => folder.ParentId is { } parentId && folderMap.ContainsKey(parentId))
            .GroupBy(folder => folder.ParentId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(folder => folder.Order)
                    .ThenBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        var directSheets = snapshot.Sheets
            .GroupBy(sheet => sheet.FolderId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(sheet => sheet.Order)
                    .ThenBy(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        var visibleStates = appearanceMode == ObserverAppearancePresentationMode.AssignmentBadges
            ? []
            : snapshot.AppearanceStates;
        var directStates = visibleStates
            .GroupBy(state => state.FolderId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(state => state.Order)
                    .ThenBy(state => state.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        var measures = new Dictionary<Guid, FolderMeasure>();
        var measuring = new HashSet<Guid>();

        FolderMeasure Measure(Guid folderId)
        {
            if (measures.TryGetValue(folderId, out var cached)) return cached;
            if (!measuring.Add(folderId))
                return new FolderMeasure(new ObserverSize(300, 92), PackedLayout.Empty, PackedLayout.Empty);
            var sheets = directSheets.GetValueOrDefault(folderId, []);
            var states = directStates.GetValueOrDefault(folderId, []);
            var directPack = Pack(
                sheets.Select(sheet => (sheet.PageViewId, SheetSize(sheet)))
                    .Concat(states.Select(state => (state.Id, AppearanceStateSize))),
                SheetGap);
            var childFolders = children.GetValueOrDefault(folderId, []);
            var childPack = Pack(childFolders.Select(folder => (folder.Id, Measure(folder.Id).Size)), FolderPadding);
            var hasBoth = !directPack.Size.IsEmpty && !childPack.Size.IsEmpty;
            var contentWidth = Math.Max(directPack.Size.Width, childPack.Size.Width);
            var contentHeight = directPack.Size.Height + childPack.Size.Height +
                                (hasBoth ? FolderPadding : 0);
            var measured = new FolderMeasure(
                new ObserverSize(
                    Math.Max(300, contentWidth + FolderPadding * 2),
                    Math.Max(92, FolderHeaderHeight + FolderPadding * 2 + contentHeight)),
                directPack,
                childPack);
            measuring.Remove(folderId);
            measures[folderId] = measured;
            return measured;
        }

        var roots = orderedFolders
            .Where(entry => entry.Depth == 0)
            .Select(entry => entry.Folder)
            .ToArray();
        foreach (var folder in roots) Measure(folder.Id);
        var rootPack = Pack(roots.Select(folder => (folder.Id, measures[folder.Id].Size)), FolderGap);
        var cards = new Dictionary<Guid, ObserverSheetCard>();
        var stateCards = new Dictionary<Guid, ObserverAppearanceStateCard>();
        var frames = new Dictionary<Guid, ObserverFolderFrame>();

        ObserverRect Place(
            ObserverFolderSnapshot folder,
            ObserverPoint naturalOrigin,
            ObserverPoint inheritedOffset,
            int depth)
        {
            var measure = measures[folder.Id];
            var localOffset = snapshot.CanvasState.FolderOrigins.TryGetValue(folder.Id, out var origin)
                ? new ObserverPoint(origin.X, origin.Y)
                : new ObserverPoint();
            var cumulativeOffset = inheritedOffset + localOffset;
            var actualOrigin = naturalOrigin + cumulativeOffset;
            var contentBounds = new ObserverRect();
            var sheets = directSheets.GetValueOrDefault(folder.Id, []);
            foreach (var sheet in sheets)
            {
                var position = measure.DirectItems.Positions[sheet.PageViewId];
                var automatic = new ObserverPoint(
                    naturalOrigin.X + FolderPadding + position.X,
                    naturalOrigin.Y + FolderHeaderHeight + FolderPadding + position.Y);
                var hasManual = snapshot.CanvasState.SheetPlacements.TryGetValue(
                    sheet.PageViewId,
                    out var placement);
                var basePoint = hasManual
                    ? new ObserverPoint(placement.X, placement.Y)
                    : automatic;
                var size = SheetSize(sheet);
                var bounds = new ObserverRect(
                    basePoint.X + cumulativeOffset.X,
                    basePoint.Y + cumulativeOffset.Y,
                    size.Width,
                    size.Height);
                cards[sheet.PageViewId] = new ObserverSheetCard(sheet, bounds, hasManual);
                contentBounds = ObserverRect.Union(contentBounds, bounds);
            }
            foreach (var state in directStates.GetValueOrDefault(folder.Id, []))
            {
                var position = measure.DirectItems.Positions[state.Id];
                var hasManual = snapshot.CanvasState.StatePlacements.TryGetValue(state.Id, out var placement);
                var basePoint = hasManual
                    ? new ObserverPoint(placement.X, placement.Y)
                    : new ObserverPoint(
                        naturalOrigin.X + FolderPadding + position.X,
                        naturalOrigin.Y + FolderHeaderHeight + FolderPadding + position.Y);
                var bounds = new ObserverRect(
                    basePoint.X + cumulativeOffset.X,
                    basePoint.Y + cumulativeOffset.Y,
                    AppearanceStateSize.Width,
                    AppearanceStateSize.Height);
                stateCards[state.Id] = new ObserverAppearanceStateCard(state, bounds, hasManual);
                contentBounds = ObserverRect.Union(contentBounds, bounds);
            }

            var childTop = naturalOrigin.Y + FolderHeaderHeight + FolderPadding +
                           measure.DirectItems.Size.Height +
                           (!measure.DirectItems.Size.IsEmpty && !measure.ChildFolders.Size.IsEmpty
                               ? FolderPadding
                               : 0);
            foreach (var child in children.GetValueOrDefault(folder.Id, []))
            {
                var position = measure.ChildFolders.Positions[child.Id];
                var childBounds = Place(
                    child,
                    new ObserverPoint(
                        naturalOrigin.X + FolderPadding + position.X,
                        childTop + position.Y),
                    cumulativeOffset,
                    depth + 1);
                contentBounds = ObserverRect.Union(contentBounds, childBounds);
            }

            var frameBounds = new ObserverRect(
                actualOrigin.X,
                actualOrigin.Y,
                measure.Size.Width,
                measure.Size.Height);
            if (!contentBounds.IsEmpty)
                frameBounds = ObserverRect.Union(frameBounds, contentBounds.Inflate(FolderPadding));
            frames[folder.Id] = new ObserverFolderFrame(
                folder, frameBounds, depth, sheets.Length,
                directStates.GetValueOrDefault(folder.Id, []).Length);
            return frameBounds;
        }

        foreach (var root in roots)
            Place(root, rootPack.Positions[root.Id], new ObserverPoint(), 0);

        var boardBounds = frames.Values
            .Select(frame => frame.Bounds)
            .Concat(cards.Values.Select(card => card.Bounds))
            .Concat(stateCards.Values.Select(card => card.Bounds))
            .Aggregate(new ObserverRect(), ObserverRect.Union);
        return new ObserverBoardLayout(cards, frames, boardBounds, stateCards);
    }

    private static ObserverSize SheetSize(ObserverSheetSnapshot sheet) => new(
        Math.Max(1, sheet.PaperWidthMillimeters) * PaperScale,
        Math.Max(1, sheet.PaperHeightMillimeters) * PaperScale);

    private static PackedLayout Pack(
        IEnumerable<(Guid Id, ObserverSize Size)> items,
        double gap)
    {
        var positions = new Dictionary<Guid, ObserverPoint>();
        var rowX = 0d;
        var rowY = 0d;
        var rowHeight = 0d;
        var width = 0d;
        var hasItems = false;
        foreach (var (id, size) in items)
        {
            hasItems = true;
            if (rowX > 0 && rowX + size.Width > MaximumRowWidth)
            {
                rowX = 0;
                rowY += rowHeight + gap;
                rowHeight = 0;
            }

            positions[id] = new ObserverPoint(rowX, rowY);
            rowX += size.Width + gap;
            rowHeight = Math.Max(rowHeight, size.Height);
            width = Math.Max(width, rowX - gap);
        }

        return new PackedLayout(
            positions,
            hasItems ? new ObserverSize(width, rowY + rowHeight) : new ObserverSize());
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

    public ObserverCanvasState MoveAppearanceStates(
        ObserverSnapshot snapshot,
        ObserverBoardLayout layout,
        IEnumerable<Guid> stateIds,
        ObserverPoint worldDelta)
    {
        var placements = snapshot.CanvasState.StatePlacements
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var stateId in stateIds.Distinct())
        {
            if (!layout.AppearanceStates.TryGetValue(stateId, out var card)) continue;
            var folderDelta = CumulativeFolderOffset(snapshot, card.State.FolderId);
            placements[stateId] = new ObserverPointRecord(
                card.Bounds.X + worldDelta.X - folderDelta.X,
                card.Bounds.Y + worldDelta.Y - folderDelta.Y);
        }

        return snapshot.CanvasState with { StatePlacements = placements };
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
        IReadOnlySet<Guid>? folderIds = null,
        IReadOnlySet<Guid>? appearanceStateIds = null)
    {
        var placements = snapshot.CanvasState.SheetPlacements.ToDictionary(pair => pair.Key, pair => pair.Value);
        var origins = snapshot.CanvasState.FolderOrigins.ToDictionary(pair => pair.Key, pair => pair.Value);
        var statePlacements = snapshot.CanvasState.StatePlacements
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        if (sheetIds is null && folderIds is null && appearanceStateIds is null)
        {
            placements.Clear();
            origins.Clear();
            statePlacements.Clear();
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
                    foreach (var state in snapshot.AppearanceStates.Where(state => state.FolderId == folderId))
                    {
                        statePlacements.Remove(state.Id);
                    }
                }
            }

            if (appearanceStateIds is not null)
            {
                foreach (var stateId in appearanceStateIds) statePlacements.Remove(stateId);
            }
        }

        return snapshot.CanvasState with
        {
            LayoutAlgorithmVersion = ObserverCanvasState.CurrentLayoutAlgorithmVersion,
            FolderOrigins = origins,
            SheetPlacements = placements,
            StatePlacements = statePlacements,
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

    private sealed record FolderMeasure(
        ObserverSize Size,
        PackedLayout DirectItems,
        PackedLayout ChildFolders);

    private sealed record PackedLayout(
        IReadOnlyDictionary<Guid, ObserverPoint> Positions,
        ObserverSize Size)
    {
        internal static PackedLayout Empty { get; } = new(
            new Dictionary<Guid, ObserverPoint>(),
            new ObserverSize());
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

    public IReadOnlyList<ObserverAppearanceStateCard> QueryAppearanceStates(ObserverRect worldBounds) =>
        _layout.AppearanceStates.Values
            .Where(card => card.Bounds.Intersects(worldBounds))
            .OrderBy(card => card.State.Order)
            .ThenBy(card => card.State.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public ObserverSheetCard? HitSheet(ObserverPoint worldPoint) =>
        _layout.Sheets.Values
            .Where(card => card.Bounds.Contains(worldPoint))
            .OrderBy(card => card.Bounds.Width * card.Bounds.Height)
            .FirstOrDefault();

    public ObserverAppearanceStateCard? HitAppearanceState(ObserverPoint worldPoint) =>
        _layout.AppearanceStates.Values.FirstOrDefault(card => card.Bounds.Contains(worldPoint));

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

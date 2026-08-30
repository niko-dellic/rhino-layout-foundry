namespace RhinoLayoutFoundry.Core.Observer;

public enum ObserverCanvasLodTier
{
    Folder,
    Sheet,
    Detail,
}

public sealed record ObserverFolderSummary(
    Guid FolderId,
    string Name,
    ObserverRect WorldBounds,
    ObserverRect ScreenBounds,
    IReadOnlySet<Guid> RepresentedSheetIds)
{
    public int LayoutCount => RepresentedSheetIds.Count;
}

public sealed record ObserverCanvasPresentation(
    IReadOnlyDictionary<Guid, ObserverCanvasLodTier> SheetTiers,
    IReadOnlyList<ObserverFolderSummary> FolderSummaries)
{
    public static ObserverCanvasPresentation Empty { get; } = new(
        new Dictionary<Guid, ObserverCanvasLodTier>(),
        []);

    public ObserverCanvasLodTier TierForSheet(Guid sheetId) =>
        SheetTiers.GetValueOrDefault(sheetId, ObserverCanvasLodTier.Folder);

    public IReadOnlySet<Guid> PreviewEligibleSheetIds => SheetTiers
        .Where(pair => pair.Value == ObserverCanvasLodTier.Detail)
        .Select(pair => pair.Key)
        .ToHashSet();
}

public sealed class ObserverCanvasLodPolicy
{
    public const double LeaveDetailPixels = 72;
    public const double EnterDetailPixels = 88;
    public const double LeaveSheetPixels = 28;
    public const double EnterSheetPixels = 36;
    public const double InitialDetailPixels = 80;
    public const double InitialSheetPixels = 32;
    public const double FolderSummaryHeightPixels = 30;
    public const double MinimumFolderSummaryWidthPixels = 112;
    public const double MaximumFolderSummaryWidthPixels = 220;

    public ObserverCanvasPresentation Evaluate(
        ObserverSnapshot snapshot,
        ObserverBoardLayout layout,
        ObserverCamera camera,
        ObserverSize viewport,
        ObserverPackingMode packingMode,
        ObserverCanvasPresentation? previous = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(camera);

        if (!snapshot.HasDocument || layout.Sheets.Count == 0)
            return ObserverCanvasPresentation.Empty;

        var tiers = layout.Sheets.Values.ToDictionary(
            card => card.Sheet.PageViewId,
            card => SelectTier(
                Math.Min(card.Bounds.Width, card.Bounds.Height) * camera.Zoom,
                PreviousTier(previous, card.Sheet.PageViewId)));
        var summaries = BuildFolderSummaries(
            snapshot,
            layout,
            camera,
            viewport,
            packingMode,
            tiers);
        return new ObserverCanvasPresentation(tiers, summaries);
    }

    public static ObserverCanvasLodTier SelectTier(
        double projectedShortEdgePixels,
        ObserverCanvasLodTier? previousTier = null)
    {
        if (!double.IsFinite(projectedShortEdgePixels))
            projectedShortEdgePixels = 0;

        return previousTier switch
        {
            ObserverCanvasLodTier.Detail => projectedShortEdgePixels < LeaveDetailPixels
                ? projectedShortEdgePixels < LeaveSheetPixels
                    ? ObserverCanvasLodTier.Folder
                    : ObserverCanvasLodTier.Sheet
                : ObserverCanvasLodTier.Detail,
            ObserverCanvasLodTier.Sheet => projectedShortEdgePixels > EnterDetailPixels
                ? ObserverCanvasLodTier.Detail
                : projectedShortEdgePixels < LeaveSheetPixels
                    ? ObserverCanvasLodTier.Folder
                    : ObserverCanvasLodTier.Sheet,
            ObserverCanvasLodTier.Folder => projectedShortEdgePixels > EnterSheetPixels
                ? projectedShortEdgePixels > EnterDetailPixels
                    ? ObserverCanvasLodTier.Detail
                    : ObserverCanvasLodTier.Sheet
                : ObserverCanvasLodTier.Folder,
            _ => projectedShortEdgePixels >= InitialDetailPixels
                ? ObserverCanvasLodTier.Detail
                : projectedShortEdgePixels >= InitialSheetPixels
                    ? ObserverCanvasLodTier.Sheet
                    : ObserverCanvasLodTier.Folder,
        };
    }

    private static ObserverCanvasLodTier? PreviousTier(
        ObserverCanvasPresentation? previous,
        Guid sheetId) =>
        previous is not null && previous.SheetTiers.TryGetValue(sheetId, out var tier)
            ? tier
            : null;

    private static IReadOnlyList<ObserverFolderSummary> BuildFolderSummaries(
        ObserverSnapshot snapshot,
        ObserverBoardLayout layout,
        ObserverCamera camera,
        ObserverSize viewport,
        ObserverPackingMode packingMode,
        IReadOnlyDictionary<Guid, ObserverCanvasLodTier> tiers)
    {
        var hiddenCards = layout.Sheets.Values
            .Where(card => tiers.GetValueOrDefault(card.Sheet.PageViewId) == ObserverCanvasLodTier.Folder)
            .ToArray();
        if (hiddenCards.Length == 0) return [];

        var folders = snapshot.Folders.ToDictionary(folder => folder.Id);
        if (folders.Count == 0) return [];
        var fallbackFolderId = folders.ContainsKey(snapshot.RootFolderId)
            ? snapshot.RootFolderId
            : folders.Values
                .OrderBy(folder => folder.Order)
                .ThenBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
                .First().Id;
        var candidates = hiddenCards
            .GroupBy(card => folders.ContainsKey(card.Sheet.FolderId)
                ? card.Sheet.FolderId
                : fallbackFolderId)
            .Select(group => Candidate(
                folders[group.Key],
                group.Select(card => card.Sheet.PageViewId).ToHashSet(),
                group.Select(card => card.Bounds).Aggregate(new ObserverRect(), ObserverRect.Union),
                camera,
                viewport))
            .ToList();

        var descendantSheetFolders = snapshot.Sheets.Select(sheet => sheet.FolderId).ToHashSet();
        var emptyLeaves = snapshot.Folders.Where(folder =>
                !descendantSheetFolders.Contains(folder.Id) &&
                !snapshot.Folders.Any(candidate => candidate.ParentId == folder.Id))
            .ToArray();
        var emptyFolderLayout = packingMode == ObserverPackingMode.CompactSheets && emptyLeaves.Length > 0
            ? new ObserverPlacementPlanner().Arrange(snapshot, ObserverPackingMode.NestedFolders)
            : layout;
        foreach (var empty in emptyLeaves)
        {
            if (!emptyFolderLayout.Folders.TryGetValue(empty.Id, out var frame)) continue;
            candidates.Add(Candidate(empty, new HashSet<Guid>(), frame.Bounds, camera, viewport));
        }

        var parents = folders.ToDictionary(pair => pair.Key, pair => pair.Value.ParentId);
        var mergeGuard = Math.Max(8, candidates.Count * candidates.Count * 2);
        while (mergeGuard-- > 0)
        {
            var collision = FindCollision(candidates);
            if (collision is null) break;
            var (leftIndex, rightIndex) = collision.Value;
            var left = candidates[leftIndex];
            var right = candidates[rightIndex];
            var ancestorId = CommonAncestor(left.Folder.Id, right.Folder.Id, snapshot.RootFolderId, parents);
            if (!folders.TryGetValue(ancestorId, out var ancestor)) ancestor = left.Folder;
            var represented = left.RepresentedSheetIds
                .Concat(right.RepresentedSheetIds)
                .ToHashSet();
            var worldBounds = ObserverRect.Union(left.WorldBounds, right.WorldBounds);
            var merged = Candidate(ancestor, represented, worldBounds, camera, viewport);
            candidates.RemoveAt(Math.Max(leftIndex, rightIndex));
            candidates.RemoveAt(Math.Min(leftIndex, rightIndex));

            var sameFolder = candidates
                .Select((candidate, index) => (candidate, index))
                .Where(entry => entry.candidate.Folder.Id == ancestor.Id)
                .Select(entry => entry.index)
                .ToArray();
            foreach (var index in sameFolder.OrderByDescending(index => index))
            {
                represented.UnionWith(candidates[index].RepresentedSheetIds);
                worldBounds = ObserverRect.Union(worldBounds, candidates[index].WorldBounds);
                candidates.RemoveAt(index);
            }

            candidates.Add(Candidate(ancestor, represented, worldBounds, camera, viewport));
        }

        return candidates
            .OrderBy(candidate => candidate.Folder.Order)
            .ThenBy(candidate => candidate.Folder.Name, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => new ObserverFolderSummary(
                candidate.Folder.Id,
                candidate.Folder.Name,
                candidate.WorldBounds,
                candidate.ScreenBounds,
                candidate.RepresentedSheetIds))
            .ToArray();
    }

    private static FolderCandidate Candidate(
        ObserverFolderSnapshot folder,
        IReadOnlySet<Guid> representedSheetIds,
        ObserverRect worldBounds,
        ObserverCamera camera,
        ObserverSize viewport)
    {
        var center = camera.WorldToScreen(worldBounds.Center, viewport);
        var countWidth = representedSheetIds.Count == 0
            ? 0
            : 42 + representedSheetIds.Count.ToString().Length * 7;
        var width = Math.Clamp(
            42 + folder.Name.Length * 7 + countWidth,
            MinimumFolderSummaryWidthPixels,
            MaximumFolderSummaryWidthPixels);
        return new FolderCandidate(
            folder,
            representedSheetIds,
            worldBounds,
            new ObserverRect(
                center.X - width / 2,
                center.Y - FolderSummaryHeightPixels / 2,
                width,
                FolderSummaryHeightPixels));
    }

    private static (int Left, int Right)? FindCollision(IReadOnlyList<FolderCandidate> candidates)
    {
        for (var left = 0; left < candidates.Count; left++)
        for (var right = left + 1; right < candidates.Count; right++)
        {
            if (candidates[left].Folder.Id == candidates[right].Folder.Id ||
                candidates[left].ScreenBounds.Inflate(4).Intersects(candidates[right].ScreenBounds))
                return (left, right);
        }

        return null;
    }

    private static Guid CommonAncestor(
        Guid left,
        Guid right,
        Guid root,
        IReadOnlyDictionary<Guid, Guid?> parents)
    {
        var leftAncestors = new HashSet<Guid>();
        Guid? current = left;
        while (current is { } id && leftAncestors.Add(id))
            current = parents.GetValueOrDefault(id);

        current = right;
        var visited = new HashSet<Guid>();
        while (current is { } id && visited.Add(id))
        {
            if (leftAncestors.Contains(id)) return id;
            current = parents.GetValueOrDefault(id);
        }

        return root;
    }

    private sealed record FolderCandidate(
        ObserverFolderSnapshot Folder,
        IReadOnlySet<Guid> RepresentedSheetIds,
        ObserverRect WorldBounds,
        ObserverRect ScreenBounds);
}

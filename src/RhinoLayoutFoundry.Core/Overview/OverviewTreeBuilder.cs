namespace RhinoLayoutFoundry.Core.Overview;

public enum OverviewNodeKind
{
    Folder,
    Sheet,
    Detail,
}

public enum OverviewFilterKind
{
    All,
    Sheets,
    Details,
    Tagged,
    Untagged,
}

public readonly record struct OverviewNodeKey(OverviewNodeKind Kind, Guid Id);

public readonly record struct OverviewTreeFilter(
    string? Text,
    OverviewFilterKind Kind = OverviewFilterKind.All)
{
    public string? Query => string.IsNullOrWhiteSpace(Text) ? null : Text.Trim();

    public bool IsActive => Query is not null || Kind != OverviewFilterKind.All;
}

public readonly record struct OverviewNavigationTarget(
    Guid SheetPageViewId,
    Guid? DetailViewportId = null);

public sealed record OverviewTreeNode(
    OverviewNodeKey Key,
    string Label,
    string SecondaryText,
    IReadOnlyList<OverviewTreeNode> Children,
    SheetOverview? Sheet = null,
    DetailOverview? Detail = null,
    OverviewNavigationTarget? NavigationTarget = null,
    IReadOnlyList<OverviewIssue>? Diagnostics = null,
    bool IsDocumentRoot = false)
{
    public IReadOnlyList<OverviewIssue> Issues => Diagnostics ?? [];

    public string StatusText => OverviewDiagnostics.Badge(Issues);
}

public static class OverviewTreeBuilder
{
    public static IReadOnlyList<OverviewTreeNode> Build(
        DocumentOverview overview,
        string? filterText = null)
    {
        return Build(overview, new OverviewTreeFilter(filterText));
    }

    public static IReadOnlyList<OverviewTreeNode> Build(
        DocumentOverview overview,
        OverviewTreeFilter filter)
    {
        ArgumentNullException.ThrowIfNull(overview);

        if (overview.RootFolderId is not { } rootId || overview.Folders.Count == 0)
        {
            return [];
        }

        var folders = overview.Folders.ToDictionary(folder => folder.Id);
        if (!folders.ContainsKey(rootId))
        {
            return [];
        }

        var childFolders = overview.Folders
            .Where(folder => folder.ParentId is not null && folders.ContainsKey(folder.ParentId.Value))
            .GroupBy(folder => folder.ParentId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(folder => folder.Order)
                    .ThenBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        var sheetsByFolder = overview.Sheets
            .GroupBy(sheet => folders.ContainsKey(sheet.FolderId) ? sheet.FolderId : rootId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(sheet => sheet.Order)
                    .ThenBy(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray());

        var rootChildren = BuildFolderChildren(
            rootId,
            childFolders,
            sheetsByFolder,
            filter,
            new HashSet<Guid> { rootId },
            includeAllTextMatches: false);
        if (filter.IsActive && rootChildren.Count == 0)
        {
            return [];
        }

        return
        [
            new OverviewTreeNode(
                new OverviewNodeKey(OverviewNodeKind.Folder, rootId),
                overview.DocumentName,
                Pluralize(CountSheets(rootChildren), "sheet"),
                rootChildren,
                IsDocumentRoot: true),
        ];
    }

    private static IReadOnlyList<OverviewTreeNode> BuildFolderChildren(
        Guid folderId,
        IReadOnlyDictionary<Guid, FolderOverview[]> childFolders,
        IReadOnlyDictionary<Guid, SheetOverview[]> sheetsByFolder,
        OverviewTreeFilter filter,
        HashSet<Guid> ancestors,
        bool includeAllTextMatches)
    {
        var children = new List<OverviewTreeNode>();
        if (childFolders.TryGetValue(folderId, out var folders))
        {
            foreach (var childFolder in folders)
            {
                var child = BuildFolder(
                    childFolder,
                    childFolders,
                    sheetsByFolder,
                    filter,
                    ancestors,
                    includeAllTextMatches);
                if (child is not null)
                {
                    children.Add(child);
                }
            }
        }

        if (sheetsByFolder.TryGetValue(folderId, out var sheets))
        {
            foreach (var sheet in sheets)
            {
                var child = BuildSheet(sheet, filter, includeAllTextMatches);
                if (child is not null)
                {
                    children.Add(child);
                }
            }
        }

        return children;
    }

    private static OverviewTreeNode? BuildFolder(
        FolderOverview folder,
        IReadOnlyDictionary<Guid, FolderOverview[]> childFolders,
        IReadOnlyDictionary<Guid, SheetOverview[]> sheetsByFolder,
        OverviewTreeFilter filter,
        HashSet<Guid> ancestors,
        bool includeAllTextMatches)
    {
        if (!ancestors.Add(folder.Id))
        {
            return null;
        }

        var folderMatchesText = includeAllTextMatches || Matches(folder.Name, filter.Query);
        var children = BuildFolderChildren(
            folder.Id,
            childFolders,
            sheetsByFolder,
            filter,
            ancestors,
            folderMatchesText);
        ancestors.Remove(folder.Id);

        if (children.Count == 0 && filter.IsActive)
        {
            return null;
        }

        var sheetCount = CountSheets(children);
        return new OverviewTreeNode(
            new OverviewNodeKey(OverviewNodeKind.Folder, folder.Id),
            folder.Name,
            Pluralize(sheetCount, "sheet"),
            children);
    }

    private static OverviewTreeNode? BuildSheet(
        SheetOverview sheet,
        OverviewTreeFilter filter,
        bool includeAllTextMatches)
    {
        if (!MatchesKind(sheet, filter.Kind))
        {
            return null;
        }

        var showDetails = filter.Kind != OverviewFilterKind.Sheets;
        var sheetMatchesText = includeAllTextMatches ||
                               Matches(sheet.Name, filter.Query) ||
                               sheet.Tags.Any(tag => Matches(tag, filter.Query));
        var details = showDetails
            ? sheet.Details
                .OrderBy(detail => detail.Order)
                .Where(detail => sheetMatchesText || Matches(detail.Name, filter.Query))
                .Select(detail => new OverviewTreeNode(
                    new OverviewNodeKey(OverviewNodeKind.Detail, detail.DetailViewportId),
                    detail.Name,
                    "Detail viewport",
                    [],
                    Detail: detail,
                    NavigationTarget: new OverviewNavigationTarget(
                        sheet.PageViewId,
                        detail.DetailViewportId)))
                .ToArray()
            : [];

        if (!sheetMatchesText && details.Length == 0 && filter.Query is not null)
        {
            return null;
        }

        return new OverviewTreeNode(
            new OverviewNodeKey(OverviewNodeKind.Sheet, sheet.PageViewId),
            sheet.Name,
            Pluralize(sheet.DetailCount, "detail"),
            details,
            Sheet: sheet,
            NavigationTarget: new OverviewNavigationTarget(sheet.PageViewId),
            Diagnostics: sheet.Issues);
    }

    private static bool MatchesKind(SheetOverview sheet, OverviewFilterKind kind)
    {
        return kind switch
        {
            OverviewFilterKind.All => true,
            OverviewFilterKind.Sheets => true,
            OverviewFilterKind.Details => sheet.DetailCount > 0,
            OverviewFilterKind.Tagged => sheet.Tags.Count > 0,
            OverviewFilterKind.Untagged => sheet.Tags.Count == 0,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    private static bool Matches(string value, string? query)
    {
        return query is null || value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static int CountSheets(IEnumerable<OverviewTreeNode> nodes)
    {
        return nodes.Sum(node =>
            node.Key.Kind == OverviewNodeKind.Sheet
                ? 1
                : CountSheets(node.Children));
    }

    private static string Pluralize(int count, string singular)
    {
        return $"{count} {singular}{(count == 1 ? string.Empty : "s")}";
    }
}

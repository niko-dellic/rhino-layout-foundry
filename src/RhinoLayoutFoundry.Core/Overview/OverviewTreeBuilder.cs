namespace RhinoLayoutFoundry.Core.Overview;

public enum OverviewNodeKind
{
    Folder,
    Sheet,
    Detail,
}

public readonly record struct OverviewNodeKey(OverviewNodeKind Kind, Guid Id);

public sealed record OverviewTreeNode(
    OverviewNodeKey Key,
    string Label,
    string SecondaryText,
    IReadOnlyList<OverviewTreeNode> Children,
    SheetOverview? Sheet = null);

public static class OverviewTreeBuilder
{
    public static IReadOnlyList<OverviewTreeNode> Build(
        DocumentOverview overview,
        string? filterText = null)
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
        var query = filterText?.Trim();

        var root = BuildFolder(
            folders[rootId],
            childFolders,
            sheetsByFolder,
            query,
            new HashSet<Guid>(),
            includeAll: false);
        return root is null ? [] : [root];
    }

    private static OverviewTreeNode? BuildFolder(
        FolderOverview folder,
        IReadOnlyDictionary<Guid, FolderOverview[]> childFolders,
        IReadOnlyDictionary<Guid, SheetOverview[]> sheetsByFolder,
        string? query,
        HashSet<Guid> ancestors,
        bool includeAll)
    {
        if (!ancestors.Add(folder.Id))
        {
            return null;
        }

        var folderMatches = includeAll || Matches(folder.Name, query);
        var children = new List<OverviewTreeNode>();
        if (childFolders.TryGetValue(folder.Id, out var folders))
        {
            foreach (var childFolder in folders)
            {
                var child = BuildFolder(
                    childFolder,
                    childFolders,
                    sheetsByFolder,
                    query,
                    ancestors,
                    folderMatches);
                if (child is not null)
                {
                    children.Add(child);
                }
            }
        }

        if (sheetsByFolder.TryGetValue(folder.Id, out var sheets))
        {
            foreach (var sheet in sheets)
            {
                var child = BuildSheet(sheet, query, folderMatches);
                if (child is not null)
                {
                    children.Add(child);
                }
            }
        }

        ancestors.Remove(folder.Id);
        if (!folderMatches && children.Count == 0 && query is not null)
        {
            return null;
        }

        return new OverviewTreeNode(
            new OverviewNodeKey(OverviewNodeKind.Folder, folder.Id),
            folder.Name,
            $"{CountSheets(children)} sheet{(CountSheets(children) == 1 ? string.Empty : "s")}",
            children);
    }

    private static OverviewTreeNode? BuildSheet(
        SheetOverview sheet,
        string? query,
        bool includeAll)
    {
        var details = sheet.Details
            .OrderBy(detail => detail.Order)
            .Select(detail => new OverviewTreeNode(
                new OverviewNodeKey(OverviewNodeKind.Detail, detail.DetailViewportId),
                detail.Name,
                "Detail viewport",
                []))
            .Where(detail => includeAll || Matches(detail.Label, query))
            .ToArray();
        var sheetMatches = includeAll || Matches(sheet.Name, query) ||
                           sheet.Tags.Any(tag => Matches(tag, query));

        if (!sheetMatches && details.Length == 0 && query is not null)
        {
            return null;
        }

        var visibleDetails = sheetMatches && query is not null
            ? sheet.Details
                .OrderBy(detail => detail.Order)
                .Select(detail => new OverviewTreeNode(
                    new OverviewNodeKey(OverviewNodeKind.Detail, detail.DetailViewportId),
                    detail.Name,
                    "Detail viewport",
                    []))
                .ToArray()
            : details;
        var tags = sheet.Tags.Count == 0 ? "No tags" : string.Join(", ", sheet.Tags);

        return new OverviewTreeNode(
            new OverviewNodeKey(OverviewNodeKind.Sheet, sheet.PageViewId),
            sheet.Name,
            $"{sheet.DetailCount} detail{(sheet.DetailCount == 1 ? string.Empty : "s")} · {tags}",
            visibleDetails,
            sheet);
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
}

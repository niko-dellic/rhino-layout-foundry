using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Overview;

public enum OverviewNodeKind
{
    Folder,
    Sheet,
    Detail,
    AppearanceState,
}

public enum OverviewFilterKind
{
    All,
    Sheets,
    Details,
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
    FolderOverview? Folder = null,
    SheetOverview? Sheet = null,
    DetailOverview? Detail = null,
    AppearanceStateOverview? AppearanceState = null,
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
        var statesByFolder = overview.AppearanceStates
            .GroupBy(state => folders.ContainsKey(state.FolderId) ? state.FolderId : rootId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(state => state.Order)
                    .ThenBy(state => state.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray());

        var rootFolder = folders[rootId];
        var rootNotesMatch = filter.Query is not null && Matches(rootFolder.Notes, filter.Query);
        var rootChildren = BuildFolderChildren(
            rootId,
            childFolders,
            sheetsByFolder,
            statesByFolder,
            filter,
            new HashSet<Guid> { rootId },
            includeAllTextMatches: rootNotesMatch);
        if (filter.IsActive && rootChildren.Count == 0)
        {
            return [];
        }

        return
        [
            new OverviewTreeNode(
                new OverviewNodeKey(OverviewNodeKind.Folder, rootId),
                DocumentRootLabel(overview.DocumentName),
                Pluralize(CountSheets(rootChildren), "sheet"),
                rootChildren,
                Folder: rootFolder,
                IsDocumentRoot: true),
        ];
    }

    private static string DocumentRootLabel(string documentName)
    {
        var name = string.IsNullOrWhiteSpace(documentName)
            ? "Untitled"
            : documentName.Trim();
        return name.EndsWith(".3dm", StringComparison.OrdinalIgnoreCase)
            ? name
            : $"{name}.3dm";
    }

    private static IReadOnlyList<OverviewTreeNode> BuildFolderChildren(
        Guid folderId,
        IReadOnlyDictionary<Guid, FolderOverview[]> childFolders,
        IReadOnlyDictionary<Guid, SheetOverview[]> sheetsByFolder,
        IReadOnlyDictionary<Guid, AppearanceStateOverview[]> statesByFolder,
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
                    statesByFolder,
                    filter,
                    ancestors,
                    includeAllTextMatches);
                if (child is not null)
                {
                    children.Add(child);
                }
            }
        }

        if (filter.Kind == OverviewFilterKind.All && statesByFolder.TryGetValue(folderId, out var states))
        {
            foreach (var state in states.Where(state =>
                         includeAllTextMatches || Matches(state.Name, filter.Query)))
            {
                children.Add(new OverviewTreeNode(
                    new OverviewNodeKey(OverviewNodeKind.AppearanceState, state.Id),
                    state.Name,
                    "Appearance State",
                    [],
                    AppearanceState: state));
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
        IReadOnlyDictionary<Guid, AppearanceStateOverview[]> statesByFolder,
        OverviewTreeFilter filter,
        HashSet<Guid> ancestors,
        bool includeAllTextMatches)
    {
        if (!ancestors.Add(folder.Id))
        {
            return null;
        }

        var folderMatchesText = includeAllTextMatches ||
                                Matches(folder.Name, filter.Query) ||
                                Matches(folder.Notes, filter.Query);
        var children = BuildFolderChildren(
            folder.Id,
            childFolders,
            sheetsByFolder,
            statesByFolder,
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
            children,
            Folder: folder);
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
                               Matches(sheet.Notes, filter.Query);
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

namespace RhinoLayoutFoundry.Core.Overview;

public sealed record LayoutPrintScope(
    Guid? FolderId,
    string Name,
    IReadOnlyList<Guid> SheetPageViewIds,
    bool Exists)
{
    public bool HasSheets => SheetPageViewIds.Count > 0;
}

/// <summary>
/// Resolves PDF page order from the Foundry hierarchy. The order intentionally
/// matches the tree: child folders (recursively) appear before direct sheets.
/// </summary>
public static class LayoutPrintScopeResolver
{
    public static LayoutPrintScope Resolve(DocumentOverview overview, Guid? folderId)
    {
        ArgumentNullException.ThrowIfNull(overview);

        if (overview.RootFolderId is not { } rootFolderId)
        {
            return new LayoutPrintScope(folderId, "Layouts", [], false);
        }

        var roots = OverviewTreeBuilder.Build(overview);
        if (folderId is null || folderId == rootFolderId)
        {
            return new LayoutPrintScope(
                null,
                "All Layouts",
                CollectSheets(roots),
                true);
        }

        var folder = FindFolder(roots, folderId.Value);
        return folder is null
            ? new LayoutPrintScope(folderId, "Folder", [], false)
            : new LayoutPrintScope(
                folderId,
                folder.Label,
                CollectSheets(folder.Children),
                true);
    }

    private static OverviewTreeNode? FindFolder(
        IEnumerable<OverviewTreeNode> nodes,
        Guid folderId)
    {
        foreach (var node in nodes)
        {
            if (node.Key is { Kind: OverviewNodeKind.Folder, Id: var id } && id == folderId)
            {
                return node;
            }

            var descendant = FindFolder(node.Children, folderId);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static IReadOnlyList<Guid> CollectSheets(IEnumerable<OverviewTreeNode> nodes)
    {
        var sheetIds = new List<Guid>();
        foreach (var node in nodes)
        {
            if (node.Key.Kind == OverviewNodeKind.Sheet)
            {
                sheetIds.Add(node.Key.Id);
            }

            sheetIds.AddRange(CollectSheets(node.Children));
        }

        return sheetIds;
    }
}

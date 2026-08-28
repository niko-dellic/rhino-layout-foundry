namespace RhinoLayoutFoundry.Core.Overview;

public sealed record FolderCreationDestination(Guid ParentFolderId, string DisplayName)
{
    public static FolderCreationDestination? Resolve(
        DocumentOverview overview,
        IEnumerable<OverviewNodeKey> selectedKeys)
    {
        ArgumentNullException.ThrowIfNull(overview);
        ArgumentNullException.ThrowIfNull(selectedKeys);

        if (overview.RootFolderId is not { } rootFolderId)
        {
            return null;
        }

        var selected = selectedKeys.Distinct().Take(2).ToArray();
        if (selected.Length == 1)
        {
            var key = selected[0];
            var destinationId = key.Kind switch
            {
                OverviewNodeKind.Folder => key.Id,
                OverviewNodeKind.Sheet => overview.Sheets
                    .FirstOrDefault(sheet => sheet.PageViewId == key.Id)?.FolderId,
                OverviewNodeKind.Detail => overview.Sheets
                    .FirstOrDefault(sheet => sheet.Details.Any(detail =>
                        detail.DetailViewportId == key.Id))?.FolderId,
                _ => null,
            };
            var folder = destinationId is { } id
                ? overview.Folders.FirstOrDefault(item => item.Id == id)
                : null;
            if (folder is not null)
                return new FolderCreationDestination(folder.Id, folder.Name);
        }

        return new FolderCreationDestination(rootFolderId, "Root");
    }
}

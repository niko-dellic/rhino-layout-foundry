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
        if (selected is [{ Kind: OverviewNodeKind.Folder } folderKey])
        {
            var folder = overview.Folders.FirstOrDefault(item => item.Id == folderKey.Id);
            if (folder is not null && folder.Id != rootFolderId)
            {
                return new FolderCreationDestination(folder.Id, folder.Name);
            }
        }

        return new FolderCreationDestination(rootFolderId, "Root");
    }
}

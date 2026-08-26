using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Hierarchy;

public sealed class HierarchyIndex
{
    private readonly DocumentSnapshot _snapshot;
    private readonly IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> _childFolders;
    private readonly IReadOnlyDictionary<Guid, Guid> _detailOwners;

    public HierarchyIndex(DocumentSnapshot snapshot)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        ValidateRoot();
        ValidateFolders();
        _detailOwners = ValidateSheetsAndBuildDetailOwners();
        _childFolders = BuildChildFolders();
    }

    public bool TryResolveDetails(HierarchySelector selector, out IReadOnlyList<Guid> detailIds)
    {
        switch (selector.Kind)
        {
            case HierarchySelectorKind.Folder when _snapshot.Folders.ContainsKey(selector.Id):
                detailIds = ResolveFolderDetails(selector.Id);
                return true;
            case HierarchySelectorKind.Sheet when _snapshot.Sheets.TryGetValue(selector.Id, out var sheet):
                detailIds = sheet.DetailIds;
                return true;
            case HierarchySelectorKind.Detail when _detailOwners.ContainsKey(selector.Id):
                detailIds = [selector.Id];
                return true;
            default:
                detailIds = [];
                return false;
        }
    }

    public IReadOnlyList<Guid> ResolveDetails(IEnumerable<HierarchySelector> selectors)
    {
        ArgumentNullException.ThrowIfNull(selectors);

        var resolved = new HashSet<Guid>();
        foreach (var selector in selectors)
        {
            if (!TryResolveDetails(selector, out var detailIds))
            {
                throw new KeyNotFoundException($"The {selector.Kind} selector '{selector.Id}' does not exist.");
            }

            resolved.UnionWith(detailIds);
        }

        return resolved.Order().ToArray();
    }

    private void ValidateRoot()
    {
        if (!_snapshot.Folders.TryGetValue(_snapshot.RootFolderId, out var root))
        {
            throw new ArgumentException("The root folder does not exist in the snapshot.", nameof(_snapshot));
        }

        if (root.ParentId is not null)
        {
            throw new ArgumentException("The root folder cannot have a parent.", nameof(_snapshot));
        }
    }

    private void ValidateFolders()
    {
        foreach (var folder in _snapshot.Folders.Values)
        {
            if (folder.Id == Guid.Empty)
            {
                throw new ArgumentException("Folder IDs cannot be empty.", nameof(_snapshot));
            }

            if (folder.Id != _snapshot.RootFolderId && folder.ParentId is null)
            {
                throw new ArgumentException($"Folder '{folder.Id}' must have a parent.", nameof(_snapshot));
            }

            if (folder.ParentId is { } parentId && !_snapshot.Folders.ContainsKey(parentId))
            {
                throw new ArgumentException($"Folder '{folder.Id}' references missing parent '{parentId}'.", nameof(_snapshot));
            }

            var visited = new HashSet<Guid>();
            var current = folder;
            while (current.ParentId is { } ancestorId)
            {
                if (!visited.Add(current.Id))
                {
                    throw new ArgumentException($"Folder '{folder.Id}' participates in a cycle.", nameof(_snapshot));
                }

                current = _snapshot.Folders[ancestorId];
            }
        }
    }

    private IReadOnlyDictionary<Guid, Guid> ValidateSheetsAndBuildDetailOwners()
    {
        var owners = new Dictionary<Guid, Guid>();

        foreach (var sheet in _snapshot.Sheets.Values)
        {
            if (sheet.PageViewId == Guid.Empty)
            {
                throw new ArgumentException("Sheet IDs cannot be empty.", nameof(_snapshot));
            }

            if (!_snapshot.Folders.ContainsKey(sheet.FolderId))
            {
                throw new ArgumentException(
                    $"Sheet '{sheet.PageViewId}' references missing folder '{sheet.FolderId}'.",
                    nameof(_snapshot));
            }

            foreach (var detailId in sheet.DetailIds)
            {
                if (detailId == Guid.Empty)
                {
                    throw new ArgumentException("Detail IDs cannot be empty.", nameof(_snapshot));
                }

                if (!owners.TryAdd(detailId, sheet.PageViewId))
                {
                    throw new ArgumentException($"Detail '{detailId}' belongs to more than one sheet.", nameof(_snapshot));
                }
            }
        }

        return owners;
    }

    private IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> BuildChildFolders()
    {
        return _snapshot.Folders.Values
            .Where(folder => folder.ParentId is not null)
            .GroupBy(folder => folder.ParentId!.Value)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<Guid>)group
                    .OrderBy(folder => folder.Order)
                    .ThenBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(folder => folder.Id)
                    .ToArray());
    }

    private IReadOnlyList<Guid> ResolveFolderDetails(Guid folderId)
    {
        var folderIds = new HashSet<Guid>();
        var pending = new Stack<Guid>();
        pending.Push(folderId);

        while (pending.TryPop(out var current))
        {
            if (!folderIds.Add(current))
            {
                continue;
            }

            if (_childFolders.TryGetValue(current, out var children))
            {
                foreach (var child in children)
                {
                    pending.Push(child);
                }
            }
        }

        return _snapshot.Sheets.Values
            .Where(sheet => folderIds.Contains(sheet.FolderId))
            .OrderBy(sheet => sheet.Order)
            .ThenBy(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase)
            .SelectMany(sheet => sheet.DetailIds)
            .Distinct()
            .ToArray();
    }
}


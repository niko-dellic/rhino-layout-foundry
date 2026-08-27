using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Operations;

public static class BatchTargetResolver
{
    public static IReadOnlyList<BatchTarget> Resolve(
        DocumentSnapshot snapshot,
        IEnumerable<OverviewNodeKey> selection)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(selection);
        var sheetIds = ResolveSheetIds(snapshot, selection).ToHashSet();

        return snapshot.Sheets.Values
            .Where(sheet => sheetIds.Contains(sheet.PageViewId))
            .OrderBy(sheet => sheet.FolderId)
            .ThenBy(sheet => sheet.Order)
            .ThenBy(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase)
            .Select(sheet => new BatchTarget(
                new OverviewNodeKey(OverviewNodeKind.Sheet, sheet.PageViewId),
                sheet.Name,
                true,
                sheet.DetailIds.Count,
                sheet.PageWidth,
                sheet.PageHeight,
                sheet.PageUnitSystem,
                DisplayModes(sheet),
                string.IsNullOrWhiteSpace(sheet.TitleBlockDefinitionName)
                    ? "—"
                    : sheet.TitleBlockDefinitionName))
            .ToArray();
    }

    public static IReadOnlyList<Guid> ResolveSheetIds(
        DocumentSnapshot snapshot,
        IEnumerable<OverviewNodeKey> selection)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(selection);
        var sheetIds = new HashSet<Guid>();
        foreach (var key in selection.Distinct())
        {
            switch (key.Kind)
            {
                case OverviewNodeKind.Sheet:
                    sheetIds.Add(key.Id);
                    break;
                case OverviewNodeKind.Detail:
                    var owner = snapshot.Sheets.Values.FirstOrDefault(sheet => sheet.DetailIds.Contains(key.Id));
                    if (owner is not null) sheetIds.Add(owner.PageViewId);
                    break;
                case OverviewNodeKind.Folder:
                    var folders = Descendants(snapshot, key.Id);
                    foreach (var sheet in snapshot.Sheets.Values.Where(sheet => folders.Contains(sheet.FolderId)))
                        sheetIds.Add(sheet.PageViewId);
                    break;
            }
        }

        return snapshot.Sheets.Values
            .Where(sheet => sheetIds.Contains(sheet.PageViewId))
            .OrderBy(sheet => sheet.FolderId)
            .ThenBy(sheet => sheet.Order)
            .ThenBy(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase)
            .Select(sheet => sheet.PageViewId)
            .ToArray();
    }

    public static IReadOnlyList<Guid> ResolveDetailIds(
        DocumentSnapshot snapshot,
        IEnumerable<OverviewNodeKey> selection)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(selection);
        var keys = selection.Distinct().ToArray();
        var sheetIds = ResolveSheetIds(snapshot,
            keys.Where(key => key.Kind != OverviewNodeKind.Detail)).ToHashSet();
        var detailIds = snapshot.Sheets.Values
            .Where(sheet => sheetIds.Contains(sheet.PageViewId))
            .SelectMany(sheet => sheet.DetailIds)
            .ToHashSet();
        var existing = snapshot.Sheets.Values.SelectMany(sheet => sheet.DetailIds).ToHashSet();
        foreach (var detail in keys.Where(key => key.Kind == OverviewNodeKind.Detail && existing.Contains(key.Id)))
            detailIds.Add(detail.Id);
        return detailIds.OrderBy(id => id).ToArray();
    }

    private static HashSet<Guid> Descendants(DocumentSnapshot snapshot, Guid root)
    {
        var result = new HashSet<Guid> { root };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var folder in snapshot.Folders.Values.Where(folder =>
                         folder.ParentId is { } parent && result.Contains(parent)))
                changed |= result.Add(folder.Id);
        }
        return result;
    }

    private static string DisplayModes(SheetSnapshot sheet)
    {
        var names = sheet.Details.Select(detail => detail.DisplayModeName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return names.Length switch
        {
            0 => "—",
            1 => names[0],
            _ => "Mixed",
        };
    }
}

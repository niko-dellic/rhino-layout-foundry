using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Overview;

public sealed record NavigatorDropRow(
    OverviewNodeKey Key,
    Guid ParentFolderId,
    double Top,
    double Height);

public sealed record NavigatorDropResolution(
    bool IsValid,
    HierarchyPlacementTarget? Target,
    Guid? HighlightFolderId,
    double? InsertionLineY,
    string? RejectionReason = null)
{
    public static NavigatorDropResolution Invalid(string reason) =>
        new(false, null, null, null, reason);
}

/// <summary>Pure row-zone resolver used by the custom-drawn Canvas navigator.</summary>
public sealed class NavigatorDropResolver
{
    private const double EdgeZoneRatio = 0.25;

    public NavigatorDropResolution Resolve(
        IReadOnlyList<NavigatorDropRow> visibleRows,
        double pointerY,
        double navigatorTop,
        double navigatorBottom,
        IReadOnlyCollection<OverviewNodeKind> movingKinds,
        Guid rootFolderId)
    {
        ArgumentNullException.ThrowIfNull(visibleRows);
        ArgumentNullException.ThrowIfNull(movingKinds);

        if (pointerY < navigatorTop || pointerY > navigatorBottom)
            return NavigatorDropResolution.Invalid("The pointer is outside the navigator.");

        var kinds = movingKinds.Distinct().ToArray();
        if (kinds.Length == 0 || kinds.Any(kind => kind is not (
                OverviewNodeKind.Folder or OverviewNodeKind.Sheet or OverviewNodeKind.AppearanceState)))
            return NavigatorDropResolution.Invalid("Only folders, layouts, and appearance states can be reorganized.");
        if (kinds.Contains(OverviewNodeKind.AppearanceState) && kinds.Length > 1)
            return NavigatorDropResolution.Invalid("Appearance states must be moved separately from folders and layouts.");

        var row = visibleRows.FirstOrDefault(candidate =>
            pointerY >= candidate.Top && pointerY <= candidate.Top + candidate.Height);
        if (row is null)
        {
            return IntoFolder(rootFolderId);
        }

        if (row.Key.Kind == OverviewNodeKind.Folder && row.Key.Id == rootFolderId)
            return IntoFolder(rootFolderId);

        if (row.Key.Kind == OverviewNodeKind.Detail)
            return NavigatorDropResolution.Invalid("Details remain attached to their layout.");

        if (kinds[0] == OverviewNodeKind.AppearanceState)
        {
            var localYForState = pointerY - row.Top;
            var edgeForState = Math.Max(2, row.Height * EdgeZoneRatio);
            return row.Key.Kind == OverviewNodeKind.Folder &&
                   localYForState > edgeForState && localYForState < row.Height - edgeForState
                ? IntoFolder(row.Key.Id)
                : IntoFolder(row.ParentFolderId == Guid.Empty ? rootFolderId : row.ParentFolderId);
        }

        var mixed = kinds.Length > 1;
        var localY = pointerY - row.Top;
        var edge = Math.Max(2, row.Height * EdgeZoneRatio);
        var before = localY <= edge;
        var after = localY >= row.Height - edge;

        if (row.Key.Kind == OverviewNodeKind.Folder && !before && !after)
            return IntoFolder(row.Key.Id);

        if (mixed)
            return NavigatorDropResolution.Invalid(
                "Mixed folder and layout selections can only be dropped into a folder or the document root.");

        var movingKind = kinds[0];
        if (movingKind != row.Key.Kind)
            return NavigatorDropResolution.Invalid("Folders and layouts are ordered independently.");

        var placement = before ? HierarchyPlacementKind.BeforeSibling : HierarchyPlacementKind.AfterSibling;
        return new NavigatorDropResolution(
            true,
            new HierarchyPlacementTarget(placement, row.Key.Kind, row.Key.Id),
            null,
            before ? row.Top : row.Top + row.Height);
    }

    private static NavigatorDropResolution IntoFolder(Guid folderId) =>
        new(
            true,
            new HierarchyPlacementTarget(HierarchyPlacementKind.IntoFolder, OverviewNodeKind.Folder, folderId),
            folderId,
            null);
}

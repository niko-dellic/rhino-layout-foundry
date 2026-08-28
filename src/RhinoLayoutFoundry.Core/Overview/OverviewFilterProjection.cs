namespace RhinoLayoutFoundry.Core.Overview;

/// <summary>
/// A view-neutral projection of the shared overview filter. Hierarchy views can
/// use the filtered tree directly, thumbnail views use matching sheet IDs, and
/// spatial views use emphasized keys without removing board items.
/// </summary>
public sealed record OverviewFilterProjection(
    bool IsActive,
    IReadOnlySet<OverviewNodeKey> EmphasizedKeys,
    IReadOnlySet<Guid> MatchingSheetIds)
{
    public bool Emphasizes(OverviewNodeKey key) => !IsActive || EmphasizedKeys.Contains(key);

    public bool MatchesSheet(Guid pageViewId) => !IsActive || MatchingSheetIds.Contains(pageViewId);
}

public static class OverviewFilterProjector
{
    public static OverviewFilterProjection Resolve(
        DocumentOverview overview,
        OverviewTreeFilter filter)
    {
        ArgumentNullException.ThrowIfNull(overview);

        var nodes = OverviewTreeBuilder.Build(overview, filter);
        var keys = Flatten(nodes).Select(node => node.Key).ToHashSet();
        var sheetIds = keys
            .Where(key => key.Kind == OverviewNodeKind.Sheet)
            .Select(key => key.Id)
            .ToHashSet();
        return new OverviewFilterProjection(filter.IsActive, keys, sheetIds);
    }

    private static IEnumerable<OverviewTreeNode> Flatten(IEnumerable<OverviewTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
                yield return child;
        }
    }
}

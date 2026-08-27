namespace RhinoLayoutFoundry.Core.Overview;

public static class VisibleTreeRows
{
    public static IEnumerable<T> Flatten<T>(
        IEnumerable<T> roots,
        Func<T, IEnumerable<T>> children,
        Func<T, bool> isExpanded)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(children);
        ArgumentNullException.ThrowIfNull(isExpanded);

        foreach (var item in roots)
        {
            yield return item;
            if (!isExpanded(item))
            {
                continue;
            }

            foreach (var child in Flatten(children(item), children, isExpanded))
            {
                yield return child;
            }
        }
    }
}

namespace RhinoLayoutFoundry.Core.Overview;

public enum OverviewSortProperty
{
    None,
    Name,
    Print,
    Template,
    PaperSize,
    DetailCount,
    DisplayMode,
    Status,
}

public enum OverviewSortDirection
{
    Ascending,
    Descending,
}

public static class OverviewTreeSorter
{
    public static IReadOnlyList<OverviewTreeNode> Sort(
        IEnumerable<OverviewTreeNode> roots,
        OverviewSortProperty property,
        OverviewSortDirection direction)
    {
        ArgumentNullException.ThrowIfNull(roots);
        return roots.Select(root => SortNode(root, property, direction)).ToArray();
    }

    private static OverviewTreeNode SortNode(
        OverviewTreeNode node,
        OverviewSortProperty property,
        OverviewSortDirection direction)
    {
        var children = node.Children.Select(child => SortNode(child, property, direction));
        if (property != OverviewSortProperty.None)
        {
            children = direction == OverviewSortDirection.Ascending
                ? children.OrderBy(child => SortValue(child, property), NaturalValueComparer.Instance)
                    .ThenBy(child => child.Label, StringComparer.OrdinalIgnoreCase)
                : children.OrderByDescending(child => SortValue(child, property), NaturalValueComparer.Instance)
                    .ThenBy(child => child.Label, StringComparer.OrdinalIgnoreCase);
        }

        return node with { Children = children.ToArray() };
    }

    private static object SortValue(OverviewTreeNode node, OverviewSortProperty property)
    {
        var sheets = DescendantSheets(node).ToArray();
        var details = DescendantDetails(node).ToArray();
        return property switch
        {
            OverviewSortProperty.Name => node.Label,
            OverviewSortProperty.Print => PrintRank(sheets),
            OverviewSortProperty.Template => TemplateRank(sheets),
            OverviewSortProperty.PaperSize => PaperValue(sheets),
            OverviewSortProperty.DetailCount => details.Length,
            OverviewSortProperty.DisplayMode => Summary(details.Select(detail => detail.DisplayModeName)),
            OverviewSortProperty.Status => node.StatusText,
            _ => string.Empty,
        };
    }

    private static int PrintRank(IReadOnlyList<SheetOverview> sheets)
    {
        if (sheets.Count == 0) return 3;
        if (sheets.All(sheet => sheet.IncludeInPrintAll)) return 0;
        if (sheets.All(sheet => !sheet.IncludeInPrintAll)) return 2;
        return 1;
    }

    private static int TemplateRank(IReadOnlyList<SheetOverview> sheets)
    {
        if (sheets.Count == 0) return 2;
        return sheets.All(sheet => sheet.IsTemplate) ? 0 : 1;
    }

    private static double PaperValue(IReadOnlyList<SheetOverview> sheets)
    {
        if (sheets.Count == 0) return double.MaxValue;
        var values = sheets.Select(sheet => sheet.PageWidth * sheet.PageHeight).Distinct().Take(2).ToArray();
        return values.Length == 1 ? values[0] : double.MaxValue - 1;
    }

    private static string Summary(IEnumerable<string> values)
    {
        var distinct = values.Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
        return distinct.Length switch { 0 => "~", 1 => distinct[0], _ => "Mixed" };
    }

    private static IEnumerable<SheetOverview> DescendantSheets(OverviewTreeNode node)
    {
        if (node.Sheet is not null) yield return node.Sheet;
        foreach (var child in node.Children)
        foreach (var sheet in DescendantSheets(child))
            yield return sheet;
    }

    private static IEnumerable<DetailOverview> DescendantDetails(OverviewTreeNode node)
    {
        if (node.Sheet is not null)
        {
            foreach (var detail in node.Sheet.Details) yield return detail;
            yield break;
        }
        if (node.Detail is not null)
        {
            yield return node.Detail;
            yield break;
        }
        foreach (var child in node.Children)
        foreach (var detail in DescendantDetails(child))
            yield return detail;
    }

    private sealed class NaturalValueComparer : IComparer<object>
    {
        public static NaturalValueComparer Instance { get; } = new();

        public int Compare(object? left, object? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            if (left is int leftInt && right is int rightInt) return leftInt.CompareTo(rightInt);
            if (left is double leftDouble && right is double rightDouble) return leftDouble.CompareTo(rightDouble);
            return CompareNatural(left.ToString() ?? string.Empty, right.ToString() ?? string.Empty);
        }

        private static int CompareNatural(string left, string right)
        {
            var leftIndex = 0;
            var rightIndex = 0;
            while (leftIndex < left.Length && rightIndex < right.Length)
            {
                if (char.IsDigit(left[leftIndex]) && char.IsDigit(right[rightIndex]))
                {
                    var leftEnd = leftIndex;
                    while (leftEnd < left.Length && char.IsDigit(left[leftEnd])) leftEnd++;
                    var rightEnd = rightIndex;
                    while (rightEnd < right.Length && char.IsDigit(right[rightEnd])) rightEnd++;
                    var leftSignificant = leftIndex;
                    while (leftSignificant < leftEnd - 1 && left[leftSignificant] == '0') leftSignificant++;
                    var rightSignificant = rightIndex;
                    while (rightSignificant < rightEnd - 1 && right[rightSignificant] == '0') rightSignificant++;
                    var lengthComparison = (leftEnd - leftSignificant).CompareTo(rightEnd - rightSignificant);
                    if (lengthComparison != 0) return lengthComparison;
                    for (var index = 0; index < leftEnd - leftSignificant; index++)
                    {
                        var digitComparison = left[leftSignificant + index].CompareTo(right[rightSignificant + index]);
                        if (digitComparison != 0) return digitComparison;
                    }
                    leftIndex = leftEnd;
                    rightIndex = rightEnd;
                    continue;
                }

                var characterComparison = char.ToUpperInvariant(left[leftIndex])
                    .CompareTo(char.ToUpperInvariant(right[rightIndex]));
                if (characterComparison != 0) return characterComparison;
                leftIndex++;
                rightIndex++;
            }

            return (left.Length - leftIndex).CompareTo(right.Length - rightIndex);
        }
    }
}

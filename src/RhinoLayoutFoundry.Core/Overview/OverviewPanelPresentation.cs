namespace RhinoLayoutFoundry.Core.Overview;

public enum OverviewContentState
{
    NoDocument,
    EmptyDocument,
    NoMatches,
    Hierarchy,
}

public sealed record OverviewPanelPresentation(
    string DocumentSummary,
    string ResultSummary,
    OverviewContentState ContentState,
    string EmptyTitle,
    string EmptyDescription,
    string SelectionSummary)
{
    public static OverviewPanelPresentation Create(
        DocumentOverview overview,
        OverviewTreeFilter filter,
        IEnumerable<OverviewNodeKey> selectedKeys)
    {
        ArgumentNullException.ThrowIfNull(overview);
        ArgumentNullException.ThrowIfNull(selectedKeys);

        var sheetCount = overview.Sheets.Count;
        var detailCount = overview.Sheets.Sum(sheet => sheet.DetailCount);
        var documentSummary = overview.DocumentRuntimeSerialNumber is null
            ? "Open or create a model to begin"
            : $"{Pluralize(sheetCount, "sheet")} · {Pluralize(detailCount, "detail")}";
        var visibleTree = OverviewTreeBuilder.Build(overview, filter);
        var visibleSheets = Flatten(visibleTree).Count(node => node.Key.Kind == OverviewNodeKind.Sheet);
        var visibleDetails = Flatten(visibleTree).Count(node => node.Key.Kind == OverviewNodeKind.Detail);
        var resultSummary = filter.IsActive
            ? $"Showing {Pluralize(visibleSheets, "sheet")} · {Pluralize(visibleDetails, "detail")}"
            : string.Empty;

        var contentState = overview.DocumentRuntimeSerialNumber is null
            ? OverviewContentState.NoDocument
            : sheetCount == 0
                ? OverviewContentState.EmptyDocument
                : filter.IsActive && visibleTree.Count == 0
                    ? OverviewContentState.NoMatches
                    : OverviewContentState.Hierarchy;
        var (emptyTitle, emptyDescription) = contentState switch
        {
            OverviewContentState.NoDocument => (
                "No active document",
                "Open or create a Rhino model and Layout Foundry will follow it automatically."),
            OverviewContentState.EmptyDocument => (
                "No layout sheets yet",
                "Create a Rhino layout, then return here to organize and manage the drawing set."),
            OverviewContentState.NoMatches => (
                "No matching layouts",
                "Try another search or change the row filter."),
            _ => (string.Empty, string.Empty),
        };

        return new OverviewPanelPresentation(
            documentSummary,
            resultSummary,
            contentState,
            emptyTitle,
            emptyDescription,
            OverviewSelectionSummary.Create(selectedKeys).DisplayText);
    }

    private static IEnumerable<OverviewTreeNode> Flatten(IEnumerable<OverviewTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    private static string Pluralize(int count, string singular)
    {
        return $"{count} {singular}{(count == 1 ? string.Empty : "s")}";
    }
}

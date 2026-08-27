namespace RhinoLayoutFoundry.Core.Overview;

public static class OverviewHierarchyHeader
{
    public static string Create(DocumentOverview overview, string? contextualSummary = null)
    {
        ArgumentNullException.ThrowIfNull(overview);
        if (overview.DocumentRuntimeSerialNumber is null)
        {
            return "Layouts";
        }

        var sheetCount = overview.Sheets.Count;
        var detailCount = overview.Sheets.Sum(sheet => sheet.DetailCount);
        var counts = $"Layouts  ·  {Pluralize(sheetCount, "sheet")}  ·  {Pluralize(detailCount, "detail")}";
        return string.IsNullOrWhiteSpace(contextualSummary)
            ? counts
            : $"{counts}  ·  {contextualSummary.Trim()}";
    }

    private static string Pluralize(int count, string singular)
    {
        return $"{count} {singular}{(count == 1 ? string.Empty : "s")}";
    }
}

namespace RhinoLayoutFoundry.Core.Overview;

public enum OverviewIssueSeverity
{
    Information,
    Warning,
    Error,
}

public sealed record OverviewIssue(
    string Code,
    OverviewIssueSeverity Severity,
    string Message,
    Guid? EntityId = null);

public static class OverviewDiagnostics
{
    public static IReadOnlyList<OverviewIssue> ForSheet(
        SheetOverview sheet,
        bool assignedFolderExists,
        bool hasDuplicateName)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var issues = new List<OverviewIssue>();
        if (!assignedFolderExists)
        {
            issues.Add(new OverviewIssue(
                "sheet.folder_missing",
                OverviewIssueSeverity.Warning,
                "The saved folder no longer exists; this sheet is shown at the top level.",
                sheet.PageViewId));
        }

        if (hasDuplicateName)
        {
            issues.Add(new OverviewIssue(
                "sheet.name_duplicate",
                OverviewIssueSeverity.Warning,
                "Another layout sheet has the same name.",
                sheet.PageViewId));
        }

        if (sheet.DetailCount == 0)
        {
            issues.Add(new OverviewIssue(
                "sheet.details_empty",
                OverviewIssueSeverity.Information,
                "This sheet has no detail viewports.",
                sheet.PageViewId));
        }

        return issues;
    }

    public static string Badge(IReadOnlyList<OverviewIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        if (issues.Count == 0)
        {
            return string.Empty;
        }

        var highest = issues.Max(issue => issue.Severity);
        var prefix = highest switch
        {
            OverviewIssueSeverity.Error => "Error",
            OverviewIssueSeverity.Warning => "Warning",
            _ => "Info",
        };
        return $"{prefix} · {issues.Count}";
    }
}

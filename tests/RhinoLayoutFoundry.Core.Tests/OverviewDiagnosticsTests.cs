using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class OverviewDiagnosticsTests
{
    [Fact]
    public void MissingFolderAndDuplicateNameRemainDiagnosable()
    {
        var sheet = TestSnapshots.Overview(1, 1).Sheets[0];

        var issues = OverviewDiagnostics.ForSheet(
            sheet,
            assignedFolderExists: false,
            hasDuplicateName: true);

        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, issue => issue.Code == "sheet.folder_missing");
        Assert.Contains(issues, issue => issue.Code == "sheet.name_duplicate");
        Assert.Equal("Warning · 2", OverviewDiagnostics.Badge(issues));
    }

    [Fact]
    public void EmptySheetProducesInformationBadge()
    {
        var sheet = TestSnapshots.Overview(1, 0).Sheets[0];

        var issues = OverviewDiagnostics.ForSheet(sheet, true, false);

        Assert.Single(issues);
        Assert.Equal("Info · 1", OverviewDiagnostics.Badge(issues));
    }
}

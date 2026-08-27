namespace RhinoLayoutFoundry.Core.Overview;

public sealed record DocumentOverview(
    uint? DocumentRuntimeSerialNumber,
    string DocumentName,
    Guid? RootFolderId,
    IReadOnlyList<FolderOverview> Folders,
    IReadOnlyList<SheetOverview> Sheets,
    IReadOnlyList<OverviewIssue>? Diagnostics = null)
{
    public static DocumentOverview NoDocument { get; } = new(
        null,
        "No active document",
        null,
        [],
        [],
        []);

    public IReadOnlyList<OverviewIssue> Issues => Diagnostics ?? [];
}

public sealed record FolderOverview(
    Guid Id,
    Guid? ParentId,
    string Name,
    int Order);

public sealed record SheetOverview(
    Guid PageViewId,
    Guid FolderId,
    string Name,
    int Order,
    IReadOnlyList<string> Tags,
    IReadOnlyList<DetailOverview> Details,
    IReadOnlyList<OverviewIssue>? Diagnostics = null,
    double PageWidth = 0,
    double PageHeight = 0,
    string PageUnitSystem = "",
    bool IncludeInPrintAll = true,
    bool IsTemplate = false)
{
    public int DetailCount => Details.Count;

    public IReadOnlyList<OverviewIssue> Issues => Diagnostics ?? [];

    public string DisplayLabel => $"{Name}  ·  {DetailCount} detail{(DetailCount == 1 ? string.Empty : "s")}";
}

public sealed record DetailOverview(
    Guid DetailViewportId,
    string Name,
    int Order,
    Guid DisplayModeId = default,
    string DisplayModeName = "");

public readonly record struct DocumentOverviewIdentity(
    uint? DocumentRuntimeSerialNumber,
    int SheetCount);

public interface IDocumentOverviewProvider
{
    DocumentOverview Capture();

    DocumentOverviewIdentity CaptureIdentity();
}

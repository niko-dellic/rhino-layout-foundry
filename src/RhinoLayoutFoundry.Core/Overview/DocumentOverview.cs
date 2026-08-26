namespace RhinoLayoutFoundry.Core.Overview;

public sealed record DocumentOverview(
    uint? DocumentRuntimeSerialNumber,
    string DocumentName,
    Guid? RootFolderId,
    IReadOnlyList<FolderOverview> Folders,
    IReadOnlyList<SheetOverview> Sheets)
{
    public static DocumentOverview NoDocument { get; } = new(
        null,
        "No active document",
        null,
        [],
        []);
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
    IReadOnlyList<DetailOverview> Details)
{
    public int DetailCount => Details.Count;

    public string DisplayLabel => $"{Name}  ·  {DetailCount} detail{(DetailCount == 1 ? string.Empty : "s")}";
}

public sealed record DetailOverview(
    Guid DetailViewportId,
    string Name,
    int Order);

public interface IDocumentOverviewProvider
{
    DocumentOverview Capture();
}

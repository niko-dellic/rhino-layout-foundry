using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Overview;

public sealed record DocumentOverview(
    uint? DocumentRuntimeSerialNumber,
    string DocumentName,
    Guid? RootFolderId,
    IReadOnlyList<FolderOverview> Folders,
    IReadOnlyList<SheetOverview> Sheets,
    IReadOnlyList<OverviewIssue>? Diagnostics = null,
    IReadOnlyList<AppearanceStateOverview>? AppearanceStateResources = null)
{
    public static DocumentOverview NoDocument { get; } = new(
        null,
        "No active document",
        null,
        [],
        [],
        []);

    public IReadOnlyList<OverviewIssue> Issues => Diagnostics ?? [];
    public IReadOnlyList<AppearanceStateOverview> AppearanceStates => AppearanceStateResources ?? [];
}

public sealed record FolderOverview(
    Guid Id,
    Guid? ParentId,
    string Name,
    int Order,
    TemplateCapability TemplateCapabilities = TemplateCapability.None,
    ViewportAppearanceSummary? Appearance = null,
    AppearanceStateBindingOverview? AppearanceState = null,
    string Notes = "");

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
    bool IsTemplate = false,
    TemplateCapability TemplateCapabilities = TemplateCapability.None,
    ViewportAppearanceSummary? Appearance = null,
    AppearanceStateBindingOverview? AppearanceState = null,
    string Notes = "")
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
    string DisplayModeName = "",
    TemplateCapability TemplateCapabilities = TemplateCapability.None,
    ViewportAppearanceSummary? Appearance = null,
    AppearanceStateBindingOverview? AppearanceState = null);

public sealed record AppearanceStateBindingOverview(
    Guid StateId,
    string Name,
    bool IsInherited,
    HierarchyScope AssignedAt);

public sealed record AppearanceStateOverview(
    Guid Id,
    Guid FolderId,
    int Order,
    string Name,
    int RuleCount,
    int DirectAssignmentCount,
    int DependentFolderCount,
    int DependentSheetCount,
    int DependentDetailCount);

public sealed record ViewportAppearanceSummary(
    int VisibleLayerCount,
    int HiddenLayerCount,
    int ObjectDisplayOverrideCount,
    bool IsInherited,
    bool IsMixed = false,
    int UnresolvedCount = 0);

public readonly record struct DocumentOverviewIdentity(
    uint? DocumentRuntimeSerialNumber,
    int SheetCount,
    string DocumentName)
{
    public bool Matches(DocumentOverview overview) =>
        DocumentRuntimeSerialNumber == overview.DocumentRuntimeSerialNumber &&
        SheetCount == overview.Sheets.Count &&
        string.Equals(DocumentName, overview.DocumentName, StringComparison.Ordinal);
}

public interface IDocumentOverviewProvider
{
    DocumentOverview Capture();

    DocumentOverviewIdentity CaptureIdentity();
}

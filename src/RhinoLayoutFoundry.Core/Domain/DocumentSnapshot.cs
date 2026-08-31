namespace RhinoLayoutFoundry.Core.Domain;

public sealed record DocumentSnapshot(
    uint DocumentRuntimeSerialNumber,
    long Revision,
    Guid RootFolderId,
    IReadOnlyDictionary<Guid, FolderRecord> Folders,
    IReadOnlyDictionary<Guid, SheetSnapshot> Sheets,
    IReadOnlySet<Guid> ExistingObjectIds,
    IReadOnlySet<Guid> DisplayModeIds,
    IReadOnlyList<SheetTemplateRecipe>? SheetTemplates = null,
    IReadOnlyDictionary<string, string>? DocumentMetadata = null,
    IReadOnlySet<string>? NamedViewNames = null,
    IReadOnlySet<Guid>? InstanceDefinitionIds = null,
    IReadOnlyDictionary<Guid, string>? DisplayModeNames = null,
    IReadOnlyDictionary<Guid, TitleBlockInstanceSnapshot>? TitleBlockInstanceChoices = null,
    ObserverCanvasState? ObserverCanvas = null,
    ProjectInformation? ProjectData = null,
    IReadOnlyDictionary<Guid, string>? LayerNames = null)
{
    public IReadOnlyList<SheetTemplateRecipe> Templates => SheetTemplates ?? [];
    public IReadOnlyDictionary<string, string> Metadata =>
        DocumentMetadata ?? new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlySet<string> NamedViews =>
        NamedViewNames ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<Guid> InstanceDefinitions => InstanceDefinitionIds ?? new HashSet<Guid>();
    public IReadOnlyDictionary<Guid, string> DisplayModes =>
        DisplayModeNames ?? new Dictionary<Guid, string>();
    public IReadOnlyDictionary<Guid, TitleBlockInstanceSnapshot> TitleBlockInstances =>
        TitleBlockInstanceChoices ?? new Dictionary<Guid, TitleBlockInstanceSnapshot>();
    public ObserverCanvasState Canvas => ObserverCanvas ?? ObserverCanvasState.Empty;
    public ProjectInformation ProjectInfo => ProjectData ?? ProjectInformation.Empty;
    public IReadOnlyDictionary<Guid, string> Layers => LayerNames ?? new Dictionary<Guid, string>();
}

public sealed record SheetSnapshot(
    Guid PageViewId,
    Guid FolderId,
    int Order,
    string Name,
    IReadOnlyList<Guid> DetailIds,
    IReadOnlyDictionary<string, string> Metadata,
    double PageWidth = 0,
    double PageHeight = 0,
    string PageUnitSystem = "",
    IReadOnlyList<DetailSnapshot>? DetailSettings = null,
    Guid? TitleBlockInstanceObjectId = null,
    string? TitleBlockDefinitionName = null,
    bool IncludeInPrintAll = true,
    SheetTitleBlockData? TitleBlockData = null,
    BuiltInTitleBlockKind? TitleBlockBuiltInKind = null)
{
    public IReadOnlyList<DetailSnapshot> Details => DetailSettings ?? [];
}

public sealed record DetailSnapshot(
    Guid DetailViewportId,
    string Name,
    Guid DisplayModeId,
    string DisplayModeName);

public sealed record TitleBlockInstanceSnapshot(
    Guid InstanceObjectId,
    Guid InstanceDefinitionId,
    string InstanceDefinitionName,
    Guid SourcePageViewId,
    string SourcePageName,
    IReadOnlyList<double>? Transform = null,
    string AnchorName = "Template");

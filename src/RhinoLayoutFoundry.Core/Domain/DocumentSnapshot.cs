namespace RhinoLayoutFoundry.Core.Domain;
/// <summary>Owned snapshot values for planning; absent optional resources use empty collections.</summary>
public sealed record DocumentSnapshot(
    uint DocumentRuntimeSerialNumber,
    long Revision,
    Guid RootFolderId,
    IReadOnlyDictionary<Guid, FolderRecord> Folders,
    IReadOnlyDictionary<Guid, SheetSnapshot> Sheets,
    IReadOnlySet<Guid> ExistingObjectIds,
    IReadOnlySet<Guid> DisplayModeIds)
{
    public IReadOnlyList<SheetTemplateRecipe> Templates { get; init; } = [];
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlySet<string> NamedViews { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<Guid, string> DisplayModes { get; init; } = new Dictionary<Guid, string>();
    public ObserverCanvasState Canvas { get; init; } = ObserverCanvasState.Empty;
    public ProjectInformation ProjectInfo { get; init; } = ProjectInformation.Empty;
    public IReadOnlyDictionary<Guid, string> Layers { get; init; } = new Dictionary<Guid, string>();
    public IReadOnlyDictionary<Guid, LayerSnapshot> LayerSnapshots { get; init; } = new Dictionary<Guid, LayerSnapshot>();
    public IReadOnlyDictionary<Guid, ModelObjectSnapshot> ModelObjects { get; init; } = new Dictionary<Guid, ModelObjectSnapshot>();
    public IReadOnlyList<DetailLayerVisibilitySnapshot> DetailLayers { get; init; } = [];
    public IReadOnlyList<DetailObjectDisplayOverrideSnapshot> ObjectOverrides { get; init; } = [];
    public IReadOnlyList<HierarchyViewportRuleSet> AppearanceRules { get; init; } = [];
    public IReadOnlyList<LayoutTemplateRegistration> TemplateRegistrations
    {
        get;
        init;
    } = [];
    public IReadOnlyList<AppearanceStateRecord> AppearanceStates { get; init; } = [];
    public IReadOnlyList<AppearanceStateAssignment> StateAssignments { get; init; } = [];
    public IReadOnlyList<NamedViewSnapshot> NamedViewSnapshots { get; init; } = [];
    public IReadOnlyList<ClippingPlaneSnapshot> ClippingPlanes { get; init; } = [];
    public IReadOnlyList<Guid> StandardViewports { get; init; } = [];
    public Guid? DedicatedDetailLayerId { get; init; }
    public ModelBoundsSnapshot? ModelBounds { get; init; }
    public Guid? ActiveViewportDisplayModeId { get; init; }
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
    BuiltInTitleBlockKind? TitleBlockBuiltInKind = null,
    SheetNamingBinding? NamingBinding = null,
    string Notes = "")
{
    public IReadOnlyList<DetailSnapshot> Details => DetailSettings ?? [];
    public IReadOnlyDictionary<Guid, string> DetailNamedViews { get; init; } = new Dictionary<Guid, string>();
}

public sealed record DetailSnapshot(
    Guid DetailViewportId,
    string Name,
    Guid DisplayModeId,
    string DisplayModeName,
    Guid? LayerId = null,
    DetailPageBounds? PageBounds = null);

public sealed record DetailPageBounds(
    double Left,
    double Bottom,
    double Right,
    double Top)
{
    public double CenterX => (Left + Right) / 2;
    public double CenterY => (Bottom + Top) / 2;
    public bool IsValid =>
        double.IsFinite(Left) &&
        double.IsFinite(Bottom) &&
        double.IsFinite(Right) &&
        double.IsFinite(Top) &&
        Right > Left &&
        Top > Bottom;
}

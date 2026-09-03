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
    IReadOnlyDictionary<Guid, string>? LayerNames = null,
    IReadOnlyDictionary<Guid, LayerSnapshot>? LayerSettings = null,
    IReadOnlyDictionary<Guid, ModelObjectSnapshot>? ModelObjectSettings = null,
    IReadOnlyList<DetailLayerVisibilitySnapshot>? DetailLayerVisibilities = null,
    IReadOnlyList<DetailObjectDisplayOverrideSnapshot>? DetailObjectDisplayOverrides = null,
    IReadOnlyList<HierarchyViewportRuleSet>? ViewportRuleSets = null,
    IReadOnlyList<CapabilityTemplateRegistration>? CapabilityTemplates = null,
    IReadOnlyList<CapabilityTemplateLink>? CapabilityLinks = null,
    IReadOnlyList<AppearanceStateRecord>? AppearanceStateResources = null,
    IReadOnlyList<AppearanceStateAssignment>? AppearanceStateAssignments = null,
    Guid? DedicatedDetailLayerId = null,
    ModelBoundsSnapshot? ModelBounds = null,
    IReadOnlyList<NamedViewSnapshot>? NamedViewDetails = null,
    IReadOnlyList<ClippingPlaneSnapshot>? ClippingPlaneDetails = null,
    IReadOnlyList<Guid>? StandardViewportIds = null)
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
    public IReadOnlyDictionary<Guid, LayerSnapshot> LayerSnapshots =>
        LayerSettings ?? new Dictionary<Guid, LayerSnapshot>();
    public IReadOnlyDictionary<Guid, ModelObjectSnapshot> ModelObjects =>
        ModelObjectSettings ?? new Dictionary<Guid, ModelObjectSnapshot>();
    public IReadOnlyList<DetailLayerVisibilitySnapshot> DetailLayers => DetailLayerVisibilities ?? [];
    public IReadOnlyList<DetailObjectDisplayOverrideSnapshot> ObjectOverrides =>
        DetailObjectDisplayOverrides ?? [];
    public IReadOnlyList<HierarchyViewportRuleSet> AppearanceRules => ViewportRuleSets ?? [];
    public IReadOnlyList<CapabilityTemplateRegistration> TemplateRegistrations => CapabilityTemplates ?? [];
    public IReadOnlyList<CapabilityTemplateLink> TemplateLinks => CapabilityLinks ?? [];
    public IReadOnlyList<AppearanceStateRecord> AppearanceStates => AppearanceStateResources ?? [];
    public IReadOnlyList<AppearanceStateAssignment> StateAssignments => AppearanceStateAssignments ?? [];
    public IReadOnlyList<NamedViewSnapshot> NamedViewSnapshots => NamedViewDetails ?? [];
    public IReadOnlyList<ClippingPlaneSnapshot> ClippingPlanes => ClippingPlaneDetails ?? [];
    public IReadOnlyList<Guid> StandardViewports => StandardViewportIds ?? [];
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
    IReadOnlyList<string>? SheetTags = null,
    SheetNamingBinding? NamingBinding = null,
    string Notes = "")
{
    public IReadOnlyList<DetailSnapshot> Details => DetailSettings ?? [];
    public IReadOnlyList<string> Tags => SheetTags ?? [];
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

public sealed record TitleBlockInstanceSnapshot(
    Guid InstanceObjectId,
    Guid InstanceDefinitionId,
    string InstanceDefinitionName,
    Guid SourcePageViewId,
    string SourcePageName,
    IReadOnlyList<double>? Transform = null,
    string AnchorName = "Template");

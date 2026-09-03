using System.Text.Json.Serialization;

namespace RhinoLayoutFoundry.Core.Domain;

public static class WellKnownIds
{
    public static readonly Guid UnorganizedFolderId = new("f3b9cf54-a8bf-43af-bbac-6575373199af");
}

public sealed record DocumentState(
    int SchemaVersion,
    Guid RootFolderId,
    IReadOnlyList<FolderRecord> Folders,
    IReadOnlyDictionary<Guid, SheetRecord> Sheets,
    IReadOnlyList<DisplayRule> DisplayRules,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyList<SheetTemplateRecipe>? SheetTemplates = null,
    ObserverCanvasState? ObserverCanvas = null,
    IReadOnlyList<ImportRecoveryRecord>? ImportRecovery = null,
    Guid? DedicatedDetailLayerId = null,
    ProjectInformation? ProjectData = null,
    IReadOnlyList<HierarchyViewportRuleSet>? ViewportRuleSets = null,
    IReadOnlyList<CapabilityTemplateRegistration>? CapabilityTemplates = null,
    IReadOnlyList<CapabilityTemplateLink>? CapabilityLinks = null,
    IReadOnlyList<AppearanceStateRecord>? AppearanceStateResources = null,
    IReadOnlyList<AppearanceStateAssignment>? AppearanceStateAssignments = null)
{
    public const int CurrentSchemaVersion = 15;

    [JsonIgnore]
    public IReadOnlyList<SheetTemplateRecipe> Templates => SheetTemplates ?? [];

    [JsonIgnore]
    public ObserverCanvasState Canvas => ObserverCanvas ?? ObserverCanvasState.Empty;

    [JsonIgnore]
    public IReadOnlyList<ImportRecoveryRecord> Recovery => ImportRecovery ?? [];

    [JsonIgnore]
    public ProjectInformation ProjectInfo => ProjectData ?? ProjectInformation.Empty;

    [JsonIgnore]
    public IReadOnlyList<HierarchyViewportRuleSet> AppearanceRules => ViewportRuleSets ?? [];

    [JsonIgnore]
    public IReadOnlyList<CapabilityTemplateRegistration> TemplateRegistrations => CapabilityTemplates ?? [];

    [JsonIgnore]
    public IReadOnlyList<CapabilityTemplateLink> TemplateLinks => CapabilityLinks ?? [];

    [JsonIgnore]
    public IReadOnlyList<AppearanceStateRecord> AppearanceStates => AppearanceStateResources ?? [];

    [JsonIgnore]
    public IReadOnlyList<AppearanceStateAssignment> StateAssignments => AppearanceStateAssignments ?? [];

    public DocumentState RemoveTemplatesForMissingSources(IReadOnlySet<Guid> existingPageViewIds)
    {
        ArgumentNullException.ThrowIfNull(existingPageViewIds);
        var retained = Templates
            .Where(template => template.SourcePageViewId is not { } sourceId ||
                               existingPageViewIds.Contains(sourceId))
            .ToArray();
        var registrations = TemplateRegistrations.Where(item =>
                item.Source.Kind != HierarchyScopeKind.Sheet || existingPageViewIds.Contains(item.Source.Id))
            .ToArray();
        var registrationIds = registrations.Select(item => item.Id).ToHashSet();
        var rules = AppearanceRules.Where(item =>
                item.Scope.Kind != HierarchyScopeKind.Sheet || existingPageViewIds.Contains(item.Scope.Id))
            .ToList();
        var links = TemplateLinks.Where(item =>
                registrationIds.Contains(item.SourceRegistrationId) &&
                (item.Target.Kind != HierarchyScopeKind.Sheet || existingPageViewIds.Contains(item.Target.Id)))
            .ToArray();
        var stateIds = AppearanceStates.Select(item => item.Id).ToHashSet();
        var assignments = StateAssignments.Where(item =>
                stateIds.Contains(item.StateId) &&
                (item.Target.Kind != HierarchyScopeKind.Sheet || existingPageViewIds.Contains(item.Target.Id)))
            .ToArray();
        return retained.Length == Templates.Count &&
               registrations.Length == TemplateRegistrations.Count &&
               links.Length == TemplateLinks.Count &&
               assignments.Length == StateAssignments.Count &&
               rules.Count == AppearanceRules.Count
            ? this
            : this with
            {
                SheetTemplates = retained,
                CapabilityTemplates = registrations,
                CapabilityLinks = links,
                ViewportRuleSets = rules.ToArray(),
                AppearanceStateAssignments = assignments,
            };
    }

    public static DocumentState Empty()
    {
        var root = new FolderRecord(WellKnownIds.UnorganizedFolderId, null, "Unorganized", 0);

        return new DocumentState(
            CurrentSchemaVersion,
            root.Id,
            [root],
            new Dictionary<Guid, SheetRecord>(),
            [],
            new Dictionary<string, string>(StringComparer.Ordinal),
            [],
            ObserverCanvasState.Empty,
            []);
    }
}

public sealed record ImportRecoveryRecord(
    string Kind,
    string Name,
    string Message,
    Guid? EntityId = null,
    IReadOnlyDictionary<string, string>? Data = null);

/// <summary>
/// Document-shared observer-board organization. Camera, selection, hover, and
/// rendered previews are deliberately session-only and never enter this state.
/// </summary>
public sealed record ObserverCanvasState(
    int LayoutAlgorithmVersion,
    IReadOnlyDictionary<Guid, ObserverPointRecord> FolderOrigins,
    IReadOnlyDictionary<Guid, ObserverPointRecord> SheetPlacements,
    IReadOnlyDictionary<Guid, ObserverPointRecord>? AppearanceStatePlacements = null)
{
    public const int CurrentLayoutAlgorithmVersion = 1;

    public static ObserverCanvasState Empty { get; } = new(
        CurrentLayoutAlgorithmVersion,
        new Dictionary<Guid, ObserverPointRecord>(),
        new Dictionary<Guid, ObserverPointRecord>(),
        new Dictionary<Guid, ObserverPointRecord>());

    [JsonIgnore]
    public IReadOnlyDictionary<Guid, ObserverPointRecord> StatePlacements =>
        AppearanceStatePlacements ?? new Dictionary<Guid, ObserverPointRecord>();
}

public readonly record struct ObserverPointRecord(double X, double Y);

public static class ObserverCanvasStateComparer
{
    public static bool ContentEquals(ObserverCanvasState? first, ObserverCanvasState? second)
    {
        first ??= ObserverCanvasState.Empty;
        second ??= ObserverCanvasState.Empty;
        return first.LayoutAlgorithmVersion == second.LayoutAlgorithmVersion &&
               DictionaryEquals(first.FolderOrigins, second.FolderOrigins) &&
               DictionaryEquals(first.SheetPlacements, second.SheetPlacements) &&
               DictionaryEquals(first.StatePlacements, second.StatePlacements);
    }

    private static bool DictionaryEquals(
        IReadOnlyDictionary<Guid, ObserverPointRecord> first,
        IReadOnlyDictionary<Guid, ObserverPointRecord> second) =>
        first.Count == second.Count &&
        first.All(pair => second.TryGetValue(pair.Key, out var value) && value == pair.Value);
}

public sealed record FolderRecord(
    Guid Id,
    Guid? ParentId,
    string Name,
    int Order,
    string Notes = "");

public sealed record SheetRecord(
    Guid PageViewId,
    Guid FolderId,
    int Order,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, string> Metadata,
    TitleBlockRole? TitleBlock,
    bool IncludeInPrintAll = true,
    SheetTitleBlockData? TitleBlockData = null,
    SheetNamingBinding? NamingBinding = null,
    string Notes = "",
    IReadOnlyDictionary<Guid, string>? DetailNamedViewAssignments = null)
{
    [JsonIgnore]
    public IReadOnlyDictionary<Guid, string> DetailNamedViews =>
        DetailNamedViewAssignments ?? new Dictionary<Guid, string>();
}

public sealed record SheetNamingBinding(
    string Pattern,
    int Index,
    string LastGeneratedName,
    IReadOnlyDictionary<Guid, string>? NamedViewAssignments = null)
{
    [JsonIgnore]
    public IReadOnlyDictionary<Guid, string> NamedViews =>
        NamedViewAssignments ?? new Dictionary<Guid, string>();
}

public sealed record TitleBlockRole(
    Guid InstanceObjectId,
    Guid InstanceDefinitionId,
    string AnchorName,
    BuiltInTitleBlockKind? BuiltInKind = null);

using System.Text.Json.Serialization;

namespace RhinoLayoutFoundry.Core.Domain;

public static class WellKnownIds
{
    public static readonly Guid UnorganizedFolderId = new("f3b9cf54-a8bf-43af-bbac-6575373199af");
}

/// <summary>The current document format. Collections have one name and are always present.</summary>
public sealed record DocumentState(
    int SchemaVersion,
    Guid RootFolderId,
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyList<FolderRecord> Folders,
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyDictionary<Guid, SheetRecord> Sheets,
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyDictionary<string, string> Metadata)
{
    public const int CurrentSchemaVersion = 16;

    [JsonRequired]
    public ObserverCanvasState Canvas { get; init; } = ObserverCanvasState.Empty;

    [JsonRequired]
    public IReadOnlyList<ImportRecoveryRecord> Recovery { get; init; } = [];

    [JsonRequired]
    public ProjectInformation ProjectInfo { get; init; } = ProjectInformation.Empty;

    [JsonRequired]
    public IReadOnlyList<HierarchyViewportRuleSet> AppearanceRules { get; init; } = [];

    [JsonRequired]
    public IReadOnlyList<LayoutTemplateRegistration> TemplateRegistrations { get; init; } = [];

    [JsonRequired]
    public IReadOnlyList<AppearanceStateRecord> AppearanceStates { get; init; } = [];

    [JsonRequired]
    public IReadOnlyList<AppearanceStateAssignment> StateAssignments { get; init; } = [];

    public Guid? DedicatedDetailLayerId { get; init; }

    public DocumentState RemoveMissingReferences(IReadOnlySet<Guid> pageIds, IReadOnlySet<Guid> detailIds)
    {
        bool Exists(HierarchyScope scope) => scope.Kind switch
        {
            HierarchyScopeKind.Folder => Folders.Any(folder => folder.Id == scope.Id),
            HierarchyScopeKind.Sheet => pageIds.Contains(scope.Id),
            HierarchyScopeKind.Detail => detailIds.Contains(scope.Id)
,
            _ => false,
        };
        var registrations = TemplateRegistrations.Where(item =>
Exists(item.Source))
            .ToArray();
        var rules = AppearanceRules.Where(item =>
Exists(item.Scope))
            .ToArray();
        var stateIds = AppearanceStates.Select(item => item.Id).ToHashSet();
        var assignments = StateAssignments.Where(item =>
                stateIds.Contains(item.StateId) &&
Exists(item.Target))
            .ToArray();
        return registrations.Length == TemplateRegistrations.Count &&
rules.Length == AppearanceRules.Count &&
               assignments.Length == StateAssignments.Count ? this
            : this with
            {
                TemplateRegistrations = registrations,
                AppearanceRules = rules,
                StateAssignments = assignments
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
            new Dictionary<string, string>(StringComparer.Ordinal));
    }
}

public sealed record ImportRecoveryRecord(
    string Kind,
    string Name,
    string Message,
    Guid? EntityId = null,
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyDictionary<string, string>? Data = null);

/// <summary>
/// Document-shared observer-board organization. Camera, selection, hover, and
/// rendered previews are deliberately session-only and never enter this state.
/// </summary>
public sealed record ObserverCanvasState(
    int LayoutAlgorithmVersion,
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyDictionary<Guid, ObserverPointRecord> FolderOrigins,
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyDictionary<Guid, ObserverPointRecord> SheetPlacements,
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyDictionary<Guid, ObserverPointRecord> StatePlacements)
{
    public const int CurrentLayoutAlgorithmVersion = 1;

    public static ObserverCanvasState Empty { get; } = new(
        CurrentLayoutAlgorithmVersion,
        new Dictionary<Guid, ObserverPointRecord>(),
        new Dictionary<Guid, ObserverPointRecord>(),
        new Dictionary<Guid, ObserverPointRecord>());
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
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyDictionary<string, string> Metadata,
    TitleBlockRole? TitleBlock,
    bool IncludeInPrintAll = true,
    SheetTitleBlockData? TitleBlockData = null,
    SheetNamingBinding? NamingBinding = null,
    string Notes = "")
{
    [JsonRequired]
    public IReadOnlyDictionary<Guid, string> DetailNamedViews { get; init; } = new Dictionary<Guid, string>();
}

public sealed record SheetNamingBinding(
    string Pattern,
    int Index,
    string LastGeneratedName)
{
    [JsonRequired]
    public IReadOnlyDictionary<Guid, string> NamedViewAssignments { get; init; } = new Dictionary<Guid, string>();
}

public sealed record TitleBlockRole(
    Guid InstanceObjectId,
    Guid InstanceDefinitionId,
    [property: JsonRequired] BuiltInTitleBlockKind BuiltInKind);

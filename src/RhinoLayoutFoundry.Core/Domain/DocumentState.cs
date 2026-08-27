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
    ObserverCanvasState? ObserverCanvas = null)
{
    public const int CurrentSchemaVersion = 4;

    [JsonIgnore]
    public IReadOnlyList<SheetTemplateRecipe> Templates => SheetTemplates ?? [];

    [JsonIgnore]
    public ObserverCanvasState Canvas => ObserverCanvas ?? ObserverCanvasState.Empty;

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
            ObserverCanvasState.Empty);
    }
}

/// <summary>
/// Document-shared observer-board organization. Camera, selection, hover, and
/// rendered previews are deliberately session-only and never enter this state.
/// </summary>
public sealed record ObserverCanvasState(
    int LayoutAlgorithmVersion,
    IReadOnlyDictionary<Guid, ObserverPointRecord> FolderOrigins,
    IReadOnlyDictionary<Guid, ObserverPointRecord> SheetPlacements)
{
    public const int CurrentLayoutAlgorithmVersion = 1;

    public static ObserverCanvasState Empty { get; } = new(
        CurrentLayoutAlgorithmVersion,
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
               DictionaryEquals(first.SheetPlacements, second.SheetPlacements);
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
    int Order);

public sealed record SheetRecord(
    Guid PageViewId,
    Guid FolderId,
    int Order,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, string> Metadata,
    TitleBlockRole? TitleBlock,
    bool IncludeInPrintAll = true);

public sealed record TitleBlockRole(
    Guid InstanceObjectId,
    Guid InstanceDefinitionId,
    string AnchorName);

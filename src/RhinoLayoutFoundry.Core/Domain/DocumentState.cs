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
    IReadOnlyList<SheetTemplateRecipe>? SheetTemplates = null)
{
    public const int CurrentSchemaVersion = 2;

    [JsonIgnore]
    public IReadOnlyList<SheetTemplateRecipe> Templates => SheetTemplates ?? [];

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
            []);
    }
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
    TitleBlockRole? TitleBlock);

public sealed record TitleBlockRole(
    Guid InstanceObjectId,
    Guid InstanceDefinitionId,
    string AnchorName);

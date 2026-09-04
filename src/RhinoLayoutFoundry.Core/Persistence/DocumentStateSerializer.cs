using System.Text.Json;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Persistence;

public static class DocumentStateSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static string Serialize(DocumentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Validate(state);
        return JsonSerializer.Serialize(state, Options);
    }

    public static DocumentState Deserialize(string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        var state = JsonSerializer.Deserialize<DocumentState>(payload, Options)
            ?? throw new JsonException("The document state payload was empty.");

        Validate(state);
        return state;
    }

    /// <summary>Rejects incompatible or structurally invalid state before persistence or native mutation.</summary>
    public static void Validate(DocumentState state)
    {

        if (state.SchemaVersion != DocumentState.CurrentSchemaVersion)
            throw new NotSupportedException(
                $"Document state schema {state.SchemaVersion} is not supported; expected {DocumentState.CurrentSchemaVersion}.");
        if (state.Folders is null || state.Sheets is null || state.Metadata is null || state.Canvas is null || state.ProjectInfo is null || state.Recovery is null || state.AppearanceRules is null || state.TemplateRegistrations is null || state.AppearanceStates is null || state.StateAssignments is null ||
            state.Folders.Any(item => item is null) || state.Sheets.Values.Any(item => item is null))
            throw new JsonException("Required document collections are missing or contain null entries.");
        if (state.RootFolderId == Guid.Empty || state.Folders.Any(item => item.Id == Guid.Empty || item.Name is null) || state.Folders.Select(item => item.Id).Distinct().Count() != state.Folders.Count ||
            !state.Folders.Any(item => item.Id == state.RootFolderId && item.ParentId is null))
            throw new JsonException("The folder hierarchy has invalid identities or no root.");
        var parents = state.Folders.ToDictionary(item => item.Id, item => item.ParentId);
        foreach (var folder in state.Folders)
        {
            if (folder.Id != state.RootFolderId && (folder.ParentId is not { } parentId || !parents.ContainsKey(parentId)))
                throw new JsonException("A folder has no valid parent.");
            var visited = new HashSet<Guid>();
            Guid? current = folder.Id;
            while (current is { } id && parents.TryGetValue(id, out var parent))
            {
                if (!visited.Add(id)) throw new JsonException("The folder hierarchy contains a cycle.");
                current = parent;
            }
        }
        if (state.Sheets.Any(pair => pair.Key == Guid.Empty || pair.Key != pair.Value.PageViewId || !parents.ContainsKey(pair.Value.FolderId) || pair.Value.Metadata is null || pair.Value.DetailNamedViews is null || pair.Value.NamingBinding is { NamedViewAssignments: null } || pair.Value.TitleBlockData is { Revisions: null }))
            throw new JsonException("A sheet has invalid identity, parent, or required collections.");
        foreach (var sheet in state.Sheets.Values)
            if (sheet.TitleBlock is { } block && (block.InstanceObjectId == Guid.Empty || block.InstanceDefinitionId == Guid.Empty || !Enum.IsDefined(block.BuiltInKind)))
                throw new JsonException("A managed title block has an invalid identity or built-in kind.");
        if (state.TemplateRegistrations.Any(item => item is null || item.Id == Guid.Empty || item.Source.Id == Guid.Empty || item.Source.Kind is not (HierarchyScopeKind.Sheet or HierarchyScopeKind.Detail)) ||
            state.AppearanceRules.Any(item => item is null || item.LayerRules is null || item.ObjectDisplayRules is null || !ValidScope(item.Scope)) ||
            state.AppearanceStates.Any(item => item is null || item.Id == Guid.Empty || item.Name is null || !parents.ContainsKey(item.FolderId) || item.LayerRules is null || item.ObjectDisplayRules is null) ||
            state.StateAssignments.Any(item => item is null || item.Id == Guid.Empty || !ValidScope(item.Target)) || state.Recovery.Any(item => item is null) ||
            state.Canvas.FolderOrigins is null || state.Canvas.SheetPlacements is null ||
            state.Canvas.StatePlacements is null || state.ProjectInfo.CustomFields is null || state.ProjectInfo.ContentOptions is null || state.ProjectInfo.ContentOptions.IncludedFields is null || state.ProjectInfo.ContentOptions.CustomFields is null || state.ProjectInfo.ContentOptions.CustomFields.Any(item => item is null))
            throw new JsonException("Required metadata values or nested collections are missing or invalid.");
        foreach (var rules in state.AppearanceRules) ValidateRules(rules.LayerRules, rules.ObjectDisplayRules);
        foreach (var rules in state.AppearanceStates) ValidateRules(rules.LayerRules, rules.ObjectDisplayRules);
        if (state.AppearanceStates.Select(item => item.Id).Distinct().Count() != state.AppearanceStates.Count || state.TemplateRegistrations.Select(item => item.Id).Distinct().Count() != state.TemplateRegistrations.Count || state.TemplateRegistrations.Select(item => item.Source).Distinct().Count() != state.TemplateRegistrations.Count || state.AppearanceRules.Select(item => item.Scope).Distinct().Count() != state.AppearanceRules.Count || state.StateAssignments.Select(item => item.Target).Distinct().Count() != state.StateAssignments.Count || state.StateAssignments.Select(item => item.Id).Distinct().Count() != state.StateAssignments.Count || state.StateAssignments.Any(item => state.AppearanceStates.All(resource => resource.Id != item.StateId)))
            throw new JsonException("Appearance states, assignments, or template registrations have conflicting identities.");
        if (state.Canvas.FolderOrigins.Values.Concat(state.Canvas.SheetPlacements.Values).Concat(state.Canvas.StatePlacements.Values).Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            throw new JsonException("Canvas coordinates must be finite.");
    }

    private static bool ValidScope(HierarchyScope scope) => scope.Id != Guid.Empty && Enum.IsDefined(scope.Kind);

    private static void ValidateRules(IReadOnlyList<LayerVisibilityRule> layers, IReadOnlyList<ObjectDisplayRule> objects)
    {
        if (layers.Any(rule => rule is null || rule.Layer is null) || objects.Any(rule => rule is null || rule.Selector is null))
            throw new JsonException("An appearance rule contains missing required values.");
    }
}

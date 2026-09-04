using System.Text.Json;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Persistence;

public static class DocumentStateSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = false,
    };

    public static string Serialize(DocumentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return JsonSerializer.Serialize(state, Options);
    }

    public static DocumentState Deserialize(string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        var state = JsonSerializer.Deserialize<DocumentState>(payload, Options)
            ?? throw new JsonException("The document state payload was empty.");

        Validate(state);

        if (state.SchemaVersion is >= 1 and <= 8)
        {
            var projectInformation = state.SchemaVersion < 7
                ? NormalizeProjectInformation(ProjectInformation.Empty)
                : NormalizeProjectInformation(state.ProjectInfo);
            return NormalizeCurrent(state with
            {
                SchemaVersion = DocumentState.CurrentSchemaVersion,
                SheetTemplates = state.SchemaVersion == 1 ? [] : state.Templates,
                Sheets = state.Sheets.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value with
                    {
                        IncludeInPrintAll = state.SchemaVersion <= 2 || pair.Value.IncludeInPrintAll,
                        NamingBinding = null,
                    }),
                ObserverCanvas = state.SchemaVersion < 4 ? ObserverCanvasState.Empty : state.Canvas,
                ImportRecovery = state.SchemaVersion < 5 ? [] : state.Recovery,
                DedicatedDetailLayerId = state.SchemaVersion < 6 ? null : state.DedicatedDetailLayerId,
                ProjectData = projectInformation,
                ViewportRuleSets = [],
                CapabilityTemplates = MigrateTemplateRegistrations(state),
                CapabilityLinks = [],
                AppearanceStateResources = [],
                AppearanceStateAssignments = [],
            });
        }

        if (state.SchemaVersion == 9)
        {
            return NormalizeCurrent(state with
            {
                SchemaVersion = DocumentState.CurrentSchemaVersion,
                Sheets = state.Sheets.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value with { NamingBinding = null }),
                CapabilityTemplates = state.TemplateRegistrations
                    .Select(item => item with
                    {
                        Capabilities = item.Capabilities &
                                       (TemplateCapability.Layout | TemplateCapability.TitleBlock),
                    })
                    .Where(item => item.Capabilities != TemplateCapability.None)
                    .ToArray(),
                CapabilityLinks = state.TemplateLinks
                    .Where(item => item.Capability is TemplateCapability.Layout or TemplateCapability.TitleBlock)
                    .ToArray(),
                AppearanceStateResources = [],
                AppearanceStateAssignments = [],
            });
        }

        if (state.SchemaVersion == 10)
        {
            return NormalizeCurrent(state with
            {
                SchemaVersion = DocumentState.CurrentSchemaVersion,
                Sheets = state.Sheets.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value with { NamingBinding = null }),
                AppearanceStateResources = [],
                AppearanceStateAssignments = [],
            });
        }

        if (state.SchemaVersion == 12)
        {
            return NormalizeCurrent(state with
            {
                SchemaVersion = DocumentState.CurrentSchemaVersion,
                AppearanceStateResources = state.AppearanceStates
                    .Select(item => item with { Notes = item.Notes ?? string.Empty })
                    .ToArray(),
            });
        }

        if (state.SchemaVersion is 13 or 14)
        {
            return NormalizeCurrent(state with
            {
                SchemaVersion = DocumentState.CurrentSchemaVersion,
                ObserverCanvas = state.Canvas with
                {
                    AppearanceStatePlacements = new Dictionary<Guid, ObserverPointRecord>(),
                },
            });
        }

        if (state.SchemaVersion != DocumentState.CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Document state schema {state.SchemaVersion} is not supported; expected {DocumentState.CurrentSchemaVersion}.");
        }

        return NormalizeCurrent(state);
    }

    private static void Validate(DocumentState state)
    {
        if (state.Folders is null || state.Sheets is null || state.DisplayRules is null || state.Metadata is null ||
            state.Folders.Any(item => item is null) || state.Sheets.Values.Any(item => item is null) ||
            state.DisplayRules.Any(item => item is null))
            throw new JsonException("Required document collections are missing or contain null entries.");
        if (state.Folders.Select(item => item.Id).Distinct().Count() != state.Folders.Count ||
            !state.Folders.Any(item => item.Id == state.RootFolderId))
            throw new JsonException("The folder hierarchy has duplicate identities or no root.");
        var parents = state.Folders.ToDictionary(item => item.Id, item => item.ParentId);
        foreach (var folder in state.Folders)
        {
            var visited = new HashSet<Guid>();
            Guid? current = folder.Id;
            while (current is { } id && parents.TryGetValue(id, out var parent))
            {
                if (!visited.Add(id)) throw new JsonException("The folder hierarchy contains a cycle.");
                current = parent;
            }
        }
        if (state.Sheets.Values.Any(item => item.Tags is null || item.Metadata is null) ||
            state.Templates.Any(item => item is null || item.Paper is null || item.DetailSlots is null) ||
            state.TemplateRegistrations.Any(item => item is null) ||
            state.TemplateLinks.Any(item => item is null || item.DetailMappings is null || item.LastResolved is null) ||
            state.AppearanceRules.Any(item => item is null || item.LayerRules is null || item.ObjectDisplayRules is null) ||
            state.AppearanceStates.Any(item => item is null || item.LayerRules is null || item.ObjectDisplayRules is null) ||
            state.StateAssignments.Any(item => item is null) || state.Recovery.Any(item => item is null) ||
            state.Canvas.FolderOrigins is null || state.Canvas.SheetPlacements is null ||
            state.ProjectInfo.CustomFields is null)
            throw new JsonException("Required metadata values or nested collections are missing.");
        foreach (var template in state.Templates)
        {
            if (template.DefaultTags is null || template.DefaultMetadata is null || template.DetailSlots.Any(slot => slot is null) ||
                template.TitleBlock is { } block && (block.Transform is null || block.FieldMappings is null))
                throw new JsonException("A template contains missing required values.");
            foreach (var slot in template.DetailSlots) ValidateRules(slot.Layers, slot.Objects);
        }
        foreach (var rule in state.DisplayRules)
            if (rule.ObjectIds is null || rule.Targets is null || rule.Targets.Any(target => target is null))
                throw new JsonException("A display rule contains missing required values.");
        foreach (var rules in state.AppearanceRules) ValidateRules(rules.LayerRules, rules.ObjectDisplayRules);
        foreach (var rules in state.AppearanceStates) ValidateRules(rules.LayerRules, rules.ObjectDisplayRules);
        if (state.AppearanceStates.Select(item => item.Id).Distinct().Count() != state.AppearanceStates.Count)
            throw new JsonException("Appearance states have duplicate identities.");
    }

    private static void ValidateRules(IReadOnlyList<LayerVisibilityRule> layers, IReadOnlyList<ObjectDisplayRule> objects)
    {
        if (layers.Any(rule => rule is null || rule.Layer is null) || objects.Any(rule => rule is null || rule.Selector is null))
            throw new JsonException("An appearance rule contains missing required values.");
    }

    private static DocumentState NormalizeCurrent(DocumentState state) => state with
        {
            Folders = state.Folders
                .Select(item => item with { Notes = item.Notes ?? string.Empty })
                .ToArray(),
            Sheets = state.Sheets.ToDictionary(
                pair => pair.Key,
                pair => pair.Value with { Notes = pair.Value.Notes ?? string.Empty }),
            ObserverCanvas = state.Canvas with
            {
                AppearanceStatePlacements = state.Canvas.StatePlacements,
            },
            ImportRecovery = state.Recovery,
            ProjectData = NormalizeProjectInformation(state.ProjectInfo),
            ViewportRuleSets = state.AppearanceRules,
            CapabilityTemplates = state.TemplateRegistrations,
            CapabilityLinks = state.TemplateLinks,
            AppearanceStateResources = state.AppearanceStates
                .Select(item => item with { Notes = item.Notes ?? string.Empty })
                .ToArray(),
            AppearanceStateAssignments = state.StateAssignments,
        };

    private static IReadOnlyList<CapabilityTemplateRegistration> MigrateTemplateRegistrations(
        DocumentState state) => state.Templates
        .Where(template => template.SourcePageViewId is not null)
        .Select(template => new CapabilityTemplateRegistration(
            template.Id,
            new HierarchyScope(HierarchyScopeKind.Sheet, template.SourcePageViewId!.Value),
            TemplateCapability.Layout |
            (template.TitleBlock is null ? TemplateCapability.None : TemplateCapability.TitleBlock)))
        .ToArray();

    private static ProjectInformation NormalizeProjectInformation(ProjectInformation information) =>
        information with { TitleBlockOptions = information.ContentOptions };
}

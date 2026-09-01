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

        if (state.SchemaVersion is >= 1 and <= 8)
        {
            var projectInformation = state.SchemaVersion < 7
                ? NormalizeProjectInformation(ProjectInformation.Empty)
                : NormalizeProjectInformation(state.ProjectInfo);
            return state with
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
            };
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

        if (state.SchemaVersion != DocumentState.CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Document state schema {state.SchemaVersion} is not supported; expected {DocumentState.CurrentSchemaVersion}.");
        }

        return NormalizeCurrent(state);
    }

    private static DocumentState NormalizeCurrent(DocumentState state) => state with
        {
            ObserverCanvas = state.Canvas,
            ImportRecovery = state.Recovery,
            ProjectData = NormalizeProjectInformation(state.ProjectInfo),
            ViewportRuleSets = state.AppearanceRules,
            CapabilityTemplates = state.TemplateRegistrations,
            CapabilityLinks = state.TemplateLinks,
            AppearanceStateResources = state.AppearanceStates,
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

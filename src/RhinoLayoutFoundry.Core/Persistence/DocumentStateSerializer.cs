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
            return MigrateSchemaNine(state);
        }

        if (state.SchemaVersion == 10)
        {
            return NormalizeCurrent(state with
            {
                SchemaVersion = DocumentState.CurrentSchemaVersion,
                Sheets = state.Sheets.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value with { NamingBinding = null }),
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

    private static DocumentState MigrateSchemaNine(DocumentState state)
    {
        var resources = new List<AppearanceStateRecord>();
        var stateIds = new Dictionary<(Guid RegistrationId, AppearanceStateKind Kind), Guid>();
        var orderByFolder = state.AppearanceStates
            .GroupBy(item => item.FolderId)
            .ToDictionary(group => group.Key, group => group.Max(item => item.Order) + 1);
        foreach (var registration in state.TemplateRegistrations)
        {
            foreach (var kind in new[] { AppearanceStateKind.LayerState, AppearanceStateKind.ObjectDisplayState })
            {
                var capability = kind == AppearanceStateKind.LayerState
                    ? TemplateCapability.LayerStates
                    : TemplateCapability.ObjectDisplayModes;
                if (!registration.Capabilities.HasFlag(capability)) continue;
                var folderId = ResourceFolder(state, registration.Source);
                var sourceRules = state.AppearanceRules.LastOrDefault(item => item.Scope == registration.Source);
                var fallback = state.TemplateLinks.FirstOrDefault(item =>
                    item.SourceRegistrationId == registration.Id && item.Capability == capability)?.LastResolved;
                var id = Guid.NewGuid();
                stateIds[(registration.Id, kind)] = id;
                var order = orderByFolder.GetValueOrDefault(folderId);
                orderByFolder[folderId] = order + 1;
                resources.Add(new AppearanceStateRecord(
                    id,
                    folderId,
                    order,
                    ResourceName(state, registration.Source, kind),
                    kind,
                    kind == AppearanceStateKind.LayerState
                        ? sourceRules?.LayerRules.ToArray() ?? fallback?.Layers.ToArray() ?? []
                        : [],
                    kind == AppearanceStateKind.ObjectDisplayState
                        ? sourceRules?.ObjectDisplayRules.ToArray() ?? fallback?.Objects.ToArray() ?? []
                        : []));
            }
        }

        var assignments = state.TemplateLinks
            .Where(link => link.Capability is TemplateCapability.LayerStates or
                TemplateCapability.ObjectDisplayModes)
            .Select(link =>
            {
                var kind = link.Capability == TemplateCapability.LayerStates
                    ? AppearanceStateKind.LayerState
                    : AppearanceStateKind.ObjectDisplayState;
                return stateIds.TryGetValue((link.SourceRegistrationId, kind), out var stateId)
                    ? new AppearanceStateAssignment(Guid.NewGuid(), link.Target, kind, stateId)
                    : null;
            })
            .Where(item => item is not null)
            .Cast<AppearanceStateAssignment>()
            .GroupBy(item => (item.Target, item.Kind))
            .Select(group => group.Last())
            .ToArray();
        var registrations = state.TemplateRegistrations
            .Select(item => item with
            {
                Capabilities = item.Capabilities &
                               (TemplateCapability.Layout | TemplateCapability.TitleBlock),
            })
            .Where(item => item.Capabilities != TemplateCapability.None)
            .ToArray();
        return state with
        {
            SchemaVersion = DocumentState.CurrentSchemaVersion,
            Sheets = state.Sheets.ToDictionary(
                pair => pair.Key,
                pair => pair.Value with { NamingBinding = null }),
            ObserverCanvas = state.Canvas,
            ImportRecovery = state.Recovery,
            ProjectData = NormalizeProjectInformation(state.ProjectInfo),
            ViewportRuleSets = state.AppearanceRules,
            CapabilityTemplates = registrations,
            CapabilityLinks = [],
            AppearanceStateResources = resources,
            AppearanceStateAssignments = assignments,
        };
    }

    private static Guid ResourceFolder(DocumentState state, HierarchyScope source)
    {
        if (source.Kind == HierarchyScopeKind.Folder && state.Folders.Any(item => item.Id == source.Id))
            return source.Id;
        if (source.Kind == HierarchyScopeKind.Sheet && state.Sheets.TryGetValue(source.Id, out var sheet))
            return sheet.FolderId;
        return state.RootFolderId;
    }

    private static string ResourceName(
        DocumentState state,
        HierarchyScope source,
        AppearanceStateKind kind)
    {
        var sourceName = source.Kind == HierarchyScopeKind.Folder
            ? state.Folders.FirstOrDefault(item => item.Id == source.Id)?.Name
            : null;
        var suffix = kind == AppearanceStateKind.LayerState ? "Layer State" : "Object Display State";
        return string.IsNullOrWhiteSpace(sourceName) ? suffix : $"{sourceName} — {suffix}";
    }

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

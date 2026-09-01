using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Naming;
using System.Globalization;

namespace RhinoLayoutFoundry.Core.Operations;

public sealed record CaptureSheetTemplateRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    Guid TemplateId,
    Guid SourcePageViewId,
    string Name,
    string DefaultNamingPattern,
    Guid? TitleBlockInstanceObjectId);

public sealed class CaptureSheetTemplatePlanner : IOperationPlanner<CaptureSheetTemplateRequest>
{
    public OperationPlan Plan(CaptureSheetTemplateRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = ValidateContext(request.DocumentRuntimeSerialNumber, request.SourceRevision, snapshot);
        var name = request.Name?.Trim() ?? string.Empty;
        var pattern = request.DefaultNamingPattern?.Trim() ?? string.Empty;

        if (!snapshot.Sheets.ContainsKey(request.SourcePageViewId))
            diagnostics.Add(Error("template.source_missing", "The source layout no longer exists."));
        if (request.TemplateId == Guid.Empty)
            diagnostics.Add(Error("template.id_required", "The template identifier is invalid."));
        if (name.Length == 0)
            diagnostics.Add(Error("template.name_required", "Enter a template name."));
        if (snapshot.Templates.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
            diagnostics.Add(Error("template.duplicate_name", $"A template named '{name}' already exists."));
        if (snapshot.Templates.Any(item => item.SourcePageViewId == request.SourcePageViewId))
            diagnostics.Add(Error("template.source_registered", "The source layout is already registered as a template."));
        if (pattern.Length == 0)
            diagnostics.Add(Error("template.pattern_required", "Enter a default naming pattern."));

        IReadOnlyList<OperationChange> changes = diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error)
            ? []
            : [new CaptureSheetTemplateChange(
                request.TemplateId,
                request.SourcePageViewId,
                name,
                pattern,
                request.TitleBlockInstanceObjectId)];
        return new OperationPlan(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision,
            "Capture layout template", changes, diagnostics);
    }

    internal static List<Diagnostic> ValidateContext(uint serial, long revision, DocumentSnapshot snapshot)
    {
        var diagnostics = new List<Diagnostic>();
        if (serial != snapshot.DocumentRuntimeSerialNumber)
            diagnostics.Add(Error("template.document_mismatch", "The active Rhino document changed."));
        if (revision != snapshot.Revision)
            diagnostics.Add(Error("template.stale_revision", "The Rhino document changed. Refresh and try again."));
        return diagnostics;
    }

    internal static Diagnostic Error(string code, string message) =>
        new(code, DiagnosticSeverity.Error, message);
}

public sealed record TemplateQuantity(Guid TemplateId, int Quantity);

public enum BuiltInLayoutKind
{
    Blank,
    SingleDetail,
    TwoDetailsHorizontal,
    TwoDetailsVertical,
    FourDetailsGrid,
}

public sealed record LayoutCreationSpec(
    int Quantity,
    PaperRecipe Paper,
    BuiltInLayoutKind BuiltInLayout = BuiltInLayoutKind.SingleDetail,
    Guid? TemplateId = null,
    Guid? DetailDisplayModeId = null,
    bool UseTemplateTitleBlock = true,
    Guid? TitleBlockSourceInstanceObjectId = null,
    BuiltInTitleBlockKind? BuiltInTitleBlock = null,
    string? NamedView = null,
    bool UseDedicatedDetailLayer = true,
    IReadOnlyList<string?>? NamedViewsByDetail = null,
    IReadOnlyList<Guid?>? DetailDisplayModesByDetail = null,
    Guid? DetailLayerId = null,
    Guid? AppearanceStateId = null);

public sealed record BatchCreateSheetsRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    Guid DestinationFolderId,
    IReadOnlyList<TemplateQuantity> TemplateQuantities,
    string NamingPattern,
    int Start,
    int Step,
    IReadOnlyDictionary<Guid, string>? NamedViewAssignments = null,
    IReadOnlyList<LayoutCreationSpec>? CreationSpecs = null,
    ProjectInformation? ProjectData = null,
    IReadOnlyList<SheetRevisionRecord>? InitialRevisions = null);

public sealed class BatchCreateSheetsPlanner : IOperationPlanner<BatchCreateSheetsRequest>
{
    public OperationPlan Plan(BatchCreateSheetsRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = CaptureSheetTemplatePlanner.ValidateContext(
            request.DocumentRuntimeSerialNumber, request.SourceRevision, snapshot);
        if (!snapshot.Folders.TryGetValue(request.DestinationFolderId, out var destination))
            diagnostics.Add(CaptureSheetTemplatePlanner.Error("batch.destination_missing", "The destination folder no longer exists."));
        var hasCreationSpecs = request.CreationSpecs is { Count: > 0 };
        if (!hasCreationSpecs &&
            (request.TemplateQuantities.Count == 0 || request.TemplateQuantities.All(item => item.Quantity <= 0)))
            diagnostics.Add(CaptureSheetTemplatePlanner.Error("batch.empty", "Choose at least one template and quantity."));
        if (request.Step == 0)
            diagnostics.Add(CaptureSheetTemplatePlanner.Error("batch.step_zero", "The naming step cannot be zero."));
        UpdateProjectInformationPlanner.Validate(request.ProjectData ?? snapshot.ProjectInfo, diagnostics);

        var templates = snapshot.Templates.ToDictionary(item => item.Id);
        var expanded = new List<(Guid DraftId, SheetTemplateRecipe Template,
            IReadOnlyDictionary<Guid, string> NamedViewAssignments, bool UseDedicatedDetailLayer,
            Guid? DetailLayerId, Guid? AppearanceStateId)>();
        if (hasCreationSpecs)
        {
            foreach (var spec in request.CreationSpecs!)
            {
                if (spec.Quantity <= 0)
                {
                    diagnostics.Add(CaptureSheetTemplatePlanner.Error(
                        "batch.quantity_invalid", "Layout quantities must be greater than zero."));
                    continue;
                }

                var resolved = ResolveTemplate(
                    spec, templates, snapshot, request.ProjectData ?? snapshot.ProjectInfo,
                    request.NamedViewAssignments, diagnostics);
                if (resolved is null)
                {
                    continue;
                }

                ValidateTemplate(resolved.Template, snapshot, resolved.NamedViewAssignments, diagnostics);
                if (!spec.UseDedicatedDetailLayer && spec.DetailLayerId is { } detailLayerId &&
                    !snapshot.Layers.ContainsKey(detailLayerId))
                    diagnostics.Add(CaptureSheetTemplatePlanner.Error(
                        "batch.detail_layer_missing", "The selected detail layer is no longer available."));
                for (var index = 0; index < spec.Quantity; index++)
                    expanded.Add((Guid.NewGuid(), resolved.Template, resolved.NamedViewAssignments,
                        spec.UseDedicatedDetailLayer,
                        spec.UseDedicatedDetailLayer ? null : spec.DetailLayerId,
                        spec.AppearanceStateId));
            }
        }
        else foreach (var item in request.TemplateQuantities)
        {
            if (item.Quantity < 0)
            {
                diagnostics.Add(CaptureSheetTemplatePlanner.Error("batch.quantity_invalid", "Template quantities cannot be negative."));
                continue;
            }
            if (!templates.TryGetValue(item.TemplateId, out var template))
            {
                diagnostics.Add(CaptureSheetTemplatePlanner.Error("batch.template_missing", "A selected template no longer exists."));
                continue;
            }
            ValidateTemplate(template, snapshot, request.NamedViewAssignments, diagnostics);
            for (var index = 0; index < item.Quantity; index++)
                expanded.Add((Guid.NewGuid(), template,
                    request.NamedViewAssignments ?? new Dictionary<Guid, string>(), true, null, null));
        }

        var pattern = request.NamingPattern?.Trim() ?? string.Empty;
        if (pattern.Length == 0 && expanded.Select(item => item.Template.DefaultNamingPattern)
                .Distinct(StringComparer.Ordinal).Take(2).Count() > 1)
            diagnostics.Add(CaptureSheetTemplatePlanner.Error(
                "batch.pattern_required", "Mixed templates need one batch naming pattern."));

        var namingItems = expanded.Select(item => new NamingItem(
            item.DraftId,
            item.Template.Name,
            Tokens(snapshot, destination?.Name, item.Template, item.NamedViewAssignments))).ToArray();
        var resolvedPattern = pattern.Length == 0
            ? expanded.FirstOrDefault().Template?.DefaultNamingPattern ?? string.Empty
            : pattern;
        var naming = NamingEngine.Preview(new NamingRequest(
            resolvedPattern,
            namingItems,
            request.Start,
            request.Step));
        diagnostics.AddRange(naming.Diagnostics);

        var existingNames = snapshot.Sheets.Values.Select(item => item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in naming.Entries.Where(entry => existingNames.Contains(entry.ProposedName)))
            diagnostics.Add(new Diagnostic("batch.name_exists", DiagnosticSeverity.Error,
                $"A layout named '{entry.ProposedName}' already exists.", entry.SheetId));

        var changes = new List<OperationChange>();
        if (diagnostics.All(item => item.Severity != DiagnosticSeverity.Error))
        {
            var nextOrder = snapshot.Sheets.Values.Where(item => item.FolderId == request.DestinationFolderId)
                .Select(item => item.Order).DefaultIfEmpty(-1).Max() + 1;
            for (var index = 0; index < expanded.Count; index++)
            {
                changes.Add(new CreateSheetFromTemplateChange(
                    request.DestinationFolderId,
                    naming.Entries[index].ProposedName,
                    nextOrder + index,
                    expanded[index].Template,
                    expanded[index].NamedViewAssignments,
                    expanded[index].UseDedicatedDetailLayer,
                    (request.Start + index * request.Step).ToString(CultureInfo.InvariantCulture),
                    request.ProjectData ?? snapshot.ProjectInfo,
                    request.InitialRevisions,
                    expanded[index].DetailLayerId,
                    expanded[index].AppearanceStateId,
                    resolvedPattern,
                    request.Start + index * request.Step));
            }
            diagnostics.Add(new Diagnostic("batch.undo_unavailable", DiagnosticSeverity.Warning,
                "Rhino does not expose native Undo for layout creation. Foundry will roll back the entire batch if any sheet fails."));
        }
        return new OperationPlan(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision,
            $"Create {changes.Count} layouts", changes, diagnostics);
    }

    private static ResolvedCreationSpec? ResolveTemplate(
        LayoutCreationSpec spec,
        IReadOnlyDictionary<Guid, SheetTemplateRecipe> templates,
        DocumentSnapshot snapshot,
        ProjectInformation projectInformation,
        IReadOnlyDictionary<Guid, string>? legacyNamedViewAssignments,
        ICollection<Diagnostic> diagnostics)
    {
        if (spec.Paper.Width <= 0 || spec.Paper.Height <= 0 || string.IsNullOrWhiteSpace(spec.Paper.UnitSystem))
        {
            diagnostics.Add(CaptureSheetTemplatePlanner.Error(
                "template.paper_invalid", "Choose a valid paper width, height, and unit."));
            return null;
        }

        SheetTemplateRecipe source;
        if (spec.TemplateId is { } templateId)
        {
            if (!templates.TryGetValue(templateId, out source!))
            {
                diagnostics.Add(CaptureSheetTemplatePlanner.Error(
                    "batch.template_missing", "The selected layout template no longer exists."));
                return null;
            }
        }
        else
        {
            source = BuiltInTemplate(spec.BuiltInLayout, spec.Paper);
        }

        if (spec.DetailDisplayModeId is { } modeId && !snapshot.DisplayModeIds.Contains(modeId))
        {
            diagnostics.Add(CaptureSheetTemplatePlanner.Error(
                "template.display_mode_unresolved", "The selected detail display mode is unavailable."));
        }

        var hasDisplayModeOverrides = spec.DetailDisplayModesByDetail is not null &&
                                      spec.DetailDisplayModesByDetail.Count == source.DetailSlots.Count;
        if (spec.DetailDisplayModesByDetail is not null && !hasDisplayModeOverrides)
        {
            diagnostics.Add(CaptureSheetTemplatePlanner.Error(
                "template.display_mode_assignment_count",
                $"Layout '{source.Name}' has {source.DetailSlots.Count} details but received " +
                $"{spec.DetailDisplayModesByDetail.Count} detail display-mode assignments."));
        }
        else if (spec.DetailDisplayModesByDetail is not null)
        {
            foreach (var overrideId in spec.DetailDisplayModesByDetail.OfType<Guid>())
            {
                if (snapshot.DisplayModeIds.Contains(overrideId)) continue;
                diagnostics.Add(CaptureSheetTemplatePlanner.Error(
                    "template.display_mode_unresolved",
                    "A selected detail display-mode override is unavailable."));
            }
        }

        TitleBlockTemplateRecipe? titleBlock = source.TitleBlock;
        if (!spec.UseTemplateTitleBlock)
        {
            titleBlock = null;
            if (spec.TitleBlockSourceInstanceObjectId is { } instanceId)
            {
                if (!snapshot.TitleBlockInstances.TryGetValue(instanceId, out var instance) ||
                    instance.Transform is not { Count: 16 })
                {
                    diagnostics.Add(CaptureSheetTemplatePlanner.Error(
                        "template.title_block_unresolved", "The selected title-block instance is unavailable."));
                }
                else
                {
                    titleBlock = new TitleBlockTemplateRecipe(
                        instance.InstanceDefinitionId,
                        instance.InstanceDefinitionName,
                        instance.Transform,
                        instance.AnchorName,
                        new Dictionary<string, string>(StringComparer.Ordinal));
                }
            }
        }

        AdaptiveTitleBlockLayout? adaptiveTitleBlock = null;
        if (spec.BuiltInTitleBlock is { } builtInKind)
        {
            try
            {
                adaptiveTitleBlock = AdaptiveTitleBlockLayoutSolver.Solve(
                    builtInKind, spec.Paper, projectInformation, source.DetailSlots.Count);
                titleBlock = new TitleBlockTemplateRecipe(
                    Guid.Empty,
                    $"Foundry — {AdaptiveTitleBlockLayoutSolver.Label(builtInKind)}",
                    IdentityTransform,
                    AdaptiveTitleBlockLayoutSolver.Label(builtInKind),
                    StandardTitleBlockMappings,
                    builtInKind);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                diagnostics.Add(CaptureSheetTemplatePlanner.Error(
                    "title_block.paper_too_small", exception.Message));
            }
        }

        var horizontalScale = spec.Paper.Width / source.Paper.Width;
        var verticalScale = spec.Paper.Height / source.Paper.Height;
        var details = source.DetailSlots.Select((slot, index) => slot with
        {
            Left = slot.Left * horizontalScale,
            Right = slot.Right * horizontalScale,
            Bottom = slot.Bottom * verticalScale,
            Top = slot.Top * verticalScale,
            DisplayModeId = hasDisplayModeOverrides && spec.DetailDisplayModesByDetail![index] is { } overrideId
                ? overrideId
                : spec.DetailDisplayModeId ?? slot.DisplayModeId,
        }).ToArray();

        ValidateAppearanceState(spec.AppearanceStateId, snapshot, diagnostics);

        var namedViewAssignments = new Dictionary<Guid, string>();
        if (spec.NamedViewsByDetail is not null)
        {
            if (spec.NamedViewsByDetail.Count != details.Length)
            {
                diagnostics.Add(CaptureSheetTemplatePlanner.Error(
                    "template.named_view_assignment_count",
                    $"Layout '{source.Name}' has {details.Length} details but received " +
                    $"{spec.NamedViewsByDetail.Count} named-view assignments."));
            }
            else
            {
                for (var index = 0; index < details.Length; index++)
                {
                    var namedView = spec.NamedViewsByDetail[index]?.Trim();
                    if (!string.IsNullOrWhiteSpace(namedView))
                        namedViewAssignments[details[index].Id] = namedView;
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(spec.NamedView))
        {
            var namedView = spec.NamedView.Trim();
            foreach (var detail in details)
                namedViewAssignments[detail.Id] = namedView;
        }
        else if (legacyNamedViewAssignments is not null)
        {
            foreach (var detail in details)
            {
                var namedView = legacyNamedViewAssignments.GetValueOrDefault(detail.Id)?.Trim();
                if (!string.IsNullOrWhiteSpace(namedView))
                    namedViewAssignments[detail.Id] = namedView;
            }
        }

        if (adaptiveTitleBlock is not null)
        {
            var targetContent = adaptiveTitleBlock.Content;
            if (details.Length > 0)
            {
                var sourceContent = new TitleBlockRectangle(
                    details.Min(slot => slot.Left),
                    details.Min(slot => slot.Bottom),
                    details.Max(slot => slot.Right) - details.Min(slot => slot.Left),
                    details.Max(slot => slot.Top) - details.Min(slot => slot.Bottom));
                details = details.Select(slot => slot with
                {
                    Left = MapCoordinate(slot.Left, sourceContent.Left, sourceContent.Width,
                        targetContent.Left, targetContent.Width),
                    Right = MapCoordinate(slot.Right, sourceContent.Left, sourceContent.Width,
                        targetContent.Left, targetContent.Width),
                    Bottom = MapCoordinate(slot.Bottom, sourceContent.Bottom, sourceContent.Height,
                        targetContent.Bottom, targetContent.Height),
                    Top = MapCoordinate(slot.Top, sourceContent.Bottom, sourceContent.Height,
                        targetContent.Bottom, targetContent.Height),
                }).ToArray();
            }
        }

        var template = source with
        {
            Id = Guid.NewGuid(),
            Paper = spec.Paper,
            DetailSlots = details,
            TitleBlock = titleBlock,
        };
        return new ResolvedCreationSpec(template, namedViewAssignments);
    }

    private static void ValidateAppearanceState(
        Guid? stateId,
        DocumentSnapshot snapshot,
        ICollection<Diagnostic> diagnostics)
    {
        if (stateId is not { } id) return;
        var state = snapshot.AppearanceStates.LastOrDefault(item => item.Id == id);
        if (state is null)
        {
            diagnostics.Add(CaptureSheetTemplatePlanner.Error(
                "appearance_state.source_missing",
                "The selected appearance state is unavailable."));
        }
    }

    private static double MapCoordinate(
        double value,
        double sourceOrigin,
        double sourceLength,
        double targetOrigin,
        double targetLength) =>
        targetOrigin + (value - sourceOrigin) / sourceLength * targetLength;

    private sealed record ResolvedCreationSpec(
        SheetTemplateRecipe Template,
        IReadOnlyDictionary<Guid, string> NamedViewAssignments);

    private static SheetTemplateRecipe BuiltInTemplate(BuiltInLayoutKind kind, PaperRecipe paper)
    {
        var marginX = Math.Min(paper.Width, paper.Height) * 0.025;
        var marginY = marginX;
        var left = marginX;
        var right = paper.Width - marginX;
        var bottom = marginY;
        var top = paper.Height - marginY;
        var gapX = paper.Width * 0.02;
        var gapY = paper.Height * 0.02;
        var midX = paper.Width / 2;
        var midY = paper.Height / 2;
        DetailSlotRecipe Slot(string name, double x1, double y1, double x2, double y2) =>
            new(Guid.NewGuid(), name, x1, y1, x2, y2, "Top", null, false, null, null);
        IReadOnlyList<DetailSlotRecipe> details = kind switch
        {
            BuiltInLayoutKind.Blank => [],
            BuiltInLayoutKind.SingleDetail => [Slot("Detail 1", left, bottom, right, top)],
            BuiltInLayoutKind.TwoDetailsHorizontal =>
            [
                Slot("Detail 1", left, midY + gapY / 2, right, top),
                Slot("Detail 2", left, bottom, right, midY - gapY / 2),
            ],
            BuiltInLayoutKind.TwoDetailsVertical =>
            [
                Slot("Detail 1", left, bottom, midX - gapX / 2, top),
                Slot("Detail 2", midX + gapX / 2, bottom, right, top),
            ],
            BuiltInLayoutKind.FourDetailsGrid =>
            [
                Slot("Detail 1", left, midY + gapY / 2, midX - gapX / 2, top),
                Slot("Detail 2", midX + gapX / 2, midY + gapY / 2, right, top),
                Slot("Detail 3", left, bottom, midX - gapX / 2, midY - gapY / 2),
                Slot("Detail 4", midX + gapX / 2, bottom, right, midY - gapY / 2),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        return new SheetTemplateRecipe(
            Guid.NewGuid(),
            SheetTemplateRecipe.CurrentRecipeVersion,
            BuiltInLabel(kind),
            paper,
            details,
            null,
            [],
            new Dictionary<string, string>(StringComparer.Ordinal),
            "Page {index}");
    }

    private static string BuiltInLabel(BuiltInLayoutKind kind) => kind switch
    {
        BuiltInLayoutKind.Blank => "Blank",
        BuiltInLayoutKind.SingleDetail => "1 Detail — Top",
        BuiltInLayoutKind.TwoDetailsHorizontal => "2 Details — Horizontal",
        BuiltInLayoutKind.TwoDetailsVertical => "2 Details — Vertical",
        BuiltInLayoutKind.FourDetailsGrid => "4 Details — Grid",
        _ => kind.ToString(),
    };

    private static void ValidateTemplate(
        SheetTemplateRecipe template,
        DocumentSnapshot snapshot,
        IReadOnlyDictionary<Guid, string>? assignments,
        ICollection<Diagnostic> diagnostics)
    {
        if (template.RecipeVersion != SheetTemplateRecipe.CurrentRecipeVersion)
            diagnostics.Add(CaptureSheetTemplatePlanner.Error("template.version_unsupported", $"Template '{template.Name}' uses an unsupported recipe version."));
        if (template.Paper.Width <= 0 || template.Paper.Height <= 0 || string.IsNullOrWhiteSpace(template.Paper.UnitSystem))
            diagnostics.Add(CaptureSheetTemplatePlanner.Error("template.paper_invalid", $"Template '{template.Name}' has invalid paper settings."));
        if (template.DetailSlots.Any(slot => slot.Right <= slot.Left || slot.Top <= slot.Bottom))
            diagnostics.Add(CaptureSheetTemplatePlanner.Error("template.detail_bounds_invalid", $"Template '{template.Name}' contains an invalid detail rectangle."));
        if (template.DetailSlots.Any(slot => slot.PageToModelRatio is <= 0))
            diagnostics.Add(CaptureSheetTemplatePlanner.Error("template.detail_scale_invalid", $"Template '{template.Name}' contains an invalid detail scale."));
        if (template.TitleBlock is { BuiltInKind: null } block &&
            !snapshot.InstanceDefinitions.Contains(block.InstanceDefinitionId))
            diagnostics.Add(new Diagnostic("template.block_unresolved", DiagnosticSeverity.Warning,
                $"Title block '{block.InstanceDefinitionName}' is not in this document and will be skipped."));
        if (template.TitleBlock is { Transform.Count: not 16 })
            diagnostics.Add(CaptureSheetTemplatePlanner.Error("template.block_transform_invalid", $"Template '{template.Name}' contains an invalid title-block transform."));
        foreach (var slot in template.DetailSlots)
        {
            var namedView = assignments?.GetValueOrDefault(slot.Id) ?? slot.DefaultNamedView;
            if (!string.IsNullOrWhiteSpace(namedView) && !snapshot.NamedViews.Contains(namedView))
                diagnostics.Add(CaptureSheetTemplatePlanner.Error("template.named_view_unresolved",
                    $"Named view '{namedView}' assigned to '{slot.Name}' is not available."));
            if (slot.DisplayModeId is { } modeId && !snapshot.DisplayModeIds.Contains(modeId))
                diagnostics.Add(new Diagnostic("template.display_mode_unresolved", DiagnosticSeverity.Warning,
                    $"The display mode for detail '{slot.Name}' is unavailable; Rhino's default will be used."));
        }
    }

    private static readonly IReadOnlyList<double> IdentityTransform =
    [
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1,
    ];

    private static readonly IReadOnlyDictionary<string, string> StandardTitleBlockMappings =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["project.name"] = "document.project_name",
            ["project.number"] = "document.project_number",
            ["project.client"] = "document.client_name",
            ["project.site"] = "document.site_address",
            ["project.phase"] = "document.project_phase",
            ["project.status"] = "document.project_status",
            ["firm.name"] = "document.firm_name",
            ["issue.date"] = "document.issue_date",
            ["issue.purpose"] = "document.issue_purpose",
            ["sheet.number"] = "sheet.number",
            ["sheet.title"] = "sheet.title",
            ["sheet.scale"] = "sheet.scale",
        };

    private static IReadOnlyDictionary<string, string> Tokens(
        DocumentSnapshot snapshot,
        string? folder,
        SheetTemplateRecipe template,
        IReadOnlyDictionary<Guid, string>? assignments)
    {
        var result = new Dictionary<string, string>(snapshot.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["folder"] = folder ?? string.Empty,
            ["tag"] = template.DefaultTags.FirstOrDefault() ?? string.Empty,
        };
        foreach (var pair in template.DefaultMetadata)
            result[pair.Key] = pair.Value;
        var assigned = template.DetailSlots.Select(slot => assignments?.GetValueOrDefault(slot.Id))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        result["view"] = assigned ?? template.DetailSlots.Select(slot => slot.DefaultNamedView)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        return result;
    }
}

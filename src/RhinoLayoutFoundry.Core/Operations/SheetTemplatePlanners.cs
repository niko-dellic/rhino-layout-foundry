using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Naming;
using System.Globalization;

namespace RhinoLayoutFoundry.Core.Operations;

internal static class SheetPlanValidation
{

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
    BuiltInTitleBlockKind? BuiltInTitleBlock = null,
    bool UseDedicatedDetailLayer = true,
    IReadOnlyList<string?>? NamedViewsByDetail = null,
    IReadOnlyList<Guid?>? DetailDisplayModesByDetail = null,
    Guid? DetailLayerId = null,
    Guid? AppearanceStateId = null,
    IReadOnlyList<Guid?>? AppearanceStatesByDetail = null);

public sealed record BatchCreateSheetsRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    Guid DestinationFolderId,
    IReadOnlyList<LayoutCreationSpec> CreationSpecs,
    string NamingPattern,
    int Start,
    int Step,
    ProjectInformation? ProjectInfo = null,
    IReadOnlyList<SheetRevisionRecord>? InitialRevisions = null,
    NamingIndexMode IndexMode = NamingIndexMode.FolderPosition);

public sealed class BatchCreateSheetsPlanner : IOperationPlanner<BatchCreateSheetsRequest>
{
    public OperationPlan Plan(BatchCreateSheetsRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = SheetPlanValidation.ValidateContext(
            request.DocumentRuntimeSerialNumber, request.SourceRevision, snapshot);
        if (!snapshot.Folders.TryGetValue(request.DestinationFolderId, out var destination))
            diagnostics.Add(SheetPlanValidation.Error("batch.destination_missing", "The destination folder no longer exists."));
        if (request.CreationSpecs is null || request.CreationSpecs.Count == 0)
            diagnostics.Add(SheetPlanValidation.Error("batch.empty", "Provide at least one layout creation specification."));
        UpdateProjectInformationPlanner.Validate(request.ProjectInfo ?? snapshot.ProjectInfo, diagnostics);

        var templates = snapshot.Templates.ToDictionary(item => item.Id);
        var expanded = new List<(Guid DraftId, SheetTemplateRecipe Template,
            IReadOnlyDictionary<Guid, string> NamedViewAssignments, bool UseDedicatedDetailLayer,
            Guid? DetailLayerId, Guid? AppearanceStateId,
            IReadOnlyDictionary<Guid, Guid> DetailAppearanceStateAssignments)>();
        {
            foreach (var spec in request.CreationSpecs ?? [])
            {
                if (spec is null)
                {
                    diagnostics.Add(SheetPlanValidation.Error("batch.specification_missing", "A creation specification is missing."));
                    continue;
                }
                if (spec.Quantity <= 0)
                {
                    diagnostics.Add(SheetPlanValidation.Error(
                        "batch.quantity_invalid", "Layout quantities must be greater than zero."));
                    continue;
                }

                var resolved = ResolveTemplate(
                    spec, templates, snapshot, request.ProjectInfo ?? snapshot.ProjectInfo, diagnostics);
                if (resolved is null)
                {
                    continue;
                }

                ValidateTemplate(resolved.Template, snapshot, resolved.NamedViewAssignments, diagnostics);
                if (!spec.UseDedicatedDetailLayer && spec.DetailLayerId is { } detailLayerId &&
                    !snapshot.Layers.ContainsKey(detailLayerId))
                    diagnostics.Add(SheetPlanValidation.Error(
                        "batch.detail_layer_missing", "The selected detail layer is no longer available."));
                for (var index = 0; index < spec.Quantity; index++)
                    expanded.Add((Guid.NewGuid(), resolved.Template, resolved.NamedViewAssignments,
                        spec.UseDedicatedDetailLayer,
                        spec.UseDedicatedDetailLayer ? null : spec.DetailLayerId,
                        spec.AppearanceStateId,
                        resolved.DetailAppearanceStateAssignments));
            }
        }

        var pattern = request.NamingPattern?.Trim() ?? string.Empty;
        if (pattern.Length == 0 && expanded.Select(item => item.Template.DefaultNamingPattern)
                .Distinct(StringComparer.Ordinal).Take(2).Count() > 1)
            diagnostics.Add(SheetPlanValidation.Error(
                "batch.pattern_required", "Mixed templates need one batch naming pattern."));

        var namingItems = expanded.Select(item => new NamingItem(
            item.DraftId,
            item.Template.Name,
            Tokens(snapshot, destination?.Name, item.Template, item.NamedViewAssignments))).ToArray();
        var resolvedPattern = pattern.Length == 0
            ? expanded.FirstOrDefault().Template?.DefaultNamingPattern ?? string.Empty
            : pattern;
        var nextOrder = snapshot.Sheets.Values.Where(item => item.FolderId == request.DestinationFolderId)
            .Select(item => item.Order).DefaultIfEmpty(-1).Max() + 1;
        var candidates = snapshot.Sheets.Values.Select(sheet => new NamingIndexCandidate(
                new NamingItem(sheet.PageViewId, sheet.Name, SheetTokens(snapshot, sheet, sheet.FolderId)),
                sheet.FolderId,
                sheet.Order,
                false,
                sheet.NamingBinding?.Index))
            .Concat(expanded.Select((item, index) => new NamingIndexCandidate(
                namingItems[index], request.DestinationFolderId, nextOrder + index, true)))
            .ToArray();
        var indices = NamingIndexing.Resolve(
            resolvedPattern,
            1,
            1,
            request.IndexMode,
            snapshot.RootFolderId,
            snapshot.Folders,
            candidates);
        var availableNaming = NamingIndexing.PreviewAvailable(
            resolvedPattern,
            namingItems,
            indices,
            snapshot.Sheets.Values.Select(item => item.Name));
        var naming = availableNaming.Preview;
        indices = availableNaming.Indices;
        diagnostics.AddRange(naming.Diagnostics);

        var changes = new List<OperationChange>();
        if (diagnostics.All(item => item.Severity != DiagnosticSeverity.Error))
        {
            for (var index = 0; index < expanded.Count; index++)
            {
                var namingIndex = indices[expanded[index].DraftId];
                changes.Add(new CreateSheetFromTemplateChange(
                    request.DestinationFolderId,
                    naming.Entries[index].ProposedName,
                    nextOrder + index,
                    expanded[index].Template,
                    expanded[index].NamedViewAssignments,
                    expanded[index].UseDedicatedDetailLayer,
                    namingIndex.ToString(CultureInfo.InvariantCulture),
                    request.ProjectInfo ?? snapshot.ProjectInfo,
                    request.InitialRevisions,
                    expanded[index].DetailLayerId,
                    expanded[index].AppearanceStateId,
                    resolvedPattern,
                    namingIndex,
                    expanded[index].DetailAppearanceStateAssignments));
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
        ICollection<Diagnostic> diagnostics)
    {
        if (spec.Paper is null || !double.IsFinite(spec.Paper.Width) || !double.IsFinite(spec.Paper.Height) || spec.Paper.Width <= 0 || spec.Paper.Height <= 0 || string.IsNullOrWhiteSpace(spec.Paper.UnitSystem))
        {
            diagnostics.Add(SheetPlanValidation.Error(
                "template.paper_invalid", "Choose a valid paper width, height, and unit."));
            return null;
        }

        if (!Enum.IsDefined(spec.BuiltInLayout) || spec.BuiltInTitleBlock is { } kind && !Enum.IsDefined(kind))
        {
            diagnostics.Add(SheetPlanValidation.Error("batch.kind_invalid", "Choose a valid layout and title-block kind."));
            return null;
        }

        SheetTemplateRecipe source;
        if (spec.TemplateId is { } templateId)
        {
            if (!templates.TryGetValue(templateId, out source!))
            {
                diagnostics.Add(SheetPlanValidation.Error(
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
            diagnostics.Add(SheetPlanValidation.Error(
                "template.display_mode_unresolved", "The selected detail display mode is unavailable."));
        }

        var hasDisplayModeOverrides = spec.DetailDisplayModesByDetail is not null &&
                                      spec.DetailDisplayModesByDetail.Count == source.DetailSlots.Count;
        if (spec.DetailDisplayModesByDetail is not null && !hasDisplayModeOverrides)
        {
            diagnostics.Add(SheetPlanValidation.Error(
                "template.display_mode_assignment_count",
                $"Layout '{source.Name}' has {source.DetailSlots.Count} details but received " +
                $"{spec.DetailDisplayModesByDetail.Count} detail display-mode assignments."));
        }
        else if (spec.DetailDisplayModesByDetail is not null)
        {
            foreach (var overrideId in spec.DetailDisplayModesByDetail.OfType<Guid>())
            {
                if (snapshot.DisplayModeIds.Contains(overrideId)) continue;
                diagnostics.Add(SheetPlanValidation.Error(
                    "template.display_mode_unresolved",
                    "A selected detail display-mode override is unavailable."));
            }
        }

        TitleBlockTemplateRecipe? titleBlock = null;

        AdaptiveTitleBlockLayout? adaptiveTitleBlock = null;
        if (spec.BuiltInTitleBlock is { } builtInKind)
        {
            try
            {
                adaptiveTitleBlock = AdaptiveTitleBlockLayoutSolver.Solve(
                    builtInKind, spec.Paper, projectInformation, source.DetailSlots.Count);
                titleBlock = new TitleBlockTemplateRecipe(
                    builtInKind);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                diagnostics.Add(SheetPlanValidation.Error(
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
        var detailAppearanceStateAssignments = new Dictionary<Guid, Guid>();
        if (spec.AppearanceStatesByDetail is not null)
        {
            if (spec.AppearanceStatesByDetail.Count != details.Length)
            {
                diagnostics.Add(SheetPlanValidation.Error(
                    "template.appearance_state_assignment_count",
                    $"Layout '{source.Name}' has {details.Length} details but received " +
                    $"{spec.AppearanceStatesByDetail.Count} detail appearance-state assignments."));
            }
            else
            {
                for (var index = 0; index < details.Length; index++)
                {
                    if (spec.AppearanceStatesByDetail[index] is not { } stateId) continue;
                    ValidateAppearanceState(stateId, snapshot, diagnostics);
                    detailAppearanceStateAssignments[details[index].Id] = stateId;
                }
            }
        }

        var namedViewAssignments = new Dictionary<Guid, string>();
        if (spec.NamedViewsByDetail is not null)
        {
            if (spec.NamedViewsByDetail.Count != details.Length)
            {
                diagnostics.Add(SheetPlanValidation.Error(
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
        return new ResolvedCreationSpec(
            template,
            namedViewAssignments,
            detailAppearanceStateAssignments);
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
            diagnostics.Add(SheetPlanValidation.Error(
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
        IReadOnlyDictionary<Guid, string> NamedViewAssignments,
        IReadOnlyDictionary<Guid, Guid> DetailAppearanceStateAssignments);

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
            Id: Guid.NewGuid(),
            Name: BuiltInLabel(kind),
            Paper: paper,
            DetailSlots: details,
            TitleBlock: null,
            DefaultMetadata: new Dictionary<string, string>(StringComparer.Ordinal),
            DefaultNamingPattern: "Page {index}");
    }

    private static string BuiltInLabel(BuiltInLayoutKind kind) => kind switch
    {
        BuiltInLayoutKind.Blank => "Blank",
        BuiltInLayoutKind.SingleDetail => "1 Detail — Single spread",
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
        if (template.Paper.Width <= 0 || template.Paper.Height <= 0 || string.IsNullOrWhiteSpace(template.Paper.UnitSystem))
            diagnostics.Add(SheetPlanValidation.Error("template.paper_invalid", $"Template '{template.Name}' has invalid paper settings."));
        if (template.DetailSlots.Any(slot => slot.Right <= slot.Left || slot.Top <= slot.Bottom))
            diagnostics.Add(SheetPlanValidation.Error("template.detail_bounds_invalid", $"Template '{template.Name}' contains an invalid detail rectangle."));
        if (template.DetailSlots.Any(slot => slot.PageToModelRatio is <= 0))
            diagnostics.Add(SheetPlanValidation.Error("template.detail_scale_invalid", $"Template '{template.Name}' contains an invalid detail scale."));
        foreach (var slot in template.DetailSlots)
        {
            var namedView = assignments?.GetValueOrDefault(slot.Id) ?? slot.DefaultNamedView;
            if (!string.IsNullOrWhiteSpace(namedView) && !snapshot.NamedViews.Contains(namedView))
                diagnostics.Add(SheetPlanValidation.Error("template.named_view_unresolved",
                    $"Named view '{namedView}' assigned to '{slot.Name}' is not available."));
            if (slot.DisplayModeId is { } modeId && !snapshot.DisplayModeIds.Contains(modeId))
                diagnostics.Add(new Diagnostic("template.display_mode_unresolved", DiagnosticSeverity.Warning,
                    $"The display mode for detail '{slot.Name}' is unavailable; Rhino's default will be used."));
        }
    }

    private static IReadOnlyDictionary<string, string> Tokens(
        DocumentSnapshot snapshot,
        string? folder,
        SheetTemplateRecipe template,
        IReadOnlyDictionary<Guid, string>? assignments)
    {
        var result = new Dictionary<string, string>(snapshot.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["folder"] = folder ?? string.Empty,
        };
        foreach (var pair in template.DefaultMetadata)
            result[pair.Key] = pair.Value;
        var assigned = template.DetailSlots.Select(slot => assignments?.GetValueOrDefault(slot.Id))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        result["view"] = assigned ?? template.DetailSlots.Select(slot => slot.DefaultNamedView)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        return result;
    }

    internal static IReadOnlyDictionary<string, string> SheetTokens(
        DocumentSnapshot snapshot,
        SheetSnapshot sheet,
        Guid folderId)
    {
        var result = new Dictionary<string, string>(snapshot.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["folder"] = folderId == snapshot.RootFolderId
                ? string.Empty
                : snapshot.Folders.GetValueOrDefault(folderId)?.Name ?? string.Empty,
            ["view"] = sheet.Details.FirstOrDefault()?.Name ?? string.Empty,
        };
        foreach (var pair in sheet.Metadata) result[pair.Key] = pair.Value;
        return result;
    }
}

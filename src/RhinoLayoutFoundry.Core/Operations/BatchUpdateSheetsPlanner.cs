using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Naming;

namespace RhinoLayoutFoundry.Core.Operations;

public sealed record BatchUpdateSheetsRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    IReadOnlyList<Guid> SheetPageViewIds,
    string? NamingPattern,
    int Start,
    int Step,
    double? PaperWidth,
    double? PaperHeight,
    string? PaperUnitSystem,
    Guid? DetailDisplayModeId,
    bool ChangeTitleBlock = false,
    IReadOnlyList<SheetRevisionRecord>? ReplaceRevisionSchedule = null,
    SheetRevisionRecord? AppendRevision = null,
    BuiltInTitleBlockKind? BuiltInTitleBlock = null,
    NamingIndexMode IndexMode = NamingIndexMode.PreserveCurrent,
    Guid? DestinationFolderId = null,
    bool ChangeAppearanceState = false,
    Guid? AppearanceStateId = null,
    bool ChangeDetailLayer = false,
    bool UseDedicatedDetailLayer = true,
    Guid? DetailLayerId = null,
    IReadOnlyList<BatchDetailUpdate>? DetailUpdates = null);

public sealed class BatchUpdateSheetsPlanner : IOperationPlanner<BatchUpdateSheetsRequest>
{
    private static readonly HashSet<string> SupportedPageUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        "Millimeters", "Centimeters", "Meters", "Inches", "Feet",
    };

    public OperationPlan Plan(BatchUpdateSheetsRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = new List<Diagnostic>();
        if (request.DocumentRuntimeSerialNumber != snapshot.DocumentRuntimeSerialNumber)
            diagnostics.Add(Error("batch.document_mismatch", "The active Rhino document changed."));
        if (request.SourceRevision != snapshot.Revision)
            diagnostics.Add(Error("batch.stale_revision", "The Rhino document changed while this editor was open."));

        var ids = request.SheetPageViewIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
            diagnostics.Add(Error("batch.empty_selection", "Include at least one layout."));
        foreach (var id in ids.Where(id => !snapshot.Sheets.ContainsKey(id)))
            diagnostics.Add(new Diagnostic("batch.sheet_missing", DiagnosticSeverity.Error,
                "An included layout no longer exists.", id));
        if (request.DestinationFolderId is { } destinationId && !snapshot.Folders.ContainsKey(destinationId))
            diagnostics.Add(Error("batch.destination_missing", "The destination folder no longer exists."));
        if (request.ChangeAppearanceState && request.AppearanceStateId is { } appearanceStateId &&
            snapshot.AppearanceStates.All(state => state.Id != appearanceStateId))
            diagnostics.Add(Error("batch.appearance_state_missing", "The selected appearance state is unavailable."));
        if (request.ChangeDetailLayer && !request.UseDedicatedDetailLayer &&
            request.DetailLayerId is { } detailLayerId && !snapshot.Layers.ContainsKey(detailLayerId))
            diagnostics.Add(Error("batch.detail_layer_missing", "The selected detail layer is unavailable."));

        var targetDetailIds = ids.Where(snapshot.Sheets.ContainsKey)
            .SelectMany(id => snapshot.Sheets[id].DetailIds)
            .ToHashSet();
        var detailUpdates = (request.DetailUpdates ?? [])
            .Where(update => update.ChangeNamedView || update.ChangeDisplayMode ||
                             update.ChangeAppearanceState)
            .GroupBy(update => update.DetailViewportId)
            .Select(group => group.Last())
            .ToArray();
        foreach (var update in detailUpdates)
        {
            if (!targetDetailIds.Contains(update.DetailViewportId))
                diagnostics.Add(new Diagnostic(
                    "batch.detail_missing",
                    DiagnosticSeverity.Error,
                    "A detail selected for editing is no longer part of the targeted layouts.",
                    update.DetailViewportId));
            if (update.ChangeNamedView && !string.IsNullOrWhiteSpace(update.NamedViewName) &&
                !snapshot.NamedViews.Contains(update.NamedViewName.Trim()))
                diagnostics.Add(new Diagnostic(
                    "batch.named_view_missing",
                    DiagnosticSeverity.Error,
                    "A named view selected for a detail is no longer available.",
                    update.DetailViewportId));
            if (update.ChangeDisplayMode &&
                (update.DisplayModeId is not { } detailModeId || !snapshot.DisplayModeIds.Contains(detailModeId)))
                diagnostics.Add(new Diagnostic(
                    "batch.detail_display_mode_missing",
                    DiagnosticSeverity.Error,
                    "A display mode selected for a detail is no longer available.",
                    update.DetailViewportId));
            if (update.ChangeAppearanceState && update.AppearanceStateId is { } detailStateId &&
                snapshot.AppearanceStates.All(state => state.Id != detailStateId))
                diagnostics.Add(new Diagnostic(
                    "batch.detail_appearance_state_missing",
                    DiagnosticSeverity.Error,
                    "An appearance state selected for a detail is no longer available.",
                    update.DetailViewportId));
        }

        var changesPaper = request.PaperWidth is not null || request.PaperHeight is not null ||
                           !string.IsNullOrWhiteSpace(request.PaperUnitSystem);
        if (changesPaper)
        {
            if (request.PaperWidth is not > 0 || request.PaperHeight is not > 0)
                diagnostics.Add(Error("batch.paper_invalid", "Paper width and height must both be greater than zero."));
            if (string.IsNullOrWhiteSpace(request.PaperUnitSystem) ||
                !SupportedPageUnits.Contains(request.PaperUnitSystem))
                diagnostics.Add(Error("batch.paper_unit_invalid", "Choose a supported paper unit."));
        }
        if (request.DetailDisplayModeId is { } modeId && !snapshot.DisplayModeIds.Contains(modeId))
            diagnostics.Add(Error("batch.display_mode_missing", "The selected display mode is no longer available."));
        if (request.BuiltInTitleBlock is { } selectedKind &&
            !Enum.IsDefined(selectedKind))
            diagnostics.Add(Error("batch.title_block_invalid", "Choose a valid built-in title block."));
        foreach (var id in ids.Where(snapshot.Sheets.ContainsKey))
        {
            var sheet = snapshot.Sheets[id];
            var managedKind = request.ChangeTitleBlock
                ? request.BuiltInTitleBlock
                : changesPaper ? sheet.TitleBlockBuiltInKind : null;
            if (managedKind is null) continue;
            try
            {
                AdaptiveTitleBlockLayoutSolver.Solve(managedKind.Value, new PaperRecipe(
                    request.PaperWidth ?? sheet.PageWidth,
                    request.PaperHeight ?? sheet.PageHeight,
                    request.PaperUnitSystem ?? sheet.PageUnitSystem), snapshot.ProjectInfo, sheet.Details.Count);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                diagnostics.Add(new Diagnostic("title_block.paper_too_small", DiagnosticSeverity.Error,
                    exception.Message, id));
            }
        }
        if (request.ReplaceRevisionSchedule is not null && request.AppendRevision is not null)
            diagnostics.Add(Error("batch.revision_mode", "Choose either replacement or append revision editing."));
        if (request.AppendRevision is { } revision && RevisionIsEmpty(revision))
            diagnostics.Add(Error("batch.revision_empty", "Enter at least one revision value."));

        var finalPlacement = FinalPlacement(snapshot, ids, request.DestinationFolderId);
        var pattern = request.NamingPattern?.Trim();
        var newNames = new Dictionary<Guid, string>();
        var namingBindings = new Dictionary<Guid, SheetNamingBinding>();
        var namingBindingRemovals = new HashSet<Guid>();
        if (!string.IsNullOrWhiteSpace(pattern))
        {
            var items = ids.Where(snapshot.Sheets.ContainsKey).Select(id =>
            {
                var sheet = snapshot.Sheets[id];
                return new NamingItem(id, sheet.Name,
                    BatchCreateSheetsPlanner.SheetTokens(snapshot, sheet, finalPlacement[id].FolderId));
            }).ToArray();
            var candidates = snapshot.Sheets.Values.Select(sheet =>
            {
                var placement = finalPlacement.GetValueOrDefault(
                    sheet.PageViewId, (sheet.FolderId, sheet.Order));
                return new NamingIndexCandidate(
                    new NamingItem(sheet.PageViewId, sheet.Name,
                        BatchCreateSheetsPlanner.SheetTokens(snapshot, sheet, placement.FolderId)),
                    placement.FolderId,
                    placement.Order,
                    ids.Contains(sheet.PageViewId),
                    sheet.NamingBinding?.Index);
            }).ToArray();
            var indices = NamingIndexing.Resolve(
                pattern,
                1,
                1,
                request.IndexMode,
                snapshot.RootFolderId,
                snapshot.Folders,
                candidates);
            var included = ids.ToHashSet();
            var availableNaming = NamingIndexing.PreviewAvailable(
                pattern,
                items,
                indices,
                snapshot.Sheets.Values
                    .Where(sheet => !included.Contains(sheet.PageViewId))
                    .Select(sheet => sheet.Name));
            var preview = availableNaming.Preview;
            indices = availableNaming.Indices;
            diagnostics.AddRange(preview.Diagnostics);
            foreach (var entry in preview.Entries)
            {
                newNames[entry.SheetId] = entry.ProposedName;
                var sheet = snapshot.Sheets[entry.SheetId];
                namingBindings[entry.SheetId] = LinkedSheetNaming.Attach(
                    pattern,
                    indices[entry.SheetId],
                    entry.ProposedName,
                    sheet);
            }
        }
        else if (request.DestinationFolderId is { } linkedDestination)
        {
            var folderOverrides = ids.Where(snapshot.Sheets.ContainsKey)
                .ToDictionary(id => id, _ => linkedDestination);
            var linked = LinkedSheetNaming.Preview(
                snapshot,
                folderOverrides,
                affectedSheetIds: ids.ToHashSet());
            diagnostics.AddRange(linked.Diagnostics);
            if (linked.Change is { } linkedChange)
            {
                foreach (var pair in linkedChange.NewNames) newNames[pair.Key] = pair.Value;
                foreach (var pair in linkedChange.NewBindings)
                {
                    if (pair.Value is null) namingBindingRemovals.Add(pair.Key);
                    else namingBindings[pair.Key] = pair.Value;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(pattern) && !changesPaper && request.DetailDisplayModeId is null &&
            !request.ChangeTitleBlock && request.ReplaceRevisionSchedule is null && request.AppendRevision is null &&
            request.DestinationFolderId is null && !request.ChangeAppearanceState && !request.ChangeDetailLayer &&
            detailUpdates.Length == 0)
            diagnostics.Add(Error("batch.no_changes", "Choose at least one property to change."));

        IReadOnlyList<OperationChange> changes = diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error)
            ? []
            : [new BatchUpdateSheetsChange(
                SheetPageViewIds:                 ids,
                NewNames:                 newNames,
                PaperWidth:                 request.PaperWidth,
                PaperHeight:                 request.PaperHeight,
                PaperUnitSystem:                 changesPaper ? request.PaperUnitSystem : null,
                DetailDisplayModeId:                 request.DetailDisplayModeId,
                ChangeTitleBlock:                 request.ChangeTitleBlock,
                ReplaceRevisionSchedule:                 request.ReplaceRevisionSchedule,
                AppendRevision:                 request.AppendRevision,
                BuiltInTitleBlock:                 request.BuiltInTitleBlock,
                NamingBindings:                 namingBindings.Count == 0 ? null : namingBindings,
                NamingBindingRemovals:                 namingBindingRemovals.Count == 0 ? null : namingBindingRemovals,
                DestinationFolderId:                 request.DestinationFolderId,
                ChangeAppearanceState:                 request.ChangeAppearanceState,
                AppearanceStateId:                 request.AppearanceStateId,
                ChangeDetailLayer:                 request.ChangeDetailLayer,
                UseDedicatedDetailLayer:                 request.UseDedicatedDetailLayer,
                DetailLayerId:                 request.DetailLayerId,
                DetailUpdates:                 detailUpdates.Length == 0 ? null : detailUpdates)];
        if (changes.Count > 0)
            diagnostics.Add(new Diagnostic("batch.undo_unavailable", DiagnosticSeverity.Warning,
                "Rhino does not expose native Undo for these layout properties. Foundry restores every before-value if Apply fails."));
        return new OperationPlan(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision,
            $"Update {ids.Length} layouts", changes, diagnostics);
    }

    private static Diagnostic Error(string code, string message) =>
        new(code, DiagnosticSeverity.Error, message);

    private static bool RevisionIsEmpty(SheetRevisionRecord revision) =>
        new[] { revision.Code, revision.Date, revision.Description, revision.IssuedBy, revision.CheckedBy }
            .All(string.IsNullOrWhiteSpace);

    private static IReadOnlyDictionary<Guid, (Guid FolderId, int Order)> FinalPlacement(
        DocumentSnapshot snapshot,
        IReadOnlyList<Guid> targetIds,
        Guid? destinationFolderId)
    {
        var result = snapshot.Sheets.Values.ToDictionary(
            sheet => sheet.PageViewId,
            sheet => (sheet.FolderId, sheet.Order));
        if (destinationFolderId is not { } destination) return result;
        var nextOrder = snapshot.Sheets.Values.Where(sheet => sheet.FolderId == destination)
            .Select(sheet => sheet.Order).DefaultIfEmpty(-1).Max() + 1;
        foreach (var id in targetIds.Where(snapshot.Sheets.ContainsKey))
        {
            var sheet = snapshot.Sheets[id];
            if (sheet.FolderId == destination) continue;
            result[id] = (destination, nextOrder++);
        }
        return result;
    }
}

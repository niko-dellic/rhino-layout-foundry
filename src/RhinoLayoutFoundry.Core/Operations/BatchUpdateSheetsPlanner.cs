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
    Guid? TitleBlockSourceInstanceObjectId = null);

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
        if (request.ChangeTitleBlock && request.TitleBlockSourceInstanceObjectId is { } sourceId &&
            !snapshot.TitleBlockInstances.ContainsKey(sourceId))
            diagnostics.Add(Error("batch.title_block_missing", "The selected title-block instance is no longer available."));

        var pattern = request.NamingPattern?.Trim();
        var newNames = new Dictionary<Guid, string>();
        if (!string.IsNullOrWhiteSpace(pattern))
        {
            if (request.Step == 0)
                diagnostics.Add(Error("batch.step_zero", "The naming step cannot be zero."));
            var items = ids.Where(snapshot.Sheets.ContainsKey).Select(id =>
            {
                var sheet = snapshot.Sheets[id];
                var folder = snapshot.Folders.GetValueOrDefault(sheet.FolderId)?.Name ?? string.Empty;
                var tokens = new Dictionary<string, string>(snapshot.Metadata, StringComparer.OrdinalIgnoreCase)
                {
                    ["folder"] = sheet.FolderId == snapshot.RootFolderId ? string.Empty : folder,
                    ["view"] = sheet.Details.FirstOrDefault()?.Name ?? string.Empty,
                    ["tag"] = string.Empty,
                };
                foreach (var pair in sheet.Metadata) tokens[pair.Key] = pair.Value;
                return new NamingItem(id, sheet.Name, tokens);
            }).ToArray();
            var preview = NamingEngine.Preview(new NamingRequest(pattern, items, request.Start, request.Step));
            diagnostics.AddRange(preview.Diagnostics);
            foreach (var entry in preview.Entries) newNames[entry.SheetId] = entry.ProposedName;
            var included = ids.ToHashSet();
            var existing = snapshot.Sheets.Values.Where(sheet => !included.Contains(sheet.PageViewId))
                .Select(sheet => sheet.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in preview.Entries.Where(entry => existing.Contains(entry.ProposedName)))
                diagnostics.Add(new Diagnostic("batch.name_exists", DiagnosticSeverity.Error,
                    $"A layout named '{entry.ProposedName}' already exists.", entry.SheetId));
        }

        if (string.IsNullOrWhiteSpace(pattern) && !changesPaper && request.DetailDisplayModeId is null &&
            !request.ChangeTitleBlock)
            diagnostics.Add(Error("batch.no_changes", "Choose at least one property to change."));

        IReadOnlyList<OperationChange> changes = diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error)
            ? []
            : [new BatchUpdateSheetsChange(
                ids,
                newNames,
                request.PaperWidth,
                request.PaperHeight,
                changesPaper ? request.PaperUnitSystem : null,
                request.DetailDisplayModeId,
                request.ChangeTitleBlock,
                request.TitleBlockSourceInstanceObjectId)];
        if (changes.Count > 0)
            diagnostics.Add(new Diagnostic("batch.undo_unavailable", DiagnosticSeverity.Warning,
                "Rhino does not expose native Undo for these layout properties. Foundry restores every before-value if Apply fails."));
        return new OperationPlan(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision,
            $"Update {ids.Length} layouts", changes, diagnostics);
    }

    private static Diagnostic Error(string code, string message) =>
        new(code, DiagnosticSeverity.Error, message);
}

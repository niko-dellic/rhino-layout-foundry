using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Operations;

public sealed record DuplicateFolderRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    Guid FolderId,
    string ExpectedName);

public sealed class DuplicateFolderPlanner : IOperationPlanner<DuplicateFolderRequest>
{
    public OperationPlan Plan(DuplicateFolderRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = new List<Diagnostic>();
        if (request.DocumentRuntimeSerialNumber != snapshot.DocumentRuntimeSerialNumber)
            diagnostics.Add(Error("folder.document_mismatch", "The active Rhino document changed."));
        if (request.SourceRevision != snapshot.Revision)
            diagnostics.Add(Error("folder.stale_revision", "The Rhino document changed. Refresh and try again."));
        snapshot.Folders.TryGetValue(request.FolderId, out var source);
        if (source is null || request.FolderId == snapshot.RootFolderId)
            diagnostics.Add(Error("folder.missing", "The folder no longer exists."));
        else if (!string.Equals(source.Name, request.ExpectedName, StringComparison.Ordinal))
            diagnostics.Add(Error("folder.before_value_changed", "The folder changed before duplication."));

        var changes = new List<OperationChange>();
        if (source is not null && source.ParentId is { } parentId &&
            diagnostics.All(item => item.Severity != DiagnosticSeverity.Error))
        {
            var name = UniqueCopyName(source.Name, parentId, snapshot.Folders.Values);
            var descendants = Descendants(source.Id, snapshot.Folders);
            var map = descendants.ToDictionary(id => id, _ => Guid.NewGuid());
            changes.Add(new DuplicateFolderChange(source.Id, parentId, parentId, source.Name, name, map));
            if (snapshot.Sheets.Values.Any(sheet => descendants.Contains(sheet.FolderId)))
                diagnostics.Add(new Diagnostic("folder.duplicate_undo_unavailable", DiagnosticSeverity.Warning,
                    "Rhino does not expose native Undo for duplicated layouts. Foundry removes the entire copy if duplication fails."));
        }
        return new OperationPlan(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision,
            $"Duplicate folder {request.ExpectedName}", changes, diagnostics);
    }

    private static string UniqueCopyName(
        string sourceName,
        Guid parentId,
        IEnumerable<FolderRecord> folders)
    {
        var names = folders.Where(folder => folder.ParentId == parentId)
            .Select(folder => folder.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = $"{sourceName} copy";
        if (!names.Contains(candidate)) return candidate;
        for (var index = 2; ; index++)
        {
            candidate = $"{sourceName} copy {index}";
            if (!names.Contains(candidate)) return candidate;
        }
    }

    private static HashSet<Guid> Descendants(Guid rootId, IReadOnlyDictionary<Guid, FolderRecord> folders)
    {
        var result = new HashSet<Guid> { rootId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var folder in folders.Values.Where(folder =>
                         folder.ParentId is { } parent && result.Contains(parent)))
                changed |= result.Add(folder.Id);
        }
        return result;
    }

    private static Diagnostic Error(string code, string message) =>
        new(code, DiagnosticSeverity.Error, message);
}

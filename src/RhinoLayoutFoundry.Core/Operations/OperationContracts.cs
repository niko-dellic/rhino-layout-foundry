using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Operations;

public interface IDocumentSnapshotProvider
{
    DocumentSnapshot Capture();
}

public interface IOperationPlanner<in TRequest>
{
    OperationPlan Plan(TRequest request, DocumentSnapshot snapshot);
}

public interface IDocumentMutationService
{
    Task<OperationResult> ApplyAsync(OperationPlan plan, CancellationToken cancellationToken);
}

public sealed record OperationPlan(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    string UndoDescription,
    IReadOnlyList<OperationChange> Changes,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool CanApply =>
        Changes.Count > 0 &&
        Diagnostics.All(item => item.Severity != DiagnosticSeverity.Error);
}

public abstract record OperationChange;

public sealed record RenameSheetChange(
    Guid PageViewId,
    string ExpectedName,
    string NewName) : OperationChange;

public sealed record OperationResult(
    bool Succeeded,
    IReadOnlyList<Diagnostic> Diagnostics);

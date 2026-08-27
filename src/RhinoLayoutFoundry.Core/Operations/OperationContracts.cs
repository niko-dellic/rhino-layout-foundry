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

public sealed record AddFolderChange(
    Guid FolderId,
    Guid ParentFolderId,
    string Name,
    int Order) : OperationChange;

public sealed record RenameFolderChange(
    Guid FolderId,
    Guid ParentFolderId,
    string ExpectedName,
    string NewName) : OperationChange;

public sealed record DeleteFolderChange(
    Guid FolderId,
    Guid ParentFolderId,
    string ExpectedName) : OperationChange;

public sealed record MoveSheetChange(
    Guid PageViewId,
    Guid ExpectedFolderId,
    Guid DestinationFolderId,
    int Order) : OperationChange;

public sealed record MoveFolderChange(
    Guid FolderId,
    Guid ExpectedParentFolderId,
    Guid DestinationFolderId,
    int Order) : OperationChange;

public sealed record CreateSheetChange(
    Guid DestinationFolderId,
    string Name,
    int Order) : OperationChange;

public sealed record OperationResult(
    bool Succeeded,
    IReadOnlyList<Diagnostic> Diagnostics);

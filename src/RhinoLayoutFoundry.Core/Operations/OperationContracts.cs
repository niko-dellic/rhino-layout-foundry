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
    string ExpectedName,
    IReadOnlyList<Guid>? DescendantFolderIds = null,
    IReadOnlyList<Guid>? SheetPageViewIds = null) : OperationChange;

public sealed record DuplicateFolderChange(
    Guid SourceFolderId,
    Guid DestinationParentFolderId,
    string ExpectedName,
    string NewName,
    IReadOnlyDictionary<Guid, Guid> FolderIdMap) : OperationChange;

public sealed record DeleteSheetChange(
    Guid PageViewId,
    Guid ExpectedFolderId,
    string ExpectedName) : OperationChange;

public sealed record DuplicateSheetChange(
    Guid PageViewId,
    Guid ExpectedFolderId,
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

public sealed record CaptureSheetTemplateChange(
    Guid TemplateId,
    Guid SourcePageViewId,
    string Name,
    string DefaultNamingPattern,
    Guid? TitleBlockInstanceObjectId) : OperationChange;

public sealed record DeleteSheetTemplateChange(
    Guid TemplateId,
    string ExpectedName) : OperationChange;

public sealed record CreateSheetFromTemplateChange(
    Guid DestinationFolderId,
    string Name,
    int Order,
    SheetTemplateRecipe Template,
    IReadOnlyDictionary<Guid, string> NamedViewAssignments) : OperationChange;

public sealed record BatchUpdateSheetsChange(
    IReadOnlyList<Guid> SheetPageViewIds,
    IReadOnlyDictionary<Guid, string> NewNames,
    double? PaperWidth,
    double? PaperHeight,
    string? PaperUnitSystem,
    Guid? DetailDisplayModeId,
    bool ChangeTitleBlock = false,
    Guid? TitleBlockSourceInstanceObjectId = null) : OperationChange;

public sealed record UpdateDetailDisplayModesChange(
    IReadOnlyList<Guid> DetailViewportIds,
    Guid DisplayModeId) : OperationChange;

public sealed record SetPrintInclusionChange(
    IReadOnlyDictionary<Guid, bool> ExpectedValues,
    bool IncludeInPrintAll) : OperationChange;

public sealed record OperationResult(
    bool Succeeded,
    IReadOnlyList<Diagnostic> Diagnostics);

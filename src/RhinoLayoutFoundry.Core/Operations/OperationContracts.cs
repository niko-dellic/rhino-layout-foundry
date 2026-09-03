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

public interface IModelObjectSelectionService
{
    ModelObjectSelectionResult PickObjects();
}

public sealed record ModelObjectSelectionResult(
    bool Succeeded,
    bool Cancelled,
    IReadOnlyList<Guid> ObjectIds,
    string Message);

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

public sealed record CreateNamedViewChange(NamedViewDefinition Definition) : OperationChange;

public sealed record CreateClippingPlaneChange(ClippingPlaneDefinition Definition) : OperationChange;

public sealed record RenameSheetChange(
    Guid PageViewId,
    string ExpectedName,
    string NewName,
    bool DetachNamingBinding = true) : OperationChange;

public sealed record UpdateLinkedSheetNamesChange(
    IReadOnlyDictionary<Guid, string> ExpectedNames,
    IReadOnlyDictionary<Guid, string> NewNames,
    IReadOnlyDictionary<Guid, SheetNamingBinding?> NewBindings) : OperationChange;

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
    Guid ExpectedParentFolderId,
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
    Guid DestinationFolderId,
    string ExpectedName) : OperationChange;

public sealed record PlacePastedHierarchyOnCanvasChange(
    ObserverPointRecord TargetOrigin) : OperationChange;

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

public sealed record HierarchyFolderPlacement(
    Guid FolderId,
    Guid? ParentFolderId,
    int Order);

public sealed record HierarchySheetPlacement(
    Guid PageViewId,
    Guid FolderId,
    int Order);

public sealed record ReorganizeHierarchyChange(
    IReadOnlyList<HierarchyFolderPlacement> ExpectedFolders,
    IReadOnlyList<HierarchySheetPlacement> ExpectedSheets,
    IReadOnlyList<HierarchyFolderPlacement> NewFolders,
    IReadOnlyList<HierarchySheetPlacement> NewSheets) : OperationChange;

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
    IReadOnlyDictionary<Guid, string> NamedViewAssignments,
    bool UseDedicatedDetailLayer = true,
    string SheetNumber = "",
    ProjectInformation? ProjectData = null,
    IReadOnlyList<SheetRevisionRecord>? InitialRevisions = null,
    Guid? DetailLayerId = null,
    Guid? AppearanceStateId = null,
    string NamingPattern = "",
    int NamingIndex = 0) : OperationChange;

public sealed record UpdateProjectInformationChange(
    ProjectInformation ExpectedInformation,
    ProjectInformation NewInformation) : OperationChange;

public sealed record BatchUpdateSheetsChange(
    IReadOnlyList<Guid> SheetPageViewIds,
    IReadOnlyDictionary<Guid, string> NewNames,
    double? PaperWidth,
    double? PaperHeight,
    string? PaperUnitSystem,
    Guid? DetailDisplayModeId,
    bool ChangeTitleBlock = false,
    Guid? TitleBlockSourceInstanceObjectId = null,
    IReadOnlyList<SheetRevisionRecord>? ReplaceRevisionSchedule = null,
    SheetRevisionRecord? AppendRevision = null,
    BuiltInTitleBlockKind? BuiltInTitleBlock = null,
    IReadOnlyDictionary<Guid, SheetNamingBinding>? NamingBindings = null,
    IReadOnlySet<Guid>? NamingBindingRemovals = null,
    Guid? DestinationFolderId = null,
    bool ChangeAppearanceState = false,
    Guid? AppearanceStateId = null,
    bool ChangeDetailLayer = false,
    bool UseDedicatedDetailLayer = true,
    Guid? DetailLayerId = null) : OperationChange;

public sealed record UpdateDetailDisplayModesChange(
    IReadOnlyList<Guid> DetailViewportIds,
    Guid DisplayModeId) : OperationChange;

public sealed record SetPrintInclusionChange(
    IReadOnlyDictionary<Guid, bool> ExpectedValues,
    bool IncludeInPrintAll) : OperationChange;

public sealed record UpdateHierarchyNotesChange(
    IReadOnlyDictionary<Guid, string> ExpectedFolderNotes,
    IReadOnlyDictionary<Guid, string> NewFolderNotes,
    IReadOnlyDictionary<Guid, string> ExpectedSheetNotes,
    IReadOnlyDictionary<Guid, string> NewSheetNotes) : OperationChange;

public sealed record SetObserverCanvasStateChange(
    ObserverCanvasState ExpectedState,
    ObserverCanvasState NewState) : OperationChange;

public sealed record SetHierarchyViewportRulesChange(
    HierarchyScope Scope,
    HierarchyViewportRuleSet? ExpectedRules,
    HierarchyViewportRuleSet? NewRules) : OperationChange;

public sealed record SetTemplateCapabilitiesChange(
    HierarchyScope Source,
    CapabilityTemplateRegistration? ExpectedRegistration,
    CapabilityTemplateRegistration? NewRegistration) : OperationChange;

public sealed record SetCapabilityTemplateLinkChange(
    HierarchyScope Target,
    TemplateCapability Capability,
    CapabilityTemplateLink? ExpectedLink,
    CapabilityTemplateLink? NewLink) : OperationChange;

public sealed record SetAppearanceStateResourceChange(
    Guid StateId,
    AppearanceStateRecord? ExpectedState,
    AppearanceStateRecord? NewState) : OperationChange;

public sealed record SetAppearanceStateAssignmentChange(
    HierarchyScope Target,
    AppearanceStateAssignment? ExpectedAssignment,
    AppearanceStateAssignment? NewAssignment) : OperationChange;

public sealed record AssignNamedViewToDetailsChange(
    IReadOnlyList<Guid> DetailViewportIds,
    string NamedViewName) : OperationChange;

public sealed record ReorderSheetsChange(
    Guid FolderId,
    IReadOnlyDictionary<Guid, int> ExpectedOrders,
    IReadOnlyDictionary<Guid, int> NewOrders) : OperationChange;

public sealed record OperationResult(
    bool Succeeded,
    IReadOnlyList<Diagnostic> Diagnostics);

namespace RhinoLayoutFoundry.Core.Domain;

public sealed record DocumentSnapshot(
    uint DocumentRuntimeSerialNumber,
    long Revision,
    Guid RootFolderId,
    IReadOnlyDictionary<Guid, FolderRecord> Folders,
    IReadOnlyDictionary<Guid, SheetSnapshot> Sheets,
    IReadOnlySet<Guid> ExistingObjectIds,
    IReadOnlySet<Guid> DisplayModeIds);

public sealed record SheetSnapshot(
    Guid PageViewId,
    Guid FolderId,
    int Order,
    string Name,
    IReadOnlyList<Guid> DetailIds,
    IReadOnlyDictionary<string, string> Metadata);

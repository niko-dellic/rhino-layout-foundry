namespace RhinoLayoutFoundry.Core.Domain;

public enum HierarchySelectorKind
{
    Folder,
    Sheet,
    Detail,
}

public sealed record HierarchySelector(HierarchySelectorKind Kind, Guid Id);

public sealed record DisplayRule(
    Guid Id,
    string Name,
    bool Enabled,
    int Priority,
    IReadOnlyList<Guid> ObjectIds,
    IReadOnlyList<HierarchySelector> Targets,
    Guid DisplayModeId);

public readonly record struct ObjectDetailKey(Guid ObjectId, Guid DetailId);


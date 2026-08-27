namespace RhinoLayoutFoundry.Core.Overview;

[Flags]
public enum OverviewInvalidationKind
{
    None = 0,
    DocumentIdentity = 1 << 0,
    Hierarchy = 1 << 1,
    Metadata = 1 << 2,
    Diagnostics = 1 << 3,
    Thumbnails = 1 << 4,
    ActiveView = 1 << 5,
    All = DocumentIdentity | Hierarchy | Metadata | Diagnostics | Thumbnails | ActiveView,
}

public sealed record OverviewInvalidation(
    uint? DocumentRuntimeSerialNumber,
    OverviewInvalidationKind Kind,
    IReadOnlySet<Guid>? EntityIds = null)
{
    public static OverviewInvalidation All { get; } = new(
        null,
        OverviewInvalidationKind.All);

    public IReadOnlySet<Guid> AffectedEntityIds => EntityIds ?? EmptyIds;

    private static IReadOnlySet<Guid> EmptyIds { get; } = new HashSet<Guid>();

    public OverviewInvalidation Merge(OverviewInvalidation other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var serial = DocumentRuntimeSerialNumber == other.DocumentRuntimeSerialNumber
            ? DocumentRuntimeSerialNumber
            : null;
        var ids = AffectedEntityIds.Count == 0 && other.AffectedEntityIds.Count == 0
            ? null
            : AffectedEntityIds.Concat(other.AffectedEntityIds).ToHashSet();
        return new OverviewInvalidation(serial, Kind | other.Kind, ids);
    }
}

public sealed class OverviewInvalidationEventArgs : EventArgs
{
    public OverviewInvalidationEventArgs(OverviewInvalidation invalidation)
    {
        Invalidation = invalidation ?? throw new ArgumentNullException(nameof(invalidation));
    }

    public OverviewInvalidation Invalidation { get; }
}

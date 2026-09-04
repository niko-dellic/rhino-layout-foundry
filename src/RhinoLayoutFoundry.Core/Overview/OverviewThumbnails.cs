using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Overview;

public readonly record struct OverviewThumbnailKey(
    uint DocumentRuntimeSerialNumber,
    Guid SheetPageViewId,
    int Width,
    int Height,
    long ContentVersion = 0,
    int ResolutionBucket = 0,
    uint BackgroundArgb = 0);

public sealed record OverviewThumbnailRequest(
    OverviewThumbnailKey Key,
    int Priority);

public sealed record OverviewThumbnailResult(
    OverviewThumbnailKey Key,
    byte[]? PngBytes,
    string? Error = null)
{
    public bool Succeeded => PngBytes is { Length: > 0 } && Error is null;
}

public static class ObserverThumbnailResolution
{
    public static readonly int[] Buckets = [256, 512, 1024, 2048];

    public static int Select(double longestVisibleEdgePixels, int currentBucket = 0)
    {
        var required = longestVisibleEdgePixels switch
        {
            <= 192 => 256,
            <= 384 => 512,
            <= 768 => 1024,
            _ => 2048,
        };

        if (Buckets.Contains(currentBucket))
        {
            // A small hysteresis band prevents repeated requests when zooming
            // close to a resolution boundary.
            var lower = currentBucket * 0.32;
            var upper = currentBucket * 0.82;
            if (longestVisibleEdgePixels >= lower && longestVisibleEdgePixels <= upper)
            {
                return currentBucket;
            }
        }

        return required;
    }
}

public interface IDocumentThumbnailProvider
{
    Task<OverviewThumbnailResult> CaptureAsync(
        OverviewThumbnailRequest request,
        CancellationToken cancellationToken);
}

public readonly record struct NamedViewThumbnailKey(
    uint DocumentRuntimeSerialNumber,
    string NamedViewName,
    int Width,
    int Height,
    long ContentVersion = 0,
    Guid? DisplayModeId = null,
    Guid? AppearanceStateId = null,
    Guid? AppearanceScopeId = null,
    Guid? DetailSlotId = null,
    uint BackgroundArgb = 0);

public sealed record NamedViewThumbnailRequest(
    NamedViewThumbnailKey Key,
    EffectiveViewportAppearance? Appearance = null);

public sealed record NamedViewThumbnailResult(
    NamedViewThumbnailKey Key,
    byte[]? PngBytes,
    string? Error = null)
{
    public bool Succeeded => PngBytes is { Length: > 0 } && Error is null;
}

public interface INamedViewThumbnailProvider
{
    Task<NamedViewThumbnailResult> CaptureAsync(
        NamedViewThumbnailRequest request,
        CancellationToken cancellationToken);
}

public readonly record struct DraftLayoutThumbnailKey(
    uint DocumentRuntimeSerialNumber,
    Guid DraftId,
    int Width,
    int Height,
    long ContentVersion,
    uint BackgroundArgb = 0);

public sealed record DraftLayoutThumbnailRequest(
    DraftLayoutThumbnailKey Key,
    CreateSheetFromTemplateChange Change);

public sealed record DraftLayoutThumbnailResult(
    DraftLayoutThumbnailKey Key,
    byte[]? PngBytes,
    string? Error = null)
{
    public bool Succeeded => PngBytes is { Length: > 0 } && Error is null;
}

public readonly record struct EditSheetThumbnailKey(
    uint DocumentRuntimeSerialNumber,
    Guid SheetPageViewId,
    int Width,
    int Height,
    long ContentVersion,
    uint BackgroundArgb = 0);

public sealed record EditDetailPreviewAssignment(
    Guid DetailViewportId,
    string? NamedViewName,
    Guid? DisplayModeId,
    Guid? AppearanceStateId,
    bool ChangeNamedView = false);

public sealed record EditSheetThumbnailRequest(
    EditSheetThumbnailKey Key,
    Guid FolderId,
    Guid? SheetAppearanceStateId,
    IReadOnlyList<EditDetailPreviewAssignment> DetailAssignments);

public sealed record EditSheetThumbnailResult(
    EditSheetThumbnailKey Key,
    byte[]? PngBytes,
    string? Error = null)
{
    public bool Succeeded => PngBytes is { Length: > 0 } && Error is null;
}

public interface IDraftLayoutThumbnailProvider
{

    Task<DraftLayoutThumbnailResult> CaptureAsync(
        DraftLayoutThumbnailRequest request,
        CancellationToken cancellationToken);

    Task<EditSheetThumbnailResult> CaptureEditAsync(
        EditSheetThumbnailRequest request,
        CancellationToken cancellationToken);

    Task WaitForPendingCapturesAsync(
        CancellationToken cancellationToken = default);
}

public sealed class OverviewThumbnailRequestQueue
{
    private readonly Dictionary<OverviewThumbnailKey, OverviewThumbnailRequest> _pending = [];
    private readonly HashSet<OverviewThumbnailKey> _inFlight = [];

    public int PendingCount => _pending.Count;

    public void Enqueue(OverviewThumbnailRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_inFlight.Contains(request.Key))
        {
            return;
        }

        if (!_pending.TryGetValue(request.Key, out var existing) ||
            request.Priority < existing.Priority)
        {
            _pending[request.Key] = request;
        }
    }

    public OverviewThumbnailRequest? TakeNext()
    {
        var next = _pending.Values
            .OrderBy(request => request.Priority)
            .ThenBy(request => request.Key.SheetPageViewId)
            .FirstOrDefault();
        if (next is null)
        {
            return null;
        }

        _pending.Remove(next.Key);
        _inFlight.Add(next.Key);
        return next;
    }

    public void Complete(OverviewThumbnailKey key)
    {
        _inFlight.Remove(key);
    }

    public void RetainPending(Func<OverviewThumbnailKey, bool> retain)
    {
        ArgumentNullException.ThrowIfNull(retain);
        foreach (var key in _pending.Keys.Where(key => !retain(key)).ToArray())
            _pending.Remove(key);
    }

    public void RemoveDocument(uint documentRuntimeSerialNumber)
    {
        foreach (var key in _pending.Keys
                     .Where(key => key.DocumentRuntimeSerialNumber == documentRuntimeSerialNumber)
                     .ToArray())
        {
            _pending.Remove(key);
        }

        _inFlight.RemoveWhere(key =>
            key.DocumentRuntimeSerialNumber == documentRuntimeSerialNumber);
    }

    public void Clear()
    {
        _pending.Clear();
        _inFlight.Clear();
    }
}

public sealed class OverviewThumbnailCache
{
    private readonly Dictionary<OverviewThumbnailKey, CacheEntry> _entries = [];
    private readonly int _maximumEntryCount;
    private readonly long _maximumByteCount;
    private long _accessSequence;

    public OverviewThumbnailCache(
        int maximumEntryCount = 96,
        long maximumByteCount = 24 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntryCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumByteCount);
        _maximumEntryCount = maximumEntryCount;
        _maximumByteCount = maximumByteCount;
    }

    public int Count => _entries.Count;

    public long ByteCount { get; private set; }

    public bool TryGet(OverviewThumbnailKey key, out byte[] bytes)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            entry.LastAccess = ++_accessSequence;
            bytes = entry.Bytes;
            return true;
        }

        bytes = [];
        return false;
    }

    public void Store(OverviewThumbnailKey key, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0 || bytes.LongLength > _maximumByteCount)
        {
            return;
        }

        if (_entries.Remove(key, out var previous))
        {
            ByteCount -= previous.Bytes.LongLength;
        }

        _entries[key] = new CacheEntry(bytes, ++_accessSequence);
        ByteCount += bytes.LongLength;
        Trim();
    }

    public void Invalidate(uint documentRuntimeSerialNumber, IReadOnlySet<Guid>? sheetIds = null)
    {
        foreach (var pair in _entries
                     .Where(pair =>
                         pair.Key.DocumentRuntimeSerialNumber == documentRuntimeSerialNumber &&
                         (sheetIds is null || sheetIds.Count == 0 || sheetIds.Contains(pair.Key.SheetPageViewId)))
                     .ToArray())
        {
            _entries.Remove(pair.Key);
            ByteCount -= pair.Value.Bytes.LongLength;
        }
    }

    public void Clear()
    {
        _entries.Clear();
        ByteCount = 0;
    }

    private void Trim()
    {
        while (_entries.Count > _maximumEntryCount || ByteCount > _maximumByteCount)
        {
            var oldest = _entries.MinBy(pair => pair.Value.LastAccess);
            _entries.Remove(oldest.Key);
            ByteCount -= oldest.Value.Bytes.LongLength;
        }
    }

    private sealed class CacheEntry(byte[] bytes, long lastAccess)
    {
        public byte[] Bytes { get; } = bytes;

        public long LastAccess { get; set; } = lastAccess;
    }
}

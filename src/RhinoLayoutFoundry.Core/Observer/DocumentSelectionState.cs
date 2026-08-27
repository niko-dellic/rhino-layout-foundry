using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Observer;

public sealed class DocumentSelectionState
{
    private readonly HashSet<OverviewNodeKey> _selected = [];

    public uint? DocumentRuntimeSerialNumber { get; private set; }
    public IReadOnlySet<OverviewNodeKey> Selected => _selected;
    public OverviewNodeKey? Anchor { get; private set; }
    public long Version { get; private set; }

    public event EventHandler<DocumentSelectionChangedEventArgs>? Changed;

    public void Replace(
        uint? documentRuntimeSerialNumber,
        IEnumerable<OverviewNodeKey> selection,
        OverviewNodeKey? anchor = null,
        object? source = null)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var next = selection.ToHashSet();
        OverviewNodeKey? nextAnchor = anchor is { } candidate && next.Contains(candidate)
            ? candidate
            : next.Count == 1 ? next.Single() : (OverviewNodeKey?)null;
        if (DocumentRuntimeSerialNumber == documentRuntimeSerialNumber &&
            _selected.SetEquals(next) && Anchor == nextAnchor)
        {
            return;
        }

        DocumentRuntimeSerialNumber = documentRuntimeSerialNumber;
        _selected.Clear();
        _selected.UnionWith(next);
        Anchor = nextAnchor;
        Version++;
        Changed?.Invoke(this, new DocumentSelectionChangedEventArgs(
            documentRuntimeSerialNumber,
            _selected.ToArray(),
            Anchor,
            Version,
            source));
    }

    public void Clear(uint? documentRuntimeSerialNumber, object? source = null) =>
        Replace(documentRuntimeSerialNumber, [], null, source);
}

public sealed class DocumentSelectionChangedEventArgs : EventArgs
{
    public DocumentSelectionChangedEventArgs(
        uint? documentRuntimeSerialNumber,
        IReadOnlyList<OverviewNodeKey> selection,
        OverviewNodeKey? anchor,
        long version,
        object? source)
    {
        DocumentRuntimeSerialNumber = documentRuntimeSerialNumber;
        Selection = selection;
        Anchor = anchor;
        Version = version;
        Source = source;
    }

    public uint? DocumentRuntimeSerialNumber { get; }
    public IReadOnlyList<OverviewNodeKey> Selection { get; }
    public OverviewNodeKey? Anchor { get; }
    public long Version { get; }
    public object? Source { get; }
}

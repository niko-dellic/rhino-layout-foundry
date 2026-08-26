using Rhino;

namespace RhinoLayoutFoundry.Rhino;

internal sealed class DocumentRevisionTracker
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<uint, long> _revisions = new();

    public long Current(RhinoDoc document)
    {
        ArgumentNullException.ThrowIfNull(document);

        lock (_syncRoot)
        {
            return _revisions.TryGetValue(document.RuntimeSerialNumber, out var revision)
                ? revision
                : 0;
        }
    }

    public long Bump(RhinoDoc document)
    {
        ArgumentNullException.ThrowIfNull(document);

        lock (_syncRoot)
        {
            var serialNumber = document.RuntimeSerialNumber;
            var next = CurrentUnsafe(serialNumber) + 1;
            _revisions[serialNumber] = next;
            return next;
        }
    }

    public void Remove(RhinoDoc document)
    {
        ArgumentNullException.ThrowIfNull(document);

        lock (_syncRoot)
        {
            _revisions.Remove(document.RuntimeSerialNumber);
        }
    }

    private long CurrentUnsafe(uint serialNumber)
    {
        return _revisions.TryGetValue(serialNumber, out var revision) ? revision : 0;
    }
}

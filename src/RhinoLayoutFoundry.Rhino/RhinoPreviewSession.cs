using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Rhino;

/// <summary>
/// Owns transient pages immediately, including partially constructed pages.
/// Never resets Modified: Rhino finalizes page destruction asynchronously and
/// cannot distinguish those notifications from a subsequent user's edit.
/// </summary>
internal sealed class RhinoPreviewSession : IDisposable
{
    private readonly CompensationJournal _cleanup = new();
    private bool _disposed;

    internal RhinoPreviewSession(RhinoDoc document)
    {
        var transient = RhinoThumbnailCaptureGate.BeginTransientDocumentChanges();
        var undoEnabled = document.UndoRecordingEnabled;
        var activeView = document.Views.ActiveView;
        _cleanup.Register("End transient preview", transient.Dispose);
        _cleanup.Register("Restore Undo recording", () => document.UndoRecordingEnabled = undoEnabled);
        _cleanup.Register("Redraw document", () => document.Views.Redraw());
        _cleanup.Register("Restore active view", () =>
        {
            if (activeView is not null) document.Views.ActiveView = activeView;
        });
        try
        {
            var definitions = document.InstanceDefinitions.Select(item => item.Id).ToHashSet();
            _cleanup.Register("Remove preview block definitions", () =>
            {
                var cleanup = new CompensationJournal();
                foreach (var definition in document.InstanceDefinitions.Where(item => !item.IsDeleted && !definitions.Contains(item.Id)).ToArray())
                    cleanup.Register(definition.Name, () =>
                    {
                        if (!document.InstanceDefinitions.Delete(definition.Index, false, true))
                            throw new InvalidOperationException("Rhino retained a temporary block definition.");
                    });
                var failures = cleanup.Rollback();
                if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));
            });
            document.UndoRecordingEnabled = false;
        }
        catch { Dispose(); throw; }
    }

    internal void Own(RhinoPageView page) => _cleanup.Register("Remove temporary preview page", () =>
    {
        if (!page.Close()) throw new InvalidOperationException("Rhino did not close the temporary page.");
    });

    internal void Restore(string label, Action restore) => _cleanup.Register(label, restore);

    internal static void RestoreAppearance(
        RhinoDoc document,
        IReadOnlyDictionary<Guid, Layer> layerBefore,
        IReadOnlyDictionary<Guid, ObjectAttributes> objectBefore)
    {
        var cleanup = new CompensationJournal();
        foreach (var pair in layerBefore)
            cleanup.Register($"Layer {pair.Key}", () =>
            {
                using var saved = pair.Value;
                var source = document.Layers.FindId(pair.Key);
                if (source is not null && !document.Layers.Modify(saved, source.Index, quiet: true))
                    throw new InvalidOperationException("Rhino rejected layer restoration.");
            });
        foreach (var pair in objectBefore)
            cleanup.Register($"Object {pair.Key}", () =>
            {
                using var saved = pair.Value;
                var item = document.Objects.FindId(pair.Key);
                if (item is not null && !document.Objects.ModifyAttributes(item, saved, quiet: true))
                    throw new InvalidOperationException("Rhino rejected object appearance restoration.");
            });
        var failures = cleanup.Rollback();
        if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var failures = _cleanup.Rollback();
        if (failures.Count > 0)
            throw new InvalidOperationException("Preview cleanup was incomplete: " + string.Join("; ", failures));
    }
}

using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.FileIO;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Rhino;

/// <summary>Compensates every resource family touched by package import, including overwritten named resources.</summary>
internal sealed class RhinoImportTransaction : IDisposable
{
    private readonly CompensationJournal _journal = new();
    private readonly string _snapshotPath;
    private readonly HashSet<Guid> _layers = [];
    private readonly HashSet<Guid> _objects = [];
    private readonly List<IDisposable> _attributeSnapshots = [];
    private readonly Dictionary<Guid, DisplayModeDescription> _originalModes;

    internal RhinoImportTransaction(RhinoDoc document, DocumentStateStore store, byte[] asset)
    {
        _snapshotPath = Path.Combine(Path.GetTempPath(), $"Foundry-import-before-{Guid.NewGuid():N}.3dm");
        File.WriteAllBytes(_snapshotPath, asset);
        _originalModes = DisplayModeDescription.GetDisplayModes().ToDictionary(mode => mode.Id);
        _journal.Register("Restore metadata", store.CaptureRestoreAction(document));
        _journal.Register("Restore named views", () =>
        {
            using var file = File3dm.Read(_snapshotPath) ?? throw new IOException("Cannot read the recovery snapshot.");
            var actions = new CompensationJournal();
            foreach (var view in file.NamedViews)
                actions.Register($"Restore named view {view.Name}", () => Require(document.NamedViews.Add(view) >= 0));
            foreach (var name in document.NamedViews.Select(view => view.Name).ToArray())
                actions.Register($"Remove named view {name}", () => Require(document.NamedViews.Delete(name)));
            RequireComplete(actions.Rollback());
        });
        var layerStateNames = document.NamedLayerStates.Names.ToArray();
        _journal.Register("Restore named layer states", () =>
        {
            var actions = new CompensationJournal();
            actions.Register("Import original layer states", () =>
            {
                if (layerStateNames.Length > 0)
                    Require(document.NamedLayerStates.Import(_snapshotPath) == layerStateNames.Length);
            });
            foreach (var name in document.NamedLayerStates.Names.ToArray())
                actions.Register($"Remove layer state {name}", () => Require(document.NamedLayerStates.Delete(name)));
            RequireComplete(actions.Rollback());
        });
        TrackNew("linetypes", () => document.Linetypes.Where(item => !item.IsDeleted), item => item.Id,
            item => document.Linetypes.Delete(item.Index, true));
        TrackNew("materials", () => document.Materials.Where(item => !item.IsDeleted), item => item.Id,
            item => document.Materials.DeleteAt(item.Index));
        TrackNew("dimension styles", () => document.DimStyles.Where(item => !item.IsDeleted), item => item.Id,
            item => document.DimStyles.Delete(item.Index, true));
        TrackNew("hatch patterns", () => document.HatchPatterns.Where(item => !item.IsDeleted), item => item.Id,
            item => document.HatchPatterns.Delete(item, true));
        TrackNew("block definitions", () => document.InstanceDefinitions.Where(item => !item.IsDeleted), item => item.Id,
            item => document.InstanceDefinitions.Delete(item.Index, false, true));
    }

    private void TrackNew<T>(string label, Func<IEnumerable<T>> current, Func<T, Guid> identity, Func<T, bool> remove)
    {
        var original = current().Select(identity).ToHashSet();
        _journal.Register($"Remove imported {label}", () =>
        {
            var cleanup = new CompensationJournal();
            foreach (var item in current().Where(item => !original.Contains(identity(item))).ToArray())
                cleanup.Register($"{label} {identity(item)}", () => Require(remove(item)));
            RequireComplete(cleanup.Rollback());
        });
    }

    // Capture only resources actually touched, once, before their first write.
    internal void CaptureLayer(Layer layer, RhinoDoc document)
    {
        if (!_layers.Add(layer.Id)) return;
        var saved = new Layer();
        saved.CopyAttributesFrom(layer);
        _attributeSnapshots.Add(saved);
        var index = layer.Index;
        _journal.Register($"Restore layer {layer.FullPath}", () => Require(document.Layers.Modify(saved, index, true)));
    }

    internal void CaptureObject(RhinoObject item, RhinoDoc document)
    {
        if (!_objects.Add(item.Id)) return;
        var saved = item.Attributes.Duplicate();
        _attributeSnapshots.Add(saved);
        var id = item.Id;
        _journal.Register($"Restore object attributes {id}", () => Require(document.Objects.ModifyAttributes(id, saved, true)));
    }

    internal void OwnPage(RhinoPageView page) =>
        _journal.Register("Remove imported page", () => Require(page.Close()));

    internal void OwnDisplayMode(Guid id) =>
        _journal.Register("Restore display mode", () => Require(_originalModes.TryGetValue(id, out var original)
            ? DisplayModeDescription.UpdateDisplayMode(original)
            : DisplayModeDescription.DeleteDisplayMode(id)));

    internal void Commit() => _journal.Commit();
    internal IReadOnlyList<string> Rollback() => _journal.Rollback();
    private static void Require(bool succeeded)
    {
        if (!succeeded) throw new InvalidOperationException("Rhino rejected resource restoration.");
    }
    private static void RequireComplete(IReadOnlyList<string> failures)
    {
        if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));
    }
    public void Dispose()
    {
        foreach (var snapshot in _attributeSnapshots) snapshot.Dispose();
        foreach (var mode in _originalModes.Values) mode.Dispose();
        try { File.Delete(_snapshotPath); }
        catch (IOException exception) { RhinoApp.WriteLine("Could not remove import snapshot: {0}", exception.Message); }
        catch (UnauthorizedAccessException exception) { RhinoApp.WriteLine("Could not remove import snapshot: {0}", exception.Message); }
    }
}

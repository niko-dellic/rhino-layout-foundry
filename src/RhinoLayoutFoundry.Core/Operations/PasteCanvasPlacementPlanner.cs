using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Observer;

namespace RhinoLayoutFoundry.Core.Operations;

public sealed class PasteCanvasPlacementPlanner
{
    public ObserverCanvasState Place(
        ObserverSnapshot snapshot,
        IEnumerable<Guid> topFolderIds,
        IEnumerable<Guid> standaloneSheetIds,
        ObserverPointRecord targetOrigin)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(topFolderIds);
        ArgumentNullException.ThrowIfNull(standaloneSheetIds);
        var folders = topFolderIds.Distinct().ToArray();
        var sheets = standaloneSheetIds.Distinct().ToArray();
        var placement = new ObserverPlacementPlanner();
        var layout = placement.Arrange(snapshot);
        var bounds = folders
            .Where(layout.Folders.ContainsKey)
            .Select(id => layout.Folders[id].Bounds)
            .Concat(sheets
                .Where(layout.Sheets.ContainsKey)
                .Select(id => layout.Sheets[id].Bounds))
            .Aggregate(new ObserverRect(), ObserverRect.Union);
        if (bounds.IsEmpty) return snapshot.CanvasState;

        var delta = new ObserverPoint(targetOrigin.X - bounds.X, targetOrigin.Y - bounds.Y);
        var canvas = snapshot.CanvasState;
        foreach (var folderId in folders)
        {
            canvas = placement.MoveFolder(snapshot with { CanvasState = canvas }, folderId, delta);
        }
        if (sheets.Length > 0)
        {
            snapshot = snapshot with { CanvasState = canvas };
            layout = placement.Arrange(snapshot);
            canvas = placement.MoveSheets(snapshot, layout, sheets, delta);
        }
        return canvas;
    }
}

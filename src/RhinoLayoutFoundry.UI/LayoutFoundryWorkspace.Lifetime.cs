using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

internal sealed partial class LayoutFoundryWorkspace
{
    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            OnPanelUnloaded(this, EventArgs.Empty);
            // Companion views own their sessions; detach without disposing them.
            _extensionWorkspace.Content = null;
            _extensionContent = null;
            _thumbnailView.Dispose();
            _observerView.Dispose();
            _thumbnailCancellation.Cancel();
            _thumbnailCancellation.Dispose();
            _layoutPollTimer.Dispose();
            _invalidationTimer.Dispose();
            _responsiveTimer.Dispose();
            _thumbnailTimer.Dispose();

            // Eto's native custom-cell reuse views retain CellEventArgs, including
            // the cell and grid. Remove callbacks and the parent/event property
            // graph before releasing the detached grid, so those native wrappers
            // cannot retain this workspace and its document snapshots.
            _treeGrid.DataStore = null;
            _treeGrid.ContextMenu = null;
            var cells = _treeGrid.Columns.Select(column => column.DataCell).Distinct().ToArray();
            foreach (var cell in cells.OfType<CustomCell>())
            {
                cell.GetIdentifier = null;
                cell.CreateCell = null;
                cell.ConfigureCell = null;
            }
            _treeGrid.Dispose();
            _treeGrid.Properties.Clear();
            foreach (var cell in cells)
            {
                cell.Dispose();
                cell.Properties.Clear();
            }
            _sheetItems.Clear();
            _renderedTreeItems = [];
        }
        base.Dispose(disposing);
    }
}

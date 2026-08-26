using Rhino;
using Rhino.Display;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Rhino;

internal sealed class RhinoDocumentSnapshotProvider : IDocumentSnapshotProvider
{
    private readonly DocumentRevisionTracker _revisionTracker;
    private readonly DocumentStateStore _stateStore;

    public RhinoDocumentSnapshotProvider(
        DocumentStateStore stateStore,
        DocumentRevisionTracker revisionTracker)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _revisionTracker = revisionTracker ?? throw new ArgumentNullException(nameof(revisionTracker));
    }

    public DocumentSnapshot Capture()
    {
        var document = RhinoDoc.ActiveDoc
            ?? throw new InvalidOperationException("There is no active Rhino document.");
        var state = _stateStore.Get(document);
        var fallback = DocumentState.Empty();
        var folders = state.Folders.ToDictionary(folder => folder.Id);

        if (!folders.ContainsKey(state.RootFolderId))
        {
            state = fallback;
            folders = fallback.Folders.ToDictionary(folder => folder.Id);
        }

        var sheets = document.Views.GetPageViews()
            .Select((page, index) =>
            {
                var pageId = page.MainViewport.Id;
                var record = state.Sheets.GetValueOrDefault(pageId);
                var folderId = record is not null && folders.ContainsKey(record.FolderId)
                    ? record.FolderId
                    : state.RootFolderId;
                var detailIds = page.GetDetailViews()
                    .Select(detail => detail.Viewport.Id)
                    .ToArray();

                return new SheetSnapshot(
                    pageId,
                    folderId,
                    record?.Order ?? index,
                    page.PageName,
                    detailIds,
                    record?.Metadata ?? new Dictionary<string, string>(StringComparer.Ordinal));
            })
            .ToDictionary(sheet => sheet.PageViewId);

        var objectIds = document.Objects
            .Select(item => item.Id)
            .ToHashSet();
        var displayModes = DisplayModeDescription.GetDisplayModes();
        var displayModeIds = displayModes.Select(mode => mode.Id).ToHashSet();
        foreach (var displayMode in displayModes)
        {
            displayMode.Dispose();
        }

        return new DocumentSnapshot(
            document.RuntimeSerialNumber,
            _revisionTracker.Current(document),
            state.RootFolderId,
            folders,
            sheets,
            objectIds,
            displayModeIds);
    }
}

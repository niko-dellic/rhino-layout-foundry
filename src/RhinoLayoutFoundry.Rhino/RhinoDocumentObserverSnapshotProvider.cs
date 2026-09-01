using Rhino;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Observer;

namespace RhinoLayoutFoundry.Rhino;

internal sealed class RhinoDocumentObserverSnapshotProvider : IDocumentObserverSnapshotProvider
{
    private readonly DocumentStateStore _stateStore;
    private readonly DocumentRevisionTracker _revisionTracker;

    public RhinoDocumentObserverSnapshotProvider(
        DocumentStateStore stateStore,
        DocumentRevisionTracker revisionTracker)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _revisionTracker = revisionTracker ?? throw new ArgumentNullException(nameof(revisionTracker));
    }

    public ObserverSnapshot Capture()
    {
        var document = RhinoDoc.ActiveDoc;
        if (document is null) return ObserverSnapshot.NoDocument;
        var state = _stateStore.Get(document);
        if (state.Folders.All(folder => folder.Id != state.RootFolderId))
        {
            state = DocumentState.Empty();
        }

        return Capture(document, state, _revisionTracker.Current(document));
    }

    internal static ObserverSnapshot Capture(
        RhinoDoc document,
        DocumentState state,
        long revision)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(state);

        var documentName = string.IsNullOrWhiteSpace(document.Name)
            ? "Untitled Rhino document"
            : Path.GetFileNameWithoutExtension(document.Name);
        var folderIds = state.Folders.Select(folder => folder.Id).ToHashSet();
        var folders = state.Folders
            .Select(folder => new ObserverFolderSnapshot(
                folder.Id,
                folder.ParentId,
                folder.Id == state.RootFolderId ? documentName : folder.Name,
                folder.Order,
                folder.Notes ?? string.Empty))
            .ToArray();
        var sheets = document.Views.GetPageViews()
            .OrderBy(page => page.PageNumber)
            .Select(page =>
            {
                var pageId = page.MainViewport.Id;
                var record = state.Sheets.GetValueOrDefault(pageId);
                var folderId = record is not null && folderIds.Contains(record.FolderId)
                    ? record.FolderId
                    : state.RootFolderId;
                var widthMillimeters = PaperUnitConverter.ToMillimeters(
                    page.PageWidth,
                    document.PageUnitSystem.ToString());
                var heightMillimeters = PaperUnitConverter.ToMillimeters(
                    page.PageHeight,
                    document.PageUnitSystem.ToString());
                var details = page.GetDetailViews()
                    .Select((detail, index) =>
                    {
                        var box = detail.DetailGeometry.GetBoundingBox(true);
                        var normalized = box.IsValid
                            ? ObserverDetailBounds.FromPageCoordinates(
                                box.Min.X,
                                box.Min.Y,
                                box.Max.X,
                                box.Max.Y,
                                page.PageWidth,
                                page.PageHeight)
                            : new ObserverRect(0.05, 0.05, 0.9, 0.9);
                        return new ObserverDetailSnapshot(
                            detail.Viewport.Id,
                            string.IsNullOrWhiteSpace(detail.DescriptiveTitle)
                                ? $"Detail {index + 1}"
                                : detail.DescriptiveTitle,
                            normalized,
                            detail.Viewport.DisplayMode.Id,
                            detail.Viewport.DisplayMode.LocalName);
                    })
                    .ToArray();
                return new ObserverSheetSnapshot(
                    pageId,
                    folderId,
                    page.PageName,
                    record?.Order ?? page.PageNumber,
                    Math.Max(1, widthMillimeters),
                    Math.Max(1, heightMillimeters),
                    document.PageUnitSystem.ToString(),
                    details,
                    record?.IncludeInPrintAll ?? true,
                    revision,
                    Notes: record?.Notes ?? string.Empty);
            })
            .ToArray();

        return new ObserverSnapshot(
            document.RuntimeSerialNumber,
            revision,
            documentName,
            state.RootFolderId,
            folders,
            sheets,
            state.Canvas,
            document.NamedViews
                .Select(view => view.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            state.AppearanceStates,
            state.StateAssignments);
    }
}

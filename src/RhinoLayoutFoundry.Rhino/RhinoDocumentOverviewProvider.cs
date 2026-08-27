using Rhino;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Rhino;

internal sealed class RhinoDocumentOverviewProvider : IDocumentOverviewProvider
{
    private readonly DocumentStateStore _stateStore;

    public RhinoDocumentOverviewProvider(DocumentStateStore stateStore)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    }

    public DocumentOverview Capture()
    {
        var document = RhinoDoc.ActiveDoc;
        if (document is null)
        {
            return DocumentOverview.NoDocument;
        }

        var state = _stateStore.Get(document);
        var fallback = Core.Domain.DocumentState.Empty();
        if (state.Folders.All(folder => folder.Id != state.RootFolderId))
        {
            state = fallback;
        }

        var folderIds = state.Folders.Select(folder => folder.Id).ToHashSet();
        var folders = state.Folders
            .Select(folder => new FolderOverview(folder.Id, folder.ParentId, folder.Name, folder.Order))
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
                var details = page.GetDetailViews()
                    .Select((detail, index) => new DetailOverview(
                        detail.Viewport.Id,
                        string.IsNullOrWhiteSpace(detail.DescriptiveTitle)
                            ? $"Detail {index + 1}"
                            : detail.DescriptiveTitle,
                        index))
                    .ToArray();

                return new SheetOverview(
                    pageId,
                    folderId,
                    page.PageName,
                    record?.Order ?? page.PageNumber,
                    record?.Tags ?? [],
                    details);
            })
            .ToArray();

        var documentName = string.IsNullOrWhiteSpace(document.Name)
            ? "Untitled Rhino document"
            : document.Name;

        return new DocumentOverview(
            document.RuntimeSerialNumber,
            documentName,
            state.RootFolderId,
            folders,
            sheets);
    }

    public DocumentOverviewIdentity CaptureIdentity()
    {
        var document = RhinoDoc.ActiveDoc;
        return document is null
            ? new DocumentOverviewIdentity(null, 0)
            : new DocumentOverviewIdentity(
                document.RuntimeSerialNumber,
                document.Views.GetPageViews().Length);
    }
}

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
        var pageViews = document.Views.GetPageViews()
            .OrderBy(page => page.PageNumber)
            .ToArray();
        var duplicateNames = pageViews
            .GroupBy(page => page.PageName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sheets = pageViews
            .Select(page =>
            {
                var pageId = page.MainViewport.Id;
                var record = state.Sheets.GetValueOrDefault(pageId);
                var assignedFolderExists = record is null || folderIds.Contains(record.FolderId);
                var folderId = record is not null && assignedFolderExists
                    ? record.FolderId
                    : state.RootFolderId;
                var details = page.GetDetailViews()
                    .Select((detail, index) => new DetailOverview(
                        detail.Viewport.Id,
                        string.IsNullOrWhiteSpace(detail.DescriptiveTitle)
                            ? $"Detail {index + 1}"
                            : detail.DescriptiveTitle,
                        index,
                        detail.Viewport.DisplayMode.Id,
                        detail.Viewport.DisplayMode.LocalName))
                    .ToArray();

                var sheet = new SheetOverview(
                    pageId,
                    folderId,
                    page.PageName,
                    record?.Order ?? page.PageNumber,
                    record?.Tags ?? [],
                    details,
                    PageWidth: page.PageWidth,
                    PageHeight: page.PageHeight,
                    PageUnitSystem: document.PageUnitSystem.ToString(),
                    IncludeInPrintAll: record?.IncludeInPrintAll ?? true);
                return sheet with
                {
                    Diagnostics = OverviewDiagnostics.ForSheet(
                        sheet,
                        assignedFolderExists,
                        duplicateNames.Contains(page.PageName)),
                };
            })
            .ToArray();

        var documentName = string.IsNullOrWhiteSpace(document.Name)
            ? "Untitled Rhino document"
            : Path.GetFileNameWithoutExtension(document.Name);

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

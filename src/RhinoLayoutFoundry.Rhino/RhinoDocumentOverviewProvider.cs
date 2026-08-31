using Rhino;
using Rhino.DocObjects;
using RhinoLayoutFoundry.Core.Domain;
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
        var registrations = state.TemplateRegistrations
            .GroupBy(item => item.Source)
            .ToDictionary(group => group.Key, group => group.Last().Capabilities);
        var pageViews = document.Views.GetPageViews()
            .OrderBy(page => page.PageNumber)
            .ToArray();
        var duplicateNames = pageViews
            .GroupBy(page => page.PageName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var detailAppearances = pageViews
            .SelectMany(page => page.GetDetailViews())
            .ToDictionary(
                detail => detail.Viewport.Id,
                detail => CaptureAppearance(document, detail.Viewport.Id));
        var sheets = pageViews
            .Select(page =>
            {
                var pageId = page.MainViewport.Id;
                var record = state.Sheets.GetValueOrDefault(pageId);
                var assignedFolderExists = record is null || folderIds.Contains(record.FolderId);
                var folderId = record is not null && assignedFolderExists
                    ? record.FolderId
                    : state.RootFolderId;
                var pageDetails = page.GetDetailViews();
                var details = pageDetails
                    .Select((detail, index) => new DetailOverview(
                        detail.Viewport.Id,
                        string.IsNullOrWhiteSpace(detail.DescriptiveTitle)
                            ? $"Detail {index + 1}"
                            : detail.DescriptiveTitle,
                        index,
                        detail.Viewport.DisplayMode.Id,
                        detail.Viewport.DisplayMode.LocalName,
                        registrations.GetValueOrDefault(new HierarchyScope(
                            HierarchyScopeKind.Detail, detail.Viewport.Id)),
                        detailAppearances.GetValueOrDefault(detail.Viewport.Id)))
                    .ToArray();
                var sheetCapabilities = registrations.GetValueOrDefault(
                    new HierarchyScope(HierarchyScopeKind.Sheet, pageId));

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
                    IncludeInPrintAll: record?.IncludeInPrintAll ?? true,
                    IsTemplate: sheetCapabilities != TemplateCapability.None,
                    TemplateCapabilities: sheetCapabilities,
                    Appearance: AggregateAppearance(details.Select(detail => detail.Appearance)));
                return sheet with
                {
                    Diagnostics = OverviewDiagnostics.ForSheet(
                        sheet,
                        assignedFolderExists,
                        duplicateNames.Contains(page.PageName)),
                };
            })
            .ToArray();

        var folderRecords = state.Folders.ToDictionary(folder => folder.Id);
        var folders = state.Folders
            .Select(folder =>
            {
                var descendantIds = DescendantFolderIds(folder.Id, folderRecords);
                var appearance = AggregateAppearance(sheets
                    .Where(sheet => descendantIds.Contains(sheet.FolderId))
                    .SelectMany(sheet => sheet.Details)
                    .Select(detail => detail.Appearance));
                return new FolderOverview(
                    folder.Id,
                    folder.ParentId,
                    folder.Name,
                    folder.Order,
                    registrations.GetValueOrDefault(new HierarchyScope(
                        HierarchyScopeKind.Folder, folder.Id)),
                    appearance);
            })
            .ToArray();

        var documentName = DisplayName(document);

        return new DocumentOverview(
            document.RuntimeSerialNumber,
            documentName,
            state.RootFolderId,
            folders,
            sheets,
            state.Recovery.Select(item => new OverviewIssue(
                $"import.{item.Kind}",
                OverviewIssueSeverity.Warning,
                item.Message,
                item.EntityId)).ToArray());
    }

    public DocumentOverviewIdentity CaptureIdentity()
    {
        var document = RhinoDoc.ActiveDoc;
        return document is null
            ? new DocumentOverviewIdentity(null, 0, DocumentOverview.NoDocument.DocumentName)
            : new DocumentOverviewIdentity(
                document.RuntimeSerialNumber,
                document.Views.GetPageViews().Length,
                DisplayName(document));
    }

    private static string DisplayName(RhinoDoc document) =>
        string.IsNullOrWhiteSpace(document.Name)
            ? "Untitled Rhino document"
            : Path.GetFileNameWithoutExtension(document.Name);

    private static ViewportAppearanceSummary CaptureAppearance(RhinoDoc document, Guid detailViewportId)
    {
        var visibility = document.Layers
            .Where(layer => !layer.IsDeleted && !layer.IsReference &&
                            layer.HasPerViewportSettings(detailViewportId))
            .Select(layer => layer.PerViewportIsVisible(detailViewportId))
            .ToArray();
        var objectCount = document.Objects.Count(item =>
            item is not DetailViewObject &&
            item.Attributes.Space == ActiveSpace.ModelSpace &&
            item.Attributes.HasDisplayModeOverride(detailViewportId));
        return new ViewportAppearanceSummary(
            visibility.Count(value => value),
            visibility.Count(value => !value),
            objectCount,
            visibility.Length == 0 && objectCount == 0);
    }

    private static ViewportAppearanceSummary AggregateAppearance(
        IEnumerable<ViewportAppearanceSummary?> appearances)
    {
        var values = appearances.OfType<ViewportAppearanceSummary>().ToArray();
        if (values.Length == 0) return new ViewportAppearanceSummary(0, 0, 0, true);
        var first = values[0];
        var mixed = values.Skip(1).Any(value =>
            value.VisibleLayerCount != first.VisibleLayerCount ||
            value.HiddenLayerCount != first.HiddenLayerCount ||
            value.ObjectDisplayOverrideCount != first.ObjectDisplayOverrideCount ||
            value.IsInherited != first.IsInherited);
        return mixed
            ? new ViewportAppearanceSummary(
                values.Sum(value => value.VisibleLayerCount),
                values.Sum(value => value.HiddenLayerCount),
                values.Sum(value => value.ObjectDisplayOverrideCount),
                values.All(value => value.IsInherited),
                IsMixed: true,
                values.Sum(value => value.UnresolvedCount))
            : first;
    }

    private static HashSet<Guid> DescendantFolderIds(
        Guid folderId,
        IReadOnlyDictionary<Guid, FolderRecord> folders)
    {
        var result = new HashSet<Guid> { folderId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var folder in folders.Values.Where(folder =>
                         folder.ParentId is { } parentId && result.Contains(parentId)))
                changed |= result.Add(folder.Id);
        }
        return result;
    }
}

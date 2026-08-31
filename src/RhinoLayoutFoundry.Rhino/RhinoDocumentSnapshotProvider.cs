using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
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

        var pageViews = document.Views.GetPageViews();
        var pageNames = pageViews.ToDictionary(page => page.MainViewport.Id, page => page.PageName);
        var titleBlockInstances = document.Objects
            .OfType<InstanceObject>()
            .Where(instance => instance.Attributes.Space == ActiveSpace.PageSpace &&
                               pageNames.ContainsKey(instance.Attributes.ViewportId))
            .Select(instance => new TitleBlockInstanceSnapshot(
                instance.Id,
                instance.InstanceDefinition.Id,
                instance.InstanceDefinition.Name,
                instance.Attributes.ViewportId,
                pageNames[instance.Attributes.ViewportId],
                TransformValues(instance.InstanceXform),
                state.Sheets.Values
                    .Select(sheet => sheet.TitleBlock)
                    .FirstOrDefault(role => role?.InstanceObjectId == instance.Id)?.AnchorName ?? "Template"))
            .ToDictionary(instance => instance.InstanceObjectId);

        var sheets = pageViews
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
                var detailSettings = page.GetDetailViews()
                    .Select(detail => new DetailSnapshot(
                        detail.Viewport.Id,
                        string.IsNullOrWhiteSpace(detail.DescriptiveTitle)
                            ? detail.Viewport.Name
                            : detail.DescriptiveTitle,
                        detail.Viewport.DisplayMode.Id,
                        detail.Viewport.DisplayMode.LocalName))
                    .ToArray();
                var titleBlock = record?.TitleBlock;
                var titleBlockName = titleBlock is null
                    ? null
                    : titleBlockInstances.GetValueOrDefault(titleBlock.InstanceObjectId)?.InstanceDefinitionName ??
                      document.InstanceDefinitions.Find(titleBlock.InstanceDefinitionId, true)?.Name ??
                      "Missing title block";

                return new SheetSnapshot(
                    pageId,
                    folderId,
                    record?.Order ?? index,
                    page.PageName,
                    detailIds,
                    record?.Metadata ?? new Dictionary<string, string>(StringComparer.Ordinal),
                    page.PageWidth,
                    page.PageHeight,
                    document.PageUnitSystem.ToString(),
                    detailSettings,
                    titleBlock?.InstanceObjectId,
                    titleBlockName,
                    record?.IncludeInPrintAll ?? true,
                    record?.TitleBlockData,
                    titleBlock?.BuiltInKind);
            })
            .ToDictionary(sheet => sheet.PageViewId);

        var objectIds = document.Objects
            .Select(item => item.Id)
            .ToHashSet();
        var displayModes = DisplayModeDescription.GetDisplayModes();
        var displayModeNames = displayModes.ToDictionary(mode => mode.Id, mode => mode.LocalName);
        var displayModeIds = displayModeNames.Keys.ToHashSet();
        foreach (var displayMode in displayModes)
        {
            displayMode.Dispose();
        }
        var layerNames = document.Layers
            .Where(layer => !layer.IsDeleted && !layer.IsReference)
            .OrderBy(layer => layer.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(layer => layer.Id, layer => layer.FullPath);
        var pagesById = pageViews.ToDictionary(page => page.MainViewport.Id);
        var templates = state.Templates
            .Select(template => RefreshDocumentBackedTemplate(
                document,
                template,
                pagesById,
                state.Sheets))
            .ToArray();

        return new DocumentSnapshot(
            document.RuntimeSerialNumber,
            _revisionTracker.Current(document),
            state.RootFolderId,
            folders,
            sheets,
            objectIds,
            displayModeIds,
            templates,
            state.Metadata,
            document.NamedViews.Select(view => view.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
            document.InstanceDefinitions.Select(definition => definition.Id).ToHashSet(),
            displayModeNames,
            titleBlockInstances,
            state.Canvas,
            state.ProjectInfo,
            layerNames);
    }

    private static SheetTemplateRecipe RefreshDocumentBackedTemplate(
        RhinoDoc document,
        SheetTemplateRecipe template,
        IReadOnlyDictionary<Guid, RhinoPageView> pagesById,
        IReadOnlyDictionary<Guid, SheetRecord> sheetRecords)
    {
        if (template.SourcePageViewId is not { } sourcePageViewId ||
            !pagesById.TryGetValue(sourcePageViewId, out var page))
            return template;

        var existingSlots = template.DetailSlots;
        var details = page.GetDetailViews()
            .Select((detail, index) => CaptureDetail(
                detail,
                index < existingSlots.Count ? existingSlots[index] : null))
            .ToArray();
        var titleBlock = CaptureTitleBlock(
            document,
            sheetRecords.GetValueOrDefault(sourcePageViewId)?.TitleBlock,
            template.TitleBlock);
        return template with
        {
            Paper = new PaperRecipe(page.PageWidth, page.PageHeight, document.PageUnitSystem.ToString()),
            DetailSlots = details,
            TitleBlock = titleBlock,
        };
    }

    private static DetailSlotRecipe CaptureDetail(
        DetailViewObject detail,
        DetailSlotRecipe? existing)
    {
        var bounds = detail.DetailGeometry.GetBoundingBox(true);
        var viewport = detail.Viewport;
        return new DetailSlotRecipe(
            existing?.Id ?? Guid.NewGuid(),
            string.IsNullOrWhiteSpace(detail.Attributes.Name) ? viewport.Name : detail.Attributes.Name,
            bounds.Min.X,
            bounds.Min.Y,
            bounds.Max.X,
            bounds.Max.Y,
            viewport.IsPerspectiveProjection ? "Perspective" : "Top",
            detail.DetailGeometry.IsParallelProjection ? detail.DetailGeometry.PageToModelRatio : null,
            detail.DetailGeometry.IsProjectionLocked,
            viewport.DisplayMode.Id,
            existing?.DefaultNamedView,
            [viewport.CameraLocation.X, viewport.CameraLocation.Y, viewport.CameraLocation.Z],
            [viewport.CameraTarget.X, viewport.CameraTarget.Y, viewport.CameraTarget.Z],
            [viewport.CameraUp.X, viewport.CameraUp.Y, viewport.CameraUp.Z]);
    }

    private static TitleBlockTemplateRecipe? CaptureTitleBlock(
        RhinoDoc document,
        TitleBlockRole? role,
        TitleBlockTemplateRecipe? existing)
    {
        if (role is null) return existing;
        if (document.Objects.FindId(role.InstanceObjectId) is not InstanceObject instance) return null;
        return new TitleBlockTemplateRecipe(
            instance.InstanceDefinition.Id,
            instance.InstanceDefinition.Name,
            TransformValues(instance.InstanceXform),
            role.AnchorName,
            existing?.FieldMappings ?? new Dictionary<string, string>(StringComparer.Ordinal),
            role.BuiltInKind);
    }

    private static IReadOnlyList<double> TransformValues(global::Rhino.Geometry.Transform transform) =>
    [
        transform.M00, transform.M01, transform.M02, transform.M03,
        transform.M10, transform.M11, transform.M12, transform.M13,
        transform.M20, transform.M21, transform.M22, transform.M23,
        transform.M30, transform.M31, transform.M32, transform.M33,
    ];
}

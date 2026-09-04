using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
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
                        detail.Viewport.DisplayMode.LocalName,
                        document.Layers[detail.Attributes.LayerIndex]?.Id,
                        PageBounds(detail)))
                    .ToArray();
                var titleBlock = record?.TitleBlock;
                var titleBlockName = titleBlock is null
                    ? null
                    : document.InstanceDefinitions.Find(titleBlock.InstanceDefinitionId, true)?.Name ??
                      "Missing title block";

                return new SheetSnapshot(
                    PageViewId: pageId,
                    FolderId: folderId,
                    Order: record?.Order ?? index,
                    Name: page.PageName,
                    DetailIds: detailIds,
                    Metadata: record?.Metadata ?? new Dictionary<string, string>(StringComparer.Ordinal),
                    PageWidth: page.PageWidth,
                    PageHeight: page.PageHeight,
                    PageUnitSystem: document.PageUnitSystem.ToString(),
                    DetailSettings: detailSettings,
                    TitleBlockInstanceObjectId: titleBlock?.InstanceObjectId,
                    TitleBlockDefinitionName: titleBlockName,
                    IncludeInPrintAll: record?.IncludeInPrintAll ?? true,
                    TitleBlockData: record?.TitleBlockData,
                    TitleBlockBuiltInKind: titleBlock?.BuiltInKind,
                    NamingBinding: record?.NamingBinding,
                    Notes: record?.Notes ?? string.Empty)
                {
                    DetailNamedViews = record?.DetailNamedViews ?? new Dictionary<Guid, string>()
                };
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
        var layerSettings = document.Layers
            .Where(layer => !layer.IsDeleted && !layer.IsReference)
            .ToDictionary(
                layer => layer.Id,
                layer => new LayerSnapshot(
                    layer.Id,
                    layer.ParentLayerId == Guid.Empty ? null : layer.ParentLayerId,
                    layer.FullPath,
                    layer.IsVisible));
        var modelObjects = document.Objects
            .Where(item => item is not DetailViewObject &&
                           item.Attributes.Space == ActiveSpace.ModelSpace)
            .Select(item =>
            {
                var layer = document.Layers[item.Attributes.LayerIndex];
                return new ModelObjectSnapshot(
                    item.Id,
                    string.IsNullOrWhiteSpace(item.Attributes.Name)
                        ? item.ObjectType.ToString()
                        : item.Attributes.Name,
                    layer.Id,
                    layer.FullPath,
                    item is InstanceObject);
            })
            .ToDictionary(item => item.Id);
        var modelBounds = CaptureModelBounds(document);
        var namedViewSnapshots = document.NamedViews
            .Select(view => new NamedViewSnapshot(
                view.Name,
                Coordinates(view.Viewport.CameraLocation),
                Coordinates(view.Viewport.TargetPoint),
                Coordinates(view.Viewport.CameraUp),
                view.Viewport.IsPerspectiveProjection
                    ? FoundryViewProjection.Perspective
                    : FoundryViewProjection.Parallel))
            .ToArray();
        var clippingPlanes = document.Objects
            .OfType<ClippingPlaneObject>()
            .Select(item =>
            {
                var geometry = item.ClippingPlaneGeometry;
                var plane = geometry.Plane;
                return new ClippingPlaneSnapshot(
                    item.Id,
                    string.IsNullOrWhiteSpace(item.Attributes.Name)
                        ? "Clipping plane"
                        : item.Attributes.Name,
                    Coordinates(plane.Origin),
                    Coordinates(plane.Normal),
                    Math.Abs(geometry.Domain(0).Length),
                    Math.Abs(geometry.Domain(1).Length),
                    geometry.ViewportIds(),
                    item.Attributes.GetUserString("RhinoLayoutFoundry.Automation.SessionId") ?? string.Empty);
            })
            .ToArray();
        var standardViewportIds = document.Views.GetStandardRhinoViews()
            .Select(view => view.ActiveViewport.Id)
            .ToArray();
        var detailLayerVisibilities = new List<DetailLayerVisibilitySnapshot>();
        var objectOverrides = new List<DetailObjectDisplayOverrideSnapshot>();
        foreach (var detail in pageViews.SelectMany(page => page.GetDetailViews()))
        {
            var detailId = detail.Viewport.Id;
            foreach (var layer in document.Layers.Where(layer =>
                         !layer.IsDeleted && !layer.IsReference && layer.HasPerViewportSettings(detailId)))
            {
                detailLayerVisibilities.Add(new DetailLayerVisibilitySnapshot(
                    detailId,
                    layer.Id,
                    layer.PerViewportIsVisible(detailId),
                    HasExplicitOverride: true));
            }
            foreach (var item in document.Objects.Where(item =>
                         item is not DetailViewObject &&
                         item.Attributes.Space == ActiveSpace.ModelSpace &&
                         item.Attributes.HasDisplayModeOverride(detailId)))
            {
                var modeId = item.Attributes.GetDisplayModeOverride(detailId);
                objectOverrides.Add(new DetailObjectDisplayOverrideSnapshot(
                    detailId,
                    item.Id,
                    modeId,
                    displayModeNames.GetValueOrDefault(modeId) ?? "Missing display mode"));
            }
        }
        var pagesById = pageViews.ToDictionary(page => page.MainViewport.Id);
        var templates = new List<SheetTemplateRecipe>();
        foreach (var registration in state.TemplateRegistrations)
        {
            RhinoPageView? sourcePage = null;
            DetailViewObject[] sourceDetails = [];
            if (registration.Source.Kind == HierarchyScopeKind.Sheet &&
                pagesById.TryGetValue(registration.Source.Id, out sourcePage))
                sourceDetails = sourcePage.GetDetailViews();
            else if (registration.Source.Kind == HierarchyScopeKind.Detail)
            {
                sourcePage = pageViews.FirstOrDefault(page => page.GetDetailViews()
                    .Any(detail => detail.Viewport.Id == registration.Source.Id));
                sourceDetails = sourcePage?.GetDetailViews()
                    .Where(detail => detail.Viewport.Id == registration.Source.Id).ToArray() ?? [];
            }
            if (sourcePage is null)
                continue;
            var sourceRecord = state.Sheets.GetValueOrDefault(sourcePage.MainViewport.Id);
            templates.Add(new SheetTemplateRecipe(
                Id: registration.Id,
                Name: registration.Source.Kind == HierarchyScopeKind.Detail
                    ? $"{sourceDetails[0].DescriptiveTitle} — Detail template"
                    : $"{sourcePage.PageName} — Layout template",
                Paper: new PaperRecipe(sourcePage.PageWidth, sourcePage.PageHeight,
                    document.PageUnitSystem.ToString()),
                DetailSlots: sourceDetails.Select(CaptureTemplateDetail).ToArray(),
                TitleBlock: null,
                DefaultMetadata: sourceRecord?.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value) ??
                new Dictionary<string, string>(StringComparer.Ordinal),
                DefaultNamingPattern: "{Template}-{n}")
            {
                SourcePageViewId = sourcePage.MainViewport.Id,
            });
        }

        return new DocumentSnapshot(
            DocumentRuntimeSerialNumber: document.RuntimeSerialNumber,
            Revision: _revisionTracker.Current(document),
            RootFolderId: state.RootFolderId,
            Folders: folders,
            Sheets: sheets,
            ExistingObjectIds: objectIds,
            DisplayModeIds: displayModeIds)
        {
            Templates = templates.ToArray(),
            Metadata = state.Metadata,
            NamedViews = document.NamedViews.Select(view => view.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
            DisplayModes = displayModeNames,
            Canvas = state.Canvas,
            ProjectInfo = state.ProjectInfo,
            Layers = layerNames,
            LayerSnapshots = layerSettings,
            ModelObjects = modelObjects,
            DetailLayers = detailLayerVisibilities,
            ObjectOverrides = objectOverrides,
            AppearanceRules = state.AppearanceRules,
            TemplateRegistrations = state.TemplateRegistrations,
            AppearanceStates = state.AppearanceStates,
            StateAssignments = state.StateAssignments,
            DedicatedDetailLayerId = state.DedicatedDetailLayerId,
            ModelBounds = modelBounds,
            NamedViewSnapshots = namedViewSnapshots,
            ClippingPlanes = clippingPlanes,
            StandardViewports = standardViewportIds,
            ActiveViewportDisplayModeId = document.Views.ActiveView?.ActiveViewport.DisplayMode.Id
        };
    }

    private static ModelBoundsSnapshot? CaptureModelBounds(RhinoDoc document)
    {
        var bounds = BoundingBox.Empty;
        foreach (var item in document.Objects.Where(item =>
                     item is not DetailViewObject && item.Attributes.Space == ActiveSpace.ModelSpace))
        {
            var objectBounds = item.Geometry?.GetBoundingBox(true) ?? BoundingBox.Empty;
            if (objectBounds.IsValid) bounds.Union(objectBounds);
        }

        return bounds.IsValid
            ? new ModelBoundsSnapshot(Coordinates(bounds.Min), Coordinates(bounds.Max))
            : null;
    }

    private static Point3Coordinates Coordinates(Point3d point) => new(point.X, point.Y, point.Z);

    private static Vector3Coordinates Coordinates(Vector3d vector) => new(vector.X, vector.Y, vector.Z);

    private static DetailPageBounds? PageBounds(DetailViewObject detail)
    {
        var bounds = detail.DetailGeometry.GetBoundingBox(true);
        return bounds.IsValid
            ? new DetailPageBounds(bounds.Min.X, bounds.Min.Y, bounds.Max.X, bounds.Max.Y)
            : null;
    }

    private static DetailSlotRecipe CaptureTemplateDetail(
        DetailViewObject detail)
    {
        var bounds = detail.DetailGeometry.GetBoundingBox(true);
        var viewport = detail.Viewport;
        return new DetailSlotRecipe(
viewport.Id,
            string.IsNullOrWhiteSpace(detail.Attributes.Name) ? viewport.Name : detail.Attributes.Name,
            bounds.Min.X,
            bounds.Min.Y,
            bounds.Max.X,
            bounds.Max.Y,
            viewport.IsPerspectiveProjection ? "Perspective" : "Top",
            detail.DetailGeometry.IsParallelProjection ? detail.DetailGeometry.PageToModelRatio : null,
            detail.DetailGeometry.IsProjectionLocked,
            viewport.DisplayMode.Id,
null,
            [viewport.CameraLocation.X, viewport.CameraLocation.Y, viewport.CameraLocation.Z],
            [viewport.CameraTarget.X, viewport.CameraTarget.Y, viewport.CameraTarget.Z],
            [viewport.CameraUp.X, viewport.CameraUp.Y, viewport.CameraUp.Z]);
    }
}

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
                        detail.Viewport.DisplayMode.LocalName,
                        document.Layers[detail.Attributes.LayerIndex]?.Id,
                        PageBounds(detail)))
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
                    titleBlock?.BuiltInKind,
                    record?.Tags,
                    record?.NamingBinding,
                    record?.Notes ?? string.Empty,
                    record?.DetailNamedViewAssignments);
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
        var layoutTemplateSources = state.TemplateRegistrations
            .Where(item => item.Capabilities.HasFlag(TemplateCapability.Layout))
            .Select(item => item.Source)
            .ToHashSet();
        var templates = state.Templates
            .Where(template => template.SourcePageViewId is not { } sourceId ||
                               layoutTemplateSources.Contains(new HierarchyScope(
                                   HierarchyScopeKind.Sheet, sourceId)))
            .Select(template =>
            {
                var refreshed = RefreshDocumentBackedTemplate(
                    document,
                    template,
                    pagesById,
                    state.Sheets);
                if (template.SourcePageViewId is not { } sourceId) return refreshed;
                var capabilities = state.TemplateRegistrations.LastOrDefault(item =>
                        item.Source == new HierarchyScope(HierarchyScopeKind.Sheet, sourceId))
                    ?.Capabilities ?? TemplateCapability.None;
                return refreshed with
                {
                    TitleBlock = capabilities.HasFlag(TemplateCapability.TitleBlock)
                        ? refreshed.TitleBlock
                        : null,
                    DetailSlots = refreshed.DetailSlots.Select(slot => slot with
                    {
                        LayerRules = [],
                        ObjectDisplayRules = [],
                    }).ToArray(),
                };
            })
            .ToList();
        foreach (var registration in state.TemplateRegistrations.Where(item =>
                     item.Capabilities.HasFlag(TemplateCapability.Layout)))
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
            if (sourcePage is null || templates.Any(template =>
                    template.SourcePageViewId == sourcePage.MainViewport.Id &&
                    registration.Source.Kind == HierarchyScopeKind.Sheet))
                continue;
            var sourceRecord = state.Sheets.GetValueOrDefault(sourcePage.MainViewport.Id);
            templates.Add(new SheetTemplateRecipe(
                registration.Id,
                SheetTemplateRecipe.CurrentRecipeVersion,
                registration.Source.Kind == HierarchyScopeKind.Detail
                    ? $"{sourceDetails[0].DescriptiveTitle} — Detail template"
                    : $"{sourcePage.PageName} — Layout template",
                new PaperRecipe(sourcePage.PageWidth, sourcePage.PageHeight,
                    document.PageUnitSystem.ToString()),
                sourceDetails.Select(detail =>
                {
                    var slot = CaptureDetail(document, detail, null);
                    return slot with
                    {
                        LayerRules = [],
                        ObjectDisplayRules = [],
                    };
                }).ToArray(),
                registration.Source.Kind == HierarchyScopeKind.Sheet &&
                registration.Capabilities.HasFlag(TemplateCapability.TitleBlock)
                    ? CaptureTitleBlock(document, sourceRecord?.TitleBlock, null)
                    : null,
                sourceRecord?.Tags.ToArray() ?? [],
                sourceRecord?.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value) ??
                new Dictionary<string, string>(StringComparer.Ordinal),
                "{Template}-{n}")
            {
                SourcePageViewId = sourcePage.MainViewport.Id,
            });
        }

        return new DocumentSnapshot(
            document.RuntimeSerialNumber,
            _revisionTracker.Current(document),
            state.RootFolderId,
            folders,
            sheets,
            objectIds,
            displayModeIds,
            templates.ToArray(),
            state.Metadata,
            document.NamedViews.Select(view => view.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
            document.InstanceDefinitions.Select(definition => definition.Id).ToHashSet(),
            displayModeNames,
            titleBlockInstances,
            state.Canvas,
            state.ProjectInfo,
            layerNames,
            layerSettings,
            modelObjects,
            detailLayerVisibilities,
            objectOverrides,
            state.AppearanceRules,
            state.TemplateRegistrations,
            state.TemplateLinks,
            state.AppearanceStates,
            state.StateAssignments,
            state.DedicatedDetailLayerId,
            modelBounds,
            namedViewSnapshots,
            clippingPlanes,
            standardViewportIds,
            document.Views.ActiveView?.ActiveViewport.DisplayMode.Id);
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
                document,
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
        RhinoDoc document,
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
            [viewport.CameraUp.X, viewport.CameraUp.Y, viewport.CameraUp.Z],
            document.Layers
                .Where(layer => !layer.IsDeleted && !layer.IsReference &&
                                layer.HasPerViewportSettings(viewport.Id))
                .Select(layer => new LayerVisibilityRule(
                    new LayerReference(layer.Id, layer.FullPath),
                    layer.PerViewportIsVisible(viewport.Id)
                        ? LayerVisibilityOverride.Visible
                        : LayerVisibilityOverride.Hidden))
                .ToArray(),
            document.Objects
                .Where(item => item is not DetailViewObject &&
                               item.Attributes.Space == ActiveSpace.ModelSpace &&
                               item.Attributes.HasDisplayModeOverride(viewport.Id))
                .Select(item =>
                {
                    var modeId = item.Attributes.GetDisplayModeOverride(viewport.Id);
                    using var mode = DisplayModeDescription.GetDisplayMode(modeId);
                    return new ObjectDisplayRule(
                        new ObjectDisplaySelector(ObjectDisplaySelectorKind.ExactObject, ObjectId: item.Id),
                        modeId,
                        mode?.LocalName ?? "Missing display mode");
                })
                .ToArray());
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

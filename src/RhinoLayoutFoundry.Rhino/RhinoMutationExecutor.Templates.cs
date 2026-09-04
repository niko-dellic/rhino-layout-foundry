using Rhino;
using Rhino.Commands;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Observer;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Rhino;

internal sealed partial class RhinoMutationExecutor
{

    private OperationResult ApplyTemplateBatch(
        RhinoDoc document,
        OperationPlan plan,
        IReadOnlyList<CreateSheetFromTemplateChange> creates)
    {
        var beforeState = _stateStore.Get(document);
        var existingNames = document.Views.GetPageViews().Select(page => page.PageName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (creates.Any(item => !beforeState.Folders.Any(folder => folder.Id == item.DestinationFolderId)))
            return Failure("batch.destination_missing", "The destination folder no longer exists.");
        if (creates.Any(item => !existingNames.Add(item.Name)))
            return Failure("batch.duplicate_name", "One or more layout names now conflict with the document.");

        var createdPages = new List<RhinoPageView>();
        DetailLayerResolution? detailLayer = null;
        try
        {
            var selectedLayerIndices = creates
                .Where(create => !create.UseDedicatedDetailLayer && create.DetailLayerId is not null)
                .Select(create => create.DetailLayerId!.Value)
                .Distinct()
                .ToDictionary(
                    layerId => layerId,
                    layerId => document.Layers.FirstOrDefault(layer =>
                        layer.Id == layerId && !layer.IsDeleted && !layer.IsReference)?.Index
                        ?? throw new InvalidOperationException("The selected detail layer is no longer available."));
            if (creates.Any(create => create.UseDedicatedDetailLayer && create.Template.DetailSlots.Count > 0))
                detailLayer = ResolveDedicatedDetailLayer(document, beforeState.DedicatedDetailLayerId);

            var sheets = beforeState.Sheets.ToDictionary(pair => pair.Key, pair => pair.Value);
            var assignments = beforeState.StateAssignments.ToList();
            var projectInfo = creates[0].ProjectInfo ?? beforeState.ProjectInfo;
            foreach (var create in creates)
            {
                var recipeUnit = ParseUnitSystem(create.Template.Paper.UnitSystem);
                var pageScale = RhinoMath.UnitScale(recipeUnit, document.PageUnitSystem);
                var page = document.Views.AddPageView(
                    create.Name,
                    create.Template.Paper.Width * pageScale,
                    create.Template.Paper.Height * pageScale)
                    ?? throw new InvalidOperationException($"Rhino did not create '{create.Name}'.");
                createdPages.Add(page);

                var namingViews = new Dictionary<Guid, string>();
                var createdDetailIdsBySlot = new Dictionary<Guid, Guid>();
                foreach (var slot in create.Template.DetailSlots)
                {
                    var assignedView = create.NamedViewAssignments.GetValueOrDefault(slot.Id);
                    var detailId = CreateDetail(document, page, slot, recipeUnit, pageScale,
                        assignedView,
                        create.UseDedicatedDetailLayer
                            ? detailLayer?.LayerIndex
                            : create.DetailLayerId is { } detailLayerId
                                ? selectedLayerIndices[detailLayerId]
                                : null);
                    var effectiveView = assignedView ?? slot.DefaultNamedView;
                    if (!string.IsNullOrWhiteSpace(effectiveView)) namingViews[detailId] = effectiveView;
                    createdDetailIdsBySlot[slot.Id] = detailId;
                }

                var revisions = create.InitialRevisions?.ToArray() ?? [];
                var titleBlockData = new SheetTitleBlockData(create.SheetNumber, revisions);
                Guid? titleBlockId = null;
                if (create.Template.TitleBlock is { } titleBlock)
                    titleBlockId = CreateTitleBlock(
                        document,
                        page,
                        titleBlock,
                        create.Template.Paper,
                        projectInfo,
                        titleBlockData,
                        create.Template.DetailSlots);

                var placedDefinitionId = titleBlockId is { } placedId &&
                                         document.Objects.FindId(placedId) is InstanceObject placedInstance
                    ? placedInstance.InstanceDefinition.Id
                    : Guid.Empty;

                sheets[page.MainViewport.Id] = new SheetRecord(
                    PageViewId: page.MainViewport.Id,
                    FolderId: create.DestinationFolderId,
                    Order: create.Order,
                    Metadata: create.Template.DefaultMetadata.ToDictionary(pair => pair.Key, pair => pair.Value),
                    TitleBlock: titleBlockId is { } instanceId && create.Template.TitleBlock is { } block
                        ? new TitleBlockRole(
                            InstanceObjectId: instanceId,
                            InstanceDefinitionId: placedDefinitionId,
                            BuiltInKind: block.BuiltInKind)
                        : null,
                    TitleBlockData: titleBlockData,
                    NamingBinding: string.IsNullOrWhiteSpace(create.NamingPattern)
                        ? null
                        : new SheetNamingBinding(
                            Pattern: create.NamingPattern,
                            Index: create.NamingIndex,
                            LastGeneratedName: create.Name)
                        { NamedViewAssignments = namingViews })
                {
                    DetailNamedViews = namingViews
                };
                var sheetScope = new HierarchyScope(HierarchyScopeKind.Sheet, page.MainViewport.Id);
                if (create.AppearanceStateId is { } appearanceStateId)
                {
                    if (!beforeState.AppearanceStates.Any(state => state.Id == appearanceStateId))
                        throw new InvalidOperationException("The selected appearance state is no longer available.");
                    assignments.Add(new AppearanceStateAssignment(
                        Guid.NewGuid(), sheetScope, appearanceStateId));
                }
                foreach (var pair in create.DetailAppearanceStateAssignments ??
                             new Dictionary<Guid, Guid>())
                {
                    if (!createdDetailIdsBySlot.TryGetValue(pair.Key, out var detailId))
                        throw new InvalidOperationException(
                            "A detail appearance-state assignment no longer matches the layout.");
                    if (!beforeState.AppearanceStates.Any(state => state.Id == pair.Value))
                        throw new InvalidOperationException(
                            "A detail appearance state is no longer available.");
                    assignments.Add(new AppearanceStateAssignment(
                        Guid.NewGuid(),
                        new HierarchyScope(HierarchyScopeKind.Detail, detailId),
                        pair.Value));
                }
            }

            var afterState = beforeState with
            {
                Sheets = sheets,
                DedicatedDetailLayerId = detailLayer?.LayerId ?? beforeState.DedicatedDetailLayerId,
                ProjectInfo = projectInfo,
                StateAssignments = assignments,
            };
            var rootScope = new HierarchyScope(HierarchyScopeKind.Folder, beforeState.RootFolderId);
            var rootRules = beforeState.AppearanceRules.LastOrDefault(item => item.Scope == rootScope);
            var appearanceResult = ApplyViewportAppearanceRules(
                document,
                plan,
                new SetHierarchyViewportRulesChange(rootScope, rootRules, rootRules),
                afterState.TemplateRegistrations,
                afterState.AppearanceStates,
                afterState.StateAssignments,
                afterState);
            if (!appearanceResult.Succeeded)
                throw new InvalidOperationException(
                    appearanceResult.Diagnostics.FirstOrDefault()?.Message ??
                    "The appearance-state assignments could not be applied.");
            DeleteUnusedGeneratedTitleBlockDefinitions(document);
            return appearanceResult;
        }
        catch (Exception exception)
        {
            _stateStore.Set(document, beforeState);
            foreach (var page in createdPages.AsEnumerable().Reverse())
                page.Close();
            if (detailLayer is { Created: true })
                document.Layers.Delete(detailLayer.LayerId, quiet: true);
            return Failure("batch.apply_failed",
                $"Batch creation failed; every layout created by this batch was removed: {exception.Message}");
        }
    }

    private static DetailLayerResolution ResolveDedicatedDetailLayer(
        RhinoDoc document,
        Guid? trackedLayerId)
    {
        if (trackedLayerId is { } layerId)
        {
            var tracked = document.Layers.FirstOrDefault(layer =>
                layer.Id == layerId && !layer.IsDeleted && !layer.IsReference);
            if (tracked is not null)
                return new DetailLayerResolution(tracked.Index, tracked.Id, false);
        }

        var existing = document.Layers.FirstOrDefault(layer =>
            !layer.IsDeleted &&
            !layer.IsReference &&
            layer.ParentLayerId == Guid.Empty &&
            string.Equals(layer.Name, DedicatedDetailLayerName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return new DetailLayerResolution(existing.Index, existing.Id, false);

        var definition = new Layer { Name = DedicatedDetailLayerName };
        var index = document.Layers.Add(definition);
        if (index < 0)
            throw new InvalidOperationException($"Rhino did not create the '{DedicatedDetailLayerName}' layer.");
        var created = document.Layers[index];
        return new DetailLayerResolution(created.Index, created.Id, true);
    }

    private static DetailSlotRecipe CaptureDetail(RhinoDoc document, DetailViewObject detail)
    {
        var bounds = detail.DetailGeometry.GetBoundingBox(true);
        var viewport = detail.Viewport;
        return new DetailSlotRecipe(
            Id: Guid.NewGuid(),
            Name: string.IsNullOrWhiteSpace(detail.Attributes.Name) ? viewport.Name : detail.Attributes.Name,
            Left: bounds.Min.X,
            Bottom: bounds.Min.Y,
            Right: bounds.Max.X,
            Top: bounds.Max.Y,
            Projection: viewport.IsPerspectiveProjection ? "Perspective" : "Top",
            PageToModelRatio: detail.DetailGeometry.IsParallelProjection ? detail.DetailGeometry.PageToModelRatio : null,
            ProjectionLocked: detail.DetailGeometry.IsProjectionLocked,
            DisplayModeId: viewport.DisplayMode.Id,
            DefaultNamedView: null,
            CameraLocation: [viewport.CameraLocation.X, viewport.CameraLocation.Y, viewport.CameraLocation.Z],
            CameraTarget: [viewport.CameraTarget.X, viewport.CameraTarget.Y, viewport.CameraTarget.Z],
            CameraUp: [viewport.CameraUp.X, viewport.CameraUp.Y, viewport.CameraUp.Z])
        {
            LayerRules = document.Layers
                .Where(layer => !layer.IsDeleted && !layer.IsReference &&
                                layer.HasPerViewportSettings(viewport.Id))
                .Select(layer => new LayerVisibilityRule(
                    new LayerReference(layer.Id, layer.FullPath),
                    layer.PerViewportIsVisible(viewport.Id)
                        ? LayerVisibilityOverride.Visible
                        : LayerVisibilityOverride.Hidden))
                .ToArray(),
            ObjectDisplayRules = document.Objects
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
                .ToArray()
        };
    }

    internal static Guid CreateDetail(
        RhinoDoc document,
        RhinoPageView page,
        DetailSlotRecipe slot,
        UnitSystem recipePageUnit,
        double pageScale,
        string? assignedNamedView,
        int? detailLayerIndex)
    {
        var projection = Enum.TryParse<DefinedViewportProjection>(slot.Projection, true, out var parsed)
            ? parsed
            : DefinedViewportProjection.Top;
        var detail = page.AddDetailView(
            slot.Name,
            new Point2d(slot.Left * pageScale, slot.Bottom * pageScale),
            new Point2d(slot.Right * pageScale, slot.Top * pageScale),
            projection) ?? throw new InvalidOperationException($"Rhino did not create detail '{slot.Name}'.");

        var objectChanged = false;
        if (slot.PageToModelRatio is { } ratio && ratio > 0 && detail.DetailGeometry.IsParallelProjection)
        {
            if (!detail.DetailGeometry.SetScale(ratio, recipePageUnit, 1, document.ModelUnitSystem))
                throw new InvalidOperationException($"Rhino did not set the scale for detail '{slot.Name}'.");
            objectChanged = true;
        }
        if (detail.DetailGeometry.IsProjectionLocked != slot.ProjectionLocked)
        {
            detail.DetailGeometry.IsProjectionLocked = slot.ProjectionLocked;
            objectChanged = true;
        }
        if (detailLayerIndex is { } layerIndex && detail.Attributes.LayerIndex != layerIndex)
        {
            detail.Attributes.LayerIndex = layerIndex;
            objectChanged = true;
        }
        if (objectChanged)
        {
            if (!detail.CommitChanges())
                throw new InvalidOperationException($"Rhino did not commit detail properties for '{slot.Name}'.");
            detail = document.Objects.FindId(detail.Id) as DetailViewObject
                ?? throw new InvalidOperationException($"Rhino could not find detail '{slot.Name}' after committing it.");
        }

        var viewportChanged = false;
        var namedView = assignedNamedView ?? slot.DefaultNamedView;
        if (!string.IsNullOrWhiteSpace(namedView))
        {
            var index = document.NamedViews.FindByName(namedView);
            if (index >= 0)
            {
                if (!document.NamedViews.RestoreWithAspectRatio(index, detail.Viewport))
                    throw new InvalidOperationException(
                        $"Rhino did not apply named view '{namedView}' to detail '{slot.Name}'.");
                viewportChanged = true;
            }
        }
        else if (slot.CameraLocation is [var lx, var ly, var lz] &&
                 slot.CameraTarget is [var tx, var ty, var tz])
        {
            detail.Viewport.SetCameraLocations(new Point3d(tx, ty, tz), new Point3d(lx, ly, lz));
            viewportChanged = true;
        }
        if (slot.DisplayModeId is { } modeId)
        {
            using var mode = DisplayModeDescription.GetDisplayMode(modeId);
            if (mode is not null)
            {
                detail.Viewport.DisplayMode = mode;
                viewportChanged = true;
            }
        }
        if (viewportChanged && !detail.CommitViewportChanges())
            throw new InvalidOperationException($"Rhino did not commit viewport settings for detail '{slot.Name}'.");
        ApplyDetailAppearanceRecipe(document, detail.Viewport.Id, slot);
        return detail.Viewport.Id;
    }

    internal static bool RestoreNamedViewForDetail(
        RhinoDoc document,
        int namedViewIndex,
        DetailViewObject detail)
    {
        if (namedViewIndex < 0 || namedViewIndex >= document.NamedViews.Count)
            return false;

        using var namedView = document.NamedViews[namedViewIndex];
        using var projection = new ViewportInfo(namedView.Viewport);
        var bounds = detail.DetailGeometry.GetBoundingBox(true);
        var width = bounds.Max.X - bounds.Min.X;
        var height = bounds.Max.Y - bounds.Min.Y;
        if (width > RhinoMath.ZeroTolerance && height > RhinoMath.ZeroTolerance)
            projection.FrustumAspect = width / height;

        return detail.Viewport.SetViewProjection(projection, updateTargetLocation: true);
    }

    private static void ApplyDetailAppearanceRecipe(
        RhinoDoc document,
        Guid detailViewportId,
        DetailSlotRecipe slot)
    {
        foreach (var sourceRule in slot.LayerRules)
        {
            var layer = document.Layers.FindId(sourceRule.Layer.LayerId);
            if (layer is null && !string.IsNullOrWhiteSpace(sourceRule.Layer.FullPath))
            {
                var indexByPath = document.Layers.FindByFullPath(sourceRule.Layer.FullPath, -1);
                if (indexByPath >= 0) layer = document.Layers[indexByPath];
            }
            if (layer is null) continue;
            var copy = CopyLayer(layer);
            copy.SetPerViewportVisible(
                detailViewportId,
                sourceRule.Visibility == LayerVisibilityOverride.Visible);
            if (!document.Layers.Modify(copy, layer.Index, quiet: true))
                throw new InvalidOperationException(
                    $"Rhino did not apply layer visibility for '{layer.FullPath}'.");
        }

        var layers = document.Layers.Where(layer => !layer.IsDeleted && !layer.IsReference)
            .Select(layer => new LayerSnapshot(
                layer.Id,
                layer.ParentLayerId == Guid.Empty ? null : layer.ParentLayerId,
                layer.FullPath,
                layer.IsVisible))
            .ToDictionary(layer => layer.Id);
        var objects = document.Objects.Where(item => item is not DetailViewObject &&
                                                      item.Attributes.Space == ActiveSpace.ModelSpace)
            .Select(item =>
            {
                var layer = document.Layers[item.Attributes.LayerIndex];
                return new ModelObjectSnapshot(
                    item.Id,
                    item.Attributes.Name,
                    layer.Id,
                    layer.FullPath,
                    item is InstanceObject);
            })
            .ToDictionary(item => item.Id);
        var normalizedRules = slot.ObjectDisplayRules.Select(rule =>
        {
            if (rule.Selector.Kind != ObjectDisplaySelectorKind.Layer ||
                string.IsNullOrWhiteSpace(rule.Selector.LayerFullPath)) return rule;
            var index = document.Layers.FindByFullPath(rule.Selector.LayerFullPath, -1);
            return index < 0
                ? rule
                : rule with { Selector = rule.Selector with { LayerId = document.Layers[index].Id } };
        }).ToArray();
        var scope = new HierarchyScope(HierarchyScopeKind.Detail, detailViewportId);
        var resolved = ViewportAppearanceResolver.Resolve(
            [scope],
            new Dictionary<HierarchyScope, HierarchyViewportRuleSet>
            {
                [scope] = new HierarchyViewportRuleSet(scope, [], normalizedRules),
            },
            layers,
            objects);
        foreach (var pair in resolved.Objects)
        {
            var item = document.Objects.FindId(pair.Key);
            if (item is null) continue;
            using var mode = DisplayModeDescription.GetDisplayMode(pair.Value.DisplayModeId);
            if (mode is null) continue;
            var attributes = item.Attributes.Duplicate();
            if (!RhinoObjectDisplayModeOverride.TrySet(attributes, mode, detailViewportId) ||
                !document.Objects.ModifyAttributes(item, attributes, quiet: true))
                throw new InvalidOperationException(
                    $"Rhino did not apply an object display override to '{item.Id}'.");
        }
    }

}

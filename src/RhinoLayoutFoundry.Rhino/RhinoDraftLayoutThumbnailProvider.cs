using System.Drawing.Imaging;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Rhino;

internal sealed class RhinoDraftLayoutThumbnailProvider : IDraftLayoutThumbnailProvider
{
    private readonly DocumentStateStore _stateStore;
    private readonly Action<string>? _checkpoint;

    internal RhinoDraftLayoutThumbnailProvider(DocumentStateStore stateStore, Action<string>? checkpoint = null)
    {
        _checkpoint = checkpoint;
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    }

    public async Task<DraftLayoutThumbnailResult> CaptureAsync(
        DraftLayoutThumbnailRequest request,
        CancellationToken cancellationToken)
    {
        var enteredGate = false;
        try
        {
            await RhinoThumbnailCaptureGate.Gate.WaitAsync(cancellationToken);
            enteredGate = true;
            if (!RhinoApp.InvokeRequired)
                return CaptureOnUiThread(request, cancellationToken);

            var completion = new TaskCompletionSource<DraftLayoutThumbnailResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                try
                {
                    completion.SetResult(CaptureOnUiThread(request, cancellationToken));
                }
                catch (Exception exception)
                {
                    completion.SetResult(Failure(request, exception.Message));
                }
            }));
            return await completion.Task;
        }
        catch (OperationCanceledException)
        {
            return Failure(request, "Draft-layout preview capture was cancelled.");
        }
        catch (Exception exception)
        {
            return Failure(request, exception.Message);
        }
        finally
        {
            if (enteredGate) RhinoThumbnailCaptureGate.Gate.Release();
        }
    }

    public async Task<EditSheetThumbnailResult> CaptureEditAsync(
        EditSheetThumbnailRequest request,
        CancellationToken cancellationToken)
    {
        var enteredGate = false;
        try
        {
            await RhinoThumbnailCaptureGate.Gate.WaitAsync(cancellationToken);
            enteredGate = true;
            if (!RhinoApp.InvokeRequired)
                return CaptureEditOnUiThread(request, cancellationToken);

            var completion = new TaskCompletionSource<EditSheetThumbnailResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                try
                {
                    completion.SetResult(CaptureEditOnUiThread(request, cancellationToken));
                }
                catch (Exception exception)
                {
                    completion.SetResult(Failure(request, exception.Message));
                }
            }));
            return await completion.Task;
        }
        catch (OperationCanceledException)
        {
            return Failure(request, "Edit preview capture was cancelled.");
        }
        catch (Exception exception)
        {
            return Failure(request, exception.Message);
        }
        finally
        {
            if (enteredGate) RhinoThumbnailCaptureGate.Gate.Release();
        }
    }

    public async Task WaitForPendingCapturesAsync(
        CancellationToken cancellationToken = default)
    {
        await RhinoThumbnailCaptureGate.Gate.WaitAsync(cancellationToken);
        RhinoThumbnailCaptureGate.Gate.Release();
    }

    private DraftLayoutThumbnailResult CaptureOnUiThread(
        DraftLayoutThumbnailRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = RhinoDoc.FromRuntimeSerialNumber(request.Key.DocumentRuntimeSerialNumber);
        if (source is null)
            return Failure(request, "The Rhino document is no longer available.");

        _stateStore.EnsureWritable(source);
        var layerBefore = new Dictionary<Guid, Layer>();
        var objectBefore = new Dictionary<Guid, ObjectAttributes>();
        using var session = new RhinoPreviewSession(source);
        session.Restore("Restore preview appearance", () => RhinoPreviewSession.RestoreAppearance(source, layerBefore, objectBefore));
        var page = CreateDraftPage(source, request.Change, _stateStore.Get(source), layerBefore, objectBefore, page =>
        {
            session.Own(page);
            _checkpoint?.Invoke("preview-page");
        });
        source.Views.ActiveView = page;
        source.Views.Redraw();
        page.Redraw();
        cancellationToken.ThrowIfCancellationRequested();
        return CapturePage(request, source, page);
    }

    private EditSheetThumbnailResult CaptureEditOnUiThread(
        EditSheetThumbnailRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var document = RhinoDoc.FromRuntimeSerialNumber(request.Key.DocumentRuntimeSerialNumber);
        var sourcePage = document?.Views.GetPageViews().FirstOrDefault(page =>
            page.MainViewport.Id == request.Key.SheetPageViewId);
        if (document is null || sourcePage is null)
            return Failure(request, "The selected layout sheet is no longer available.");
        _stateStore.EnsureWritable(document);
        var layerBefore = new Dictionary<Guid, Layer>();
        var objectBefore = new Dictionary<Guid, ObjectAttributes>();
        using var session = new RhinoPreviewSession(document);
        session.Restore("Restore preview appearance", () => RhinoPreviewSession.RestoreAppearance(document, layerBefore, objectBefore));
        var previewPage = sourcePage.Duplicate(duplicatePageGeometry: true)
            ?? throw new InvalidOperationException("Rhino could not duplicate the sheet for preview.");
        session.Own(previewPage);
        previewPage.PageName = $"__FoundryEditPreview_{Guid.NewGuid():N}";
        document.Views.ActiveView = previewPage;
        previewPage.SetPageAsActive();
        ApplyEditAssignments(document, sourcePage, previewPage, request, _stateStore.Get(document), layerBefore, objectBefore);
        document.Views.Redraw();
        previewPage.Redraw();
        cancellationToken.ThrowIfCancellationRequested();
        return CapturePage(request, document, previewPage);
    }

    private static void ApplyEditAssignments(
        RhinoDoc document,
        RhinoPageView sourcePage,
        RhinoPageView previewPage,
        EditSheetThumbnailRequest request,
        DocumentState state,
        IDictionary<Guid, Layer> layerBefore,
        IDictionary<Guid, ObjectAttributes> objectBefore)
    {
        var sourceDetails = sourcePage.GetDetailViews().ToArray();
        if (request.DetailAssignments.Count != sourceDetails.Length)
            throw new InvalidOperationException("The sheet details changed before the preview could be rendered.");

        var requestedById = request.DetailAssignments.ToDictionary(item => item.DetailViewportId);
        var previewDetails = previewPage.GetDetailViews().ToArray();
        if (previewDetails.Length != sourceDetails.Length)
            throw new InvalidOperationException("Rhino did not preserve the sheet details in the preview copy.");
        var previewBySourceId = MatchDuplicatedDetails(sourceDetails, previewDetails);

        for (var index = 0; index < sourceDetails.Length; index++)
        {
            var sourceDetail = sourceDetails[index];
            var previewDetail = previewBySourceId[sourceDetail.Viewport.Id];
            if (!requestedById.TryGetValue(sourceDetail.Viewport.Id, out var requested))
                throw new InvalidOperationException("A detail preview assignment is unavailable.");

            var cameraChanged = requested.ChangeNamedView &&
                                !string.IsNullOrWhiteSpace(requested.NamedViewName);
            var displayModeChanged = requested.DisplayModeId is { } displayModeId &&
                                     previewDetail.Viewport.DisplayMode.Id != displayModeId;
            if (!cameraChanged && !displayModeChanged) continue;

            // A duplicated detail can retain Rhino's cached display pipeline after
            // its camera changes. Rebuild only the changed detail so the capture is
            // fresh while every untouched sibling keeps its exact duplicated camera.
            var sourceViewport = sourceDetail.Viewport;
            var bounds = sourceDetail.DetailGeometry.GetBoundingBox(true);
            var slot = new DetailSlotRecipe(
                Id: Guid.NewGuid(),
                Name: string.IsNullOrWhiteSpace(sourceDetail.Attributes.Name)
                    ? sourceViewport.Name
                    : sourceDetail.Attributes.Name,
                Left: bounds.Min.X,
                Bottom: bounds.Min.Y,
                Right: bounds.Max.X,
                Top: bounds.Max.Y,
                Projection: sourceViewport.IsPerspectiveProjection ? "Perspective" : "Top",
                PageToModelRatio: !cameraChanged && sourceDetail.DetailGeometry.IsParallelProjection
                    ? sourceDetail.DetailGeometry.PageToModelRatio
                    : null,
                ProjectionLocked: !cameraChanged && sourceDetail.DetailGeometry.IsProjectionLocked,
                DisplayModeId: requested.DisplayModeId,
                DefaultNamedView: null,
                CameraLocation: [sourceViewport.CameraLocation.X, sourceViewport.CameraLocation.Y, sourceViewport.CameraLocation.Z],
                CameraTarget: [sourceViewport.CameraTarget.X, sourceViewport.CameraTarget.Y, sourceViewport.CameraTarget.Z],
                CameraUp: [sourceViewport.CameraUp.X, sourceViewport.CameraUp.Y, sourceViewport.CameraUp.Z]);
            if (!document.Objects.Delete(previewDetail.Id, quiet: true))
                throw new InvalidOperationException("Rhino could not remove the stale preview detail.");
            var rebuiltViewportId = RhinoMutationExecutor.CreateDetail(
                document,
                previewPage,
                slot,
                document.PageUnitSystem,
                1.0,
                assignedNamedView: null,
                detailLayerIndex: sourceDetail.Attributes.LayerIndex);
            var rebuilt = previewPage.GetDetailViews().FirstOrDefault(detail =>
                detail.Viewport.Id == rebuiltViewportId)
                ?? throw new InvalidOperationException("Rhino could not find a rebuilt preview detail.");
            if (cameraChanged)
            {
                var namedViewIndex = document.NamedViews.FindByName(requested.NamedViewName!);
                if (namedViewIndex < 0 ||
                    !RhinoMutationExecutor.RestoreNamedViewForDetail(
                        document,
                        namedViewIndex,
                        rebuilt))
                    throw new InvalidOperationException(
                        $"Rhino could not apply named view '{requested.NamedViewName}' to a preview detail.");
                if (!rebuilt.CommitViewportChanges())
                    throw new InvalidOperationException("Rhino could not commit a preview detail camera.");
            }
            else
            {
                rebuilt.Viewport.SetViewProjection(new ViewportInfo(sourceViewport), true);
                if (requested.DisplayModeId is { } inheritedDisplayModeId)
                {
                    using var inheritedDisplayMode = DisplayModeDescription.GetDisplayMode(inheritedDisplayModeId)
                        ?? throw new InvalidOperationException("A detail display mode is unavailable.");
                    rebuilt.Viewport.DisplayMode = inheritedDisplayMode;
                }
                if (!rebuilt.CommitViewportChanges())
                    throw new InvalidOperationException("Rhino could not restore a preview detail camera.");
            }
            previewBySourceId[sourceDetail.Viewport.Id] = rebuilt;
        }
        previewPage.SetPageAsActive();

        var assignments = state.StateAssignments.ToList();
        var sourceSheetScope = new HierarchyScope(
            HierarchyScopeKind.Sheet,
            request.Key.SheetPageViewId);
        ReplaceDirectAssignment(assignments, sourceSheetScope, request.SheetAppearanceStateId);

        var rules = state.AppearanceRules
            .GroupBy(item => item.Scope)
            .ToDictionary(group => group.Key, group => group.Last());
        var layers = document.Layers
            .Where(layer => !layer.IsDeleted && !layer.IsReference)
            .ToDictionary(
                layer => layer.Id,
                layer => new LayerSnapshot(
                    layer.Id,
                    layer.ParentLayerId == Guid.Empty ? null : layer.ParentLayerId,
                    layer.FullPath,
                    layer.IsVisible));
        var objects = document.Objects
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
        var states = state.AppearanceStates.ToDictionary(item => item.Id);
        var folderScopes = FolderScopes(request.FolderId, state).ToArray();

        for (var index = 0; index < sourceDetails.Length; index++)
        {
            var sourceDetail = sourceDetails[index];
            var previewDetail = previewBySourceId[sourceDetail.Viewport.Id];
            var requested = requestedById[sourceDetail.Viewport.Id];

            var sourceDetailScope = new HierarchyScope(
                HierarchyScopeKind.Detail,
                sourceDetail.Viewport.Id);
            ReplaceDirectAssignment(assignments, sourceDetailScope, requested.AppearanceStateId);
            var effective = ViewportAppearanceResolver.Resolve(
                folderScopes.Append(sourceSheetScope).Append(sourceDetailScope),
                rules,
                layers,
                objects,
                states,
                assignments);
            ApplyAppearance(
                document,
                previewDetail.Viewport.Id,
                effective,
                layerBefore,
                objectBefore);
        }
    }

    private static Dictionary<Guid, DetailViewObject> MatchDuplicatedDetails(
        IReadOnlyList<DetailViewObject> sourceDetails,
        IReadOnlyList<DetailViewObject> previewDetails)
    {
        var remaining = previewDetails.ToList();
        var result = new Dictionary<Guid, DetailViewObject>();
        foreach (var source in sourceDetails)
        {
            var sourceBounds = source.DetailGeometry.GetBoundingBox(true);
            var best = remaining
                .OrderBy(candidate => DetailBoundsDistance(
                    sourceBounds,
                    candidate.DetailGeometry.GetBoundingBox(true)))
                .First();
            result[source.Viewport.Id] = best;
            remaining.Remove(best);
        }
        return result;
    }

    private static double DetailBoundsDistance(BoundingBox left, BoundingBox right)
    {
        if (!left.IsValid || !right.IsValid) return double.MaxValue;
        var centerDistance = left.Center.DistanceToSquared(right.Center);
        var widthDistance = left.Diagonal.X - right.Diagonal.X;
        var heightDistance = left.Diagonal.Y - right.Diagonal.Y;
        return centerDistance + widthDistance * widthDistance + heightDistance * heightDistance;
    }

    private static void ReplaceDirectAssignment(
        List<AppearanceStateAssignment> assignments,
        HierarchyScope target,
        Guid? stateId)
    {
        assignments.RemoveAll(item => item.Target == target);
        if (stateId is { } id)
            assignments.Add(new AppearanceStateAssignment(Guid.NewGuid(), target, id));
    }

    private RhinoPageView CreateDraftPage(
        RhinoDoc document,
        CreateSheetFromTemplateChange create,
        DocumentState state,
        IDictionary<Guid, Layer> layerBefore,
        IDictionary<Guid, ObjectAttributes> objectBefore,
        Action<RhinoPageView> ownPage)
    {
        var recipeUnit = RhinoMutationExecutor.ParseUnitSystem(
            create.Template.Paper.UnitSystem);
        var pageScale = RhinoMath.UnitScale(recipeUnit, document.PageUnitSystem);
        var page = document.Views.AddPageView(
            $"__FoundryPreview_{Guid.NewGuid():N}",
            create.Template.Paper.Width * pageScale,
            create.Template.Paper.Height * pageScale)
            ?? throw new InvalidOperationException("Rhino could not create the isolated preview page.");
        ownPage(page);

        int? detailLayerIndex = null;
        if (!create.UseDedicatedDetailLayer && create.DetailLayerId is { } layerId)
        {
            detailLayerIndex = document.Layers.FirstOrDefault(layer =>
                layer.Id == layerId && !layer.IsDeleted && !layer.IsReference)?.Index;
        }

        foreach (var slot in create.Template.DetailSlots)
        {
            RhinoMutationExecutor.CreateDetail(
                document,
                page,
                slot,
                recipeUnit,
                pageScale,
                create.NamedViewAssignments.GetValueOrDefault(slot.Id),
                detailLayerIndex);
            _checkpoint?.Invoke("preview-detail");
        }

        var titleBlockData = new SheetTitleBlockData(
            create.SheetNumber,
            create.InitialRevisions?.ToArray() ?? []);
        if (create.Template.TitleBlock is { } titleBlock)
        {
            RhinoMutationExecutor.CreateTitleBlock(
                document,
                page,
                titleBlock,
                create.Template.Paper,
                create.ProjectInfo ?? state.ProjectInfo,
                titleBlockData,
                create.Template.DetailSlots);
        }

        _checkpoint?.Invoke("preview-title-block");
        ApplySheetAppearance(document, page, create, state, layerBefore, objectBefore);
        _checkpoint?.Invoke("preview-appearance");
        return page;
    }

    private static void ApplySheetAppearance(
        RhinoDoc document,
        RhinoPageView page,
        CreateSheetFromTemplateChange create,
        DocumentState state,
        IDictionary<Guid, Layer> layerBefore,
        IDictionary<Guid, ObjectAttributes> objectBefore)
    {
        var folderScopes = FolderScopes(create.DestinationFolderId, state).ToArray();
        var sheetScope = new HierarchyScope(HierarchyScopeKind.Sheet, page.MainViewport.Id);
        var rules = state.AppearanceRules
            .GroupBy(item => item.Scope)
            .ToDictionary(group => group.Key, group => group.Last());
        var assignments = state.StateAssignments.ToList();
        if (create.AppearanceStateId is { } appearanceStateId)
        {
            assignments.Add(new AppearanceStateAssignment(
                Guid.NewGuid(),
                sheetScope,
                appearanceStateId));
        }

        var layers = document.Layers
            .Where(layer => !layer.IsDeleted && !layer.IsReference)
            .ToDictionary(
                layer => layer.Id,
                layer => new LayerSnapshot(
                    layer.Id,
                    layer.ParentLayerId == Guid.Empty ? null : layer.ParentLayerId,
                    layer.FullPath,
                    layer.IsVisible));
        var objects = document.Objects
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
        var states = state.AppearanceStates.ToDictionary(item => item.Id);

        var details = page.GetDetailViews().ToArray();
        for (var index = 0; index < details.Length; index++)
        {
            var detail = details[index];
            var detailScope = new HierarchyScope(HierarchyScopeKind.Detail, detail.Viewport.Id);
            if (index < create.Template.DetailSlots.Count &&
                create.DetailAppearanceStateAssignments?.GetValueOrDefault(
                    create.Template.DetailSlots[index].Id) is { } detailAppearanceStateId)
            {
                assignments.Add(new AppearanceStateAssignment(
                    Guid.NewGuid(),
                    detailScope,
                    detailAppearanceStateId));
            }
            var effective = ViewportAppearanceResolver.Resolve(
                folderScopes.Append(sheetScope).Append(detailScope),
                rules,
                layers,
                objects,
                states,
                assignments);
            ApplyAppearance(
                document,
                detail.Viewport.Id,
                effective,
                layerBefore,
                objectBefore);
        }
    }

    private static IEnumerable<HierarchyScope> FolderScopes(Guid folderId, DocumentState state)
    {
        var folders = state.Folders.ToDictionary(item => item.Id);
        var chain = new List<HierarchyScope>();
        var seen = new HashSet<Guid>();
        var current = folderId;
        while (folders.TryGetValue(current, out var folder) && seen.Add(current))
        {
            chain.Add(new HierarchyScope(HierarchyScopeKind.Folder, current));
            if (folder.ParentId is not { } parent) break;
            current = parent;
        }
        chain.Reverse();
        return chain;
    }

    private static void ApplyAppearance(
        RhinoDoc document,
        Guid viewportId,
        EffectiveViewportAppearance appearance,
        IDictionary<Guid, Layer> layerBefore,
        IDictionary<Guid, ObjectAttributes> objectBefore)
    {
        foreach (var pair in appearance.Layers)
        {
            var source = document.Layers.FindId(pair.Key);
            if (source is null) continue;
            if (!layerBefore.ContainsKey(pair.Key))
                layerBefore[pair.Key] = CopyLayer(source);
            var layer = CopyLayer(source);
            var visible = pair.Value == LayerVisibilityOverride.Visible;
            layer.SetPerViewportVisible(viewportId, visible);
            layer.SetPerViewportPersistentVisibility(viewportId, visible);
            if (!document.Layers.Modify(layer, source.Index, quiet: true))
                throw new InvalidOperationException(
                    $"Rhino could not apply preview visibility for layer '{source.FullPath}'.");
        }

        foreach (var pair in appearance.Objects)
        {
            var item = document.Objects.FindId(pair.Key);
            if (item is null) continue;
            if (!objectBefore.ContainsKey(pair.Key))
                objectBefore[pair.Key] = item.Attributes.Duplicate();
            var attributes = item.Attributes.Duplicate();
            using var mode = DisplayModeDescription.GetDisplayMode(pair.Value.DisplayModeId)
                ?? throw new InvalidOperationException(
                    $"Display mode '{pair.Value.DisplayModeName}' is unavailable.");
            if (!RhinoObjectDisplayModeOverride.TrySet(attributes, mode, viewportId) ||
                !document.Objects.ModifyAttributes(item, attributes, quiet: true))
                throw new InvalidOperationException(
                    $"Rhino could not apply a preview display override to '{item.Id}'.");
        }
    }

    private static Layer CopyLayer(Layer source)
    {
        var copy = new Layer();
        copy.CopyAttributesFrom(source);
        return copy;
    }

    private static DraftLayoutThumbnailResult CapturePage(
        DraftLayoutThumbnailRequest request,
        RhinoDoc document,
        RhinoPageView page)
    {
        var pageWidthInches = page.PageWidth * RhinoMath.UnitScale(
            document.PageUnitSystem,
            UnitSystem.Inches);
        var pageHeightInches = page.PageHeight * RhinoMath.UnitScale(
            document.PageUnitSystem,
            UnitSystem.Inches);
        if (!double.IsFinite(pageWidthInches) || pageWidthInches <= 0 ||
            !double.IsFinite(pageHeightInches) || pageHeightInches <= 0)
            return Failure(request, "The draft page has an invalid paper size.");

        var requestedSize = new System.Drawing.Size(request.Key.Width, request.Key.Height);
        var captureDpi = Math.Min(
            request.Key.Width / pageWidthInches,
            request.Key.Height / pageHeightInches);
        using var settings = new ViewCaptureSettings(page, requestedSize, captureDpi)
        {
            DrawBackground = false,
            DrawBackgroundBitmap = false,
            DrawWallpaper = false,
            DrawGrid = false,
            DrawAxis = false,
            RasterMode = true,
            OutputColor = ViewCaptureSettings.ColorMode.PrintColor,
            UsePrintWidths = false,
        };
        settings.SetLayout(
            requestedSize,
            new System.Drawing.Rectangle(System.Drawing.Point.Empty, requestedSize));
        using var bitmap = RhinoDocumentThumbnailProvider.CaptureToBitmap(
            settings,
            request.Key.BackgroundArgb);
        if (bitmap is null)
            return Failure(request, "Rhino did not return a draft-layout preview.");

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return new DraftLayoutThumbnailResult(request.Key, stream.ToArray());
    }

    private static EditSheetThumbnailResult CapturePage(
        EditSheetThumbnailRequest request,
        RhinoDoc document,
        RhinoPageView page)
    {
        var pageWidthInches = page.PageWidth * RhinoMath.UnitScale(
            document.PageUnitSystem,
            UnitSystem.Inches);
        var pageHeightInches = page.PageHeight * RhinoMath.UnitScale(
            document.PageUnitSystem,
            UnitSystem.Inches);
        if (!double.IsFinite(pageWidthInches) || pageWidthInches <= 0 ||
            !double.IsFinite(pageHeightInches) || pageHeightInches <= 0)
            return Failure(request, "The edit preview has an invalid paper size.");

        var requestedSize = new System.Drawing.Size(request.Key.Width, request.Key.Height);
        var captureDpi = Math.Min(
            request.Key.Width / pageWidthInches,
            request.Key.Height / pageHeightInches);
        using var settings = new ViewCaptureSettings(page, requestedSize, captureDpi)
        {
            DrawBackground = false,
            DrawBackgroundBitmap = false,
            DrawWallpaper = false,
            DrawGrid = false,
            DrawAxis = false,
            RasterMode = true,
            OutputColor = ViewCaptureSettings.ColorMode.PrintColor,
            UsePrintWidths = false,
        };
        settings.SetLayout(
            requestedSize,
            new System.Drawing.Rectangle(System.Drawing.Point.Empty, requestedSize));
        using var bitmap = RhinoDocumentThumbnailProvider.CaptureToBitmap(
            settings,
            request.Key.BackgroundArgb);
        if (bitmap is null)
            return Failure(request, "Rhino did not return an edit preview.");

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return new EditSheetThumbnailResult(request.Key, stream.ToArray());
    }

    private static DraftLayoutThumbnailResult Failure(
        DraftLayoutThumbnailRequest request,
        string message) => new(request.Key, null, message);

    private static EditSheetThumbnailResult Failure(
        EditSheetThumbnailRequest request,
        string message) => new(request.Key, null, message);
}

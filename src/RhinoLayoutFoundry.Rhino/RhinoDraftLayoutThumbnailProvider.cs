using System.Drawing.Imaging;
using System.Collections.Concurrent;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Rhino;

internal sealed class RhinoDraftLayoutThumbnailProvider : IDraftLayoutThumbnailProvider
{
    private readonly DocumentStateStore _stateStore;
    private readonly ConcurrentDictionary<uint, bool> _modifiedBeforePreview = new();

    internal RhinoDraftLayoutThumbnailProvider(DocumentStateStore stateStore)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    }

    public void BeginSession(uint documentRuntimeSerialNumber)
    {
        var document = RhinoDoc.FromRuntimeSerialNumber(documentRuntimeSerialNumber);
        if (document is not null)
            _modifiedBeforePreview.TryAdd(documentRuntimeSerialNumber, document.Modified);
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

    public async Task CompleteSessionAsync(
        uint documentRuntimeSerialNumber,
        bool restoreOriginalModifiedState,
        bool endSession = true,
        CancellationToken cancellationToken = default)
    {
        await RhinoThumbnailCaptureGate.Gate.WaitAsync(cancellationToken);
        try
        {
            var found = endSession
                ? _modifiedBeforePreview.TryRemove(
                    documentRuntimeSerialNumber,
                    out var originalModifiedState)
                : _modifiedBeforePreview.TryGetValue(
                    documentRuntimeSerialNumber,
                    out originalModifiedState);
            if (!found ||
                !restoreOriginalModifiedState)
                return;

            if (!RhinoApp.InvokeRequired)
            {
                RestoreModifiedState(documentRuntimeSerialNumber, originalModifiedState);
                RestoreModifiedStateOnIdle(documentRuntimeSerialNumber, originalModifiedState);
                _ = RestoreModifiedStateAfterDialogCloseAsync(
                    documentRuntimeSerialNumber,
                    originalModifiedState);
                return;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                RestoreModifiedState(documentRuntimeSerialNumber, originalModifiedState);
                RestoreModifiedStateOnIdle(documentRuntimeSerialNumber, originalModifiedState);
                _ = RestoreModifiedStateAfterDialogCloseAsync(
                    documentRuntimeSerialNumber,
                    originalModifiedState);
                completion.SetResult();
            }));
            await completion.Task;
        }
        finally
        {
            RhinoThumbnailCaptureGate.Gate.Release();
        }
    }

    private DraftLayoutThumbnailResult CaptureOnUiThread(
        DraftLayoutThumbnailRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = RhinoDoc.FromRuntimeSerialNumber(request.Key.DocumentRuntimeSerialNumber);
        if (source is null)
            return Failure(request, "The Rhino document is no longer available.");

        RhinoPageView? page = null;
        var previousActiveView = source.Views.ActiveView;
        var documentWasModified = _modifiedBeforePreview.GetOrAdd(
            request.Key.DocumentRuntimeSerialNumber,
            source.Modified);
        var undoRecordingWasEnabled = source.UndoRecordingEnabled;
        var layerBefore = new Dictionary<Guid, Layer>();
        var objectBefore = new Dictionary<Guid, ObjectAttributes>();
        using var transientChanges = RhinoThumbnailCaptureGate.BeginTransientDocumentChanges();
        try
        {
            source.UndoRecordingEnabled = false;
            cancellationToken.ThrowIfCancellationRequested();
            var state = _stateStore.Get(source);
            page = CreateDraftPage(
                source,
                request.Change,
                state,
                layerBefore,
                objectBefore);
            source.Views.ActiveView = page;
            source.Views.Redraw();
            page.Redraw();
            cancellationToken.ThrowIfCancellationRequested();
            return CapturePage(request, source, page);
        }
        finally
        {
            RestoreAppearance(source, layerBefore, objectBefore);
            if (page is not null) page.Close();
            if (previousActiveView is not null)
                source.Views.ActiveView = previousActiveView;
            source.Views.Redraw();
            source.UndoRecordingEnabled = undoRecordingWasEnabled;
            source.Modified = documentWasModified;
            RestoreModifiedStateOnIdle(
                request.Key.DocumentRuntimeSerialNumber,
                documentWasModified);
        }
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

        RhinoPageView? previewPage = null;
        var previousActiveView = document.Views.ActiveView;
        var documentWasModified = _modifiedBeforePreview.GetOrAdd(
            request.Key.DocumentRuntimeSerialNumber,
            document.Modified);
        var undoRecordingWasEnabled = document.UndoRecordingEnabled;
        var layerBefore = new Dictionary<Guid, Layer>();
        var objectBefore = new Dictionary<Guid, ObjectAttributes>();
        using var transientChanges = RhinoThumbnailCaptureGate.BeginTransientDocumentChanges();
        try
        {
            document.UndoRecordingEnabled = false;
            previewPage = sourcePage.Duplicate(duplicatePageGeometry: true)
                ?? throw new InvalidOperationException("Rhino could not duplicate the sheet for preview.");
            previewPage.PageName = $"__FoundryEditPreview_{Guid.NewGuid():N}";
            document.Views.ActiveView = previewPage;
            previewPage.SetPageAsActive();
            ApplyEditAssignments(
                document,
                sourcePage,
                previewPage,
                request,
                _stateStore.Get(document),
                layerBefore,
                objectBefore);
            document.Views.Redraw();
            previewPage.Redraw();
            cancellationToken.ThrowIfCancellationRequested();
            return CapturePage(request, document, previewPage);
        }
        finally
        {
            RestoreAppearance(document, layerBefore, objectBefore);
            if (previewPage is not null) previewPage.Close();
            if (previousActiveView is not null)
                document.Views.ActiveView = previousActiveView;
            document.Views.Redraw();
            document.UndoRecordingEnabled = undoRecordingWasEnabled;
            document.Modified = documentWasModified;
            RestoreModifiedStateOnIdle(request.Key.DocumentRuntimeSerialNumber, documentWasModified);
        }
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

        for (var index = 0; index < sourceDetails.Length; index++)
        {
            var sourceDetail = sourceDetails[index];
            var previewDetail = previewDetails[index];
            if (!requestedById.TryGetValue(sourceDetail.Viewport.Id, out var requested))
                throw new InvalidOperationException("A detail preview assignment is unavailable.");

            var cameraChanged = !string.IsNullOrWhiteSpace(requested.NamedViewName);
            var displayModeChanged = requested.DisplayModeId is { } displayModeId &&
                                     previewDetail.Viewport.DisplayMode.Id != displayModeId;
            if (!cameraChanged && !displayModeChanged) continue;

            // A duplicated detail can retain Rhino's cached display pipeline after
            // its camera changes. Rebuild only the changed detail so the capture is
            // fresh while every untouched sibling keeps its exact duplicated camera.
            var sourceViewport = sourceDetail.Viewport;
            var bounds = sourceDetail.DetailGeometry.GetBoundingBox(true);
            var slot = new DetailSlotRecipe(
                Guid.NewGuid(),
                string.IsNullOrWhiteSpace(sourceDetail.Attributes.Name)
                    ? sourceViewport.Name
                    : sourceDetail.Attributes.Name,
                bounds.Min.X,
                bounds.Min.Y,
                bounds.Max.X,
                bounds.Max.Y,
                sourceViewport.IsPerspectiveProjection ? "Perspective" : "Top",
                sourceDetail.DetailGeometry.IsParallelProjection
                    ? sourceDetail.DetailGeometry.PageToModelRatio
                    : null,
                sourceDetail.DetailGeometry.IsProjectionLocked,
                requested.DisplayModeId,
                null,
                [sourceViewport.CameraLocation.X, sourceViewport.CameraLocation.Y, sourceViewport.CameraLocation.Z],
                [sourceViewport.CameraTarget.X, sourceViewport.CameraTarget.Y, sourceViewport.CameraTarget.Z],
                [sourceViewport.CameraUp.X, sourceViewport.CameraUp.Y, sourceViewport.CameraUp.Z]);
            var rebuiltViewportId = RhinoDocumentMutationService.CreateDetail(
                document,
                previewPage,
                slot,
                document.PageUnitSystem,
                1.0,
                requested.NamedViewName,
                sourceDetail.Attributes.LayerIndex);
            var rebuilt = previewPage.GetDetailViews().FirstOrDefault(detail =>
                detail.Viewport.Id == rebuiltViewportId)
                ?? throw new InvalidOperationException("Rhino could not find a rebuilt preview detail.");
            if (!cameraChanged)
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
            previewDetails[index] = rebuilt;
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
            var previewDetail = previewDetails[index];
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

    private static void ReplaceDirectAssignment(
        List<AppearanceStateAssignment> assignments,
        HierarchyScope target,
        Guid? stateId)
    {
        assignments.RemoveAll(item => item.Target == target);
        if (stateId is { } id)
            assignments.Add(new AppearanceStateAssignment(Guid.NewGuid(), target, id));
    }

    private static void RestoreModifiedState(
        uint documentRuntimeSerialNumber,
        bool modified)
    {
        var document = RhinoDoc.FromRuntimeSerialNumber(documentRuntimeSerialNumber);
        if (document is not null) document.Modified = modified;
    }

    private static async Task RestoreModifiedStateAfterDialogCloseAsync(
        uint documentRuntimeSerialNumber,
        bool modified)
    {
        // Eto's modal teardown completes after its Closed event. Give that
        // teardown one short turn to finish, then make the final restoration on
        // Rhino's UI thread. This prevents the temporary page close from
        // winning the race and leaving a cancelled preview marked as an edit.
        await Task.Delay(400);
        RhinoApp.InvokeOnUiThread((Action)(() =>
            RestoreModifiedState(documentRuntimeSerialNumber, modified)));
    }

    private static void RestoreModifiedStateOnIdle(
        uint documentRuntimeSerialNumber,
        bool modified)
    {
        // Closing a page view is finalized by Rhino during an idle turn. The
        // document can therefore be marked modified after the first idle
        // callback has restored the original flag. Repeat the restoration over
        // the following idle turns so the deferred close cannot leak preview
        // bookkeeping into the user's document state.
        var remainingPasses = 3;
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            var document = RhinoDoc.FromRuntimeSerialNumber(documentRuntimeSerialNumber);
            if (document is not null) document.Modified = modified;
            remainingPasses--;
            if (remainingPasses > 0) return;
            RhinoApp.Idle -= handler;
        };
        RhinoApp.Idle += handler;
    }

    private static RhinoPageView CreateDraftPage(
        RhinoDoc document,
        CreateSheetFromTemplateChange create,
        DocumentState state,
        IDictionary<Guid, Layer> layerBefore,
        IDictionary<Guid, ObjectAttributes> objectBefore)
    {
        var recipeUnit = RhinoDocumentMutationService.ParseUnitSystem(
            create.Template.Paper.UnitSystem);
        var pageScale = RhinoMath.UnitScale(recipeUnit, document.PageUnitSystem);
        var page = document.Views.AddPageView(
            $"__FoundryPreview_{Guid.NewGuid():N}",
            create.Template.Paper.Width * pageScale,
            create.Template.Paper.Height * pageScale)
            ?? throw new InvalidOperationException("Rhino could not create the isolated preview page.");

        int? detailLayerIndex = null;
        if (!create.UseDedicatedDetailLayer && create.DetailLayerId is { } layerId)
        {
            detailLayerIndex = document.Layers.FirstOrDefault(layer =>
                layer.Id == layerId && !layer.IsDeleted && !layer.IsReference)?.Index;
        }

        foreach (var slot in create.Template.DetailSlots)
        {
            RhinoDocumentMutationService.CreateDetail(
                document,
                page,
                slot,
                recipeUnit,
                pageScale,
                create.NamedViewAssignments.GetValueOrDefault(slot.Id),
                detailLayerIndex);
        }

        var titleBlockData = new SheetTitleBlockData(
            create.SheetNumber,
            create.InitialRevisions?.ToArray() ?? []);
        if (create.Template.TitleBlock is { } titleBlock)
        {
            RhinoDocumentMutationService.CreateTitleBlock(
                document,
                page,
                titleBlock,
                create.Template.Paper,
                create.ProjectData ?? state.ProjectInfo,
                titleBlockData,
                create.Template.DetailSlots);
        }

        ApplySheetAppearance(document, page, create, state, layerBefore, objectBefore);
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

    private static void RestoreAppearance(
        RhinoDoc document,
        IReadOnlyDictionary<Guid, Layer> layerBefore,
        IReadOnlyDictionary<Guid, ObjectAttributes> objectBefore)
    {
        foreach (var pair in layerBefore)
        {
            var source = document.Layers.FindId(pair.Key);
            if (source is not null)
                document.Layers.Modify(pair.Value, source.Index, quiet: true);
        }
        foreach (var pair in objectBefore)
        {
            var item = document.Objects.FindId(pair.Key);
            if (item is not null)
                document.Objects.ModifyAttributes(item, pair.Value, quiet: true);
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

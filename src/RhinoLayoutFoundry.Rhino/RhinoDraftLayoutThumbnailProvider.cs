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

    public async Task CompleteSessionAsync(
        uint documentRuntimeSerialNumber,
        bool restoreOriginalModifiedState,
        CancellationToken cancellationToken = default)
    {
        await RhinoThumbnailCaptureGate.Gate.WaitAsync(cancellationToken);
        try
        {
            if (!_modifiedBeforePreview.TryRemove(
                    documentRuntimeSerialNumber,
                    out var originalModifiedState) ||
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

        foreach (var detail in page.GetDetailViews())
        {
            var detailScope = new HierarchyScope(HierarchyScopeKind.Detail, detail.Viewport.Id);
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

    private static DraftLayoutThumbnailResult Failure(
        DraftLayoutThumbnailRequest request,
        string message) => new(request.Key, null, message);
}

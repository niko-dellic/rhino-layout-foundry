using System.Drawing.Imaging;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Rhino;

internal sealed class RhinoNamedViewThumbnailProvider : INamedViewThumbnailProvider
{
    public async Task<NamedViewThumbnailResult> CaptureAsync(
        NamedViewThumbnailRequest request,
        CancellationToken cancellationToken)
    {
        var enteredGate = false;
        try
        {
            await RhinoThumbnailCaptureGate.Gate.WaitAsync(cancellationToken);
            enteredGate = true;
            if (!RhinoApp.InvokeRequired)
                return CaptureOnUiThread(request, cancellationToken);

            var completion = new TaskCompletionSource<NamedViewThumbnailResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                try
                {
                    completion.SetResult(CaptureOnUiThread(request, cancellationToken));
                }
                catch (Exception exception)
                {
                    completion.SetResult(new NamedViewThumbnailResult(
                        request.Key,
                        null,
                        exception.Message));
                }
            }));
            return await completion.Task;
        }
        catch (OperationCanceledException)
        {
            return new NamedViewThumbnailResult(
                request.Key,
                null,
                "Named-view thumbnail capture was cancelled.");
        }
        catch (Exception exception)
        {
            return new NamedViewThumbnailResult(request.Key, null, exception.Message);
        }
        finally
        {
            if (enteredGate) RhinoThumbnailCaptureGate.Gate.Release();
        }
    }

    private static NamedViewThumbnailResult CaptureOnUiThread(
        NamedViewThumbnailRequest request,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Failure(request, "Named-view thumbnail capture was cancelled.");

        var document = RhinoDoc.FromRuntimeSerialNumber(request.Key.DocumentRuntimeSerialNumber);
        if (document is null)
            return Failure(request, "The Rhino document is no longer available.");
        var namedViewIndex = document.NamedViews.FindByName(request.Key.NamedViewName);
        if (namedViewIndex < 0)
            return Failure(request, "The named view no longer exists.");

        // Use an existing standard view as Rhino's display pipeline requires a
        // live RhinoView. Capture through the same ViewCaptureSettings pipeline
        // as sheet thumbnails so display modes and viewport appearance overrides
        // are evaluated by Rhino rather than approximated by the Foundry UI.
        var fallbackDisplayModeId = document.Views.ActiveView?.ActiveViewport.DisplayMode.Id;
        var view = document.Views.GetStandardRhinoViews().FirstOrDefault();
        if (view is null)
            return Failure(request, "No standard Rhino viewport is available for preview capture.");
        using var previous = new ViewInfo(view.ActiveViewport);
        var previousDisplayModeId = view.ActiveViewport.DisplayMode.Id;
        var layerBefore = new Dictionary<Guid, Layer>();
        var objectBefore = new Dictionary<Guid, ObjectAttributes>();
        var documentWasModified = document.Modified;
        var undoRecordingWasEnabled = document.UndoRecordingEnabled;
        DisplayModeDescription? requestedDisplayMode = null;
        using var transientChanges = RhinoThumbnailCaptureGate.BeginTransientDocumentChanges();
        try
        {
            document.UndoRecordingEnabled = false;
            if (!document.NamedViews.RestoreWithAspectRatio(namedViewIndex, view.ActiveViewport))
                return Failure(request, "Rhino could not restore the named view for preview capture.");
            var effectiveDisplayModeId = request.Key.DisplayModeId ?? fallbackDisplayModeId;
            if (effectiveDisplayModeId is { } displayModeId)
            {
                requestedDisplayMode = DisplayModeDescription.GetDisplayMode(displayModeId);
                if (requestedDisplayMode is null)
                    return Failure(request, "The requested display mode is unavailable.");
                view.ActiveViewport.DisplayMode = requestedDisplayMode;
            }

            ApplyAppearance(
                document,
                view.ActiveViewport.Id,
                request.Appearance,
                layerBefore,
                objectBefore);

            var requestedSize = new System.Drawing.Size(request.Key.Width, request.Key.Height);
            using var captureSettings = new ViewCaptureSettings(view, requestedSize, 96)
            {
                DrawBackground = false,
                DrawBackgroundBitmap = false,
                DrawWallpaper = false,
                DrawGrid = false,
                DrawAxis = false,
                RasterMode = true,
                OutputColor = ViewCaptureSettings.ColorMode.PrintColor,
                UsePrintWidths = false,
                ApplyDisplayModeThicknessScales = true,
            };
            captureSettings.SetLayout(
                requestedSize,
                new System.Drawing.Rectangle(System.Drawing.Point.Empty, requestedSize));
            using var bitmap = RhinoDocumentThumbnailProvider.CaptureToBitmap(
                captureSettings,
                request.Key.BackgroundArgb);
            if (bitmap is null)
                return Failure(request, "Rhino did not return a named-view preview.");

            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return new NamedViewThumbnailResult(request.Key, stream.ToArray());
        }
        finally
        {
            RestoreAppearance(document, layerBefore, objectBefore);
            view.ActiveViewport.SetViewProjection(previous.Viewport, false);
            using var previousDisplayMode = DisplayModeDescription.GetDisplayMode(previousDisplayModeId);
            if (previousDisplayMode is not null)
                view.ActiveViewport.DisplayMode = previousDisplayMode;
            requestedDisplayMode?.Dispose();
            document.UndoRecordingEnabled = undoRecordingWasEnabled;
            document.Modified = documentWasModified;
        }
    }

    private static void ApplyAppearance(
        RhinoDoc document,
        Guid viewportId,
        EffectiveViewportAppearance? appearance,
        IDictionary<Guid, Layer> layerBefore,
        IDictionary<Guid, ObjectAttributes> objectBefore)
    {
        if (appearance is null) return;
        foreach (var pair in appearance.Layers)
        {
            var source = document.Layers.FindId(pair.Key);
            if (source is null) continue;
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

    private static NamedViewThumbnailResult Failure(
        NamedViewThumbnailRequest request,
        string message) => new(request.Key, null, message);
}

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

        // A standalone RhinoViewport is supported by Rhino's image-creation
        // pipeline and keeps gallery rendering isolated from the user's camera.
        // Do not restore a named view into a live model viewport here: Rhino can
        // paint that intermediate projection while this asynchronous gallery is
        // loading, even if the original projection is restored afterward.
        var fallbackDisplayModeId = document.Views.ActiveView?.ActiveViewport.DisplayMode.Id;
        var requestedSize = new System.Drawing.Size(request.Key.Width, request.Key.Height);
        using var previewViewport = new RhinoViewport
        {
            Size = requestedSize,
        };
        var layerBefore = new Dictionary<Guid, Layer>();
        var objectBefore = new Dictionary<Guid, ObjectAttributes>();
        DisplayModeDescription? requestedDisplayMode = null;
        using var session = new RhinoPreviewSession(document);
        session.Restore("Dispose requested display mode", () => requestedDisplayMode?.Dispose());
        session.Restore("Restore named-view appearance", () =>
            RhinoPreviewSession.RestoreAppearance(document, layerBefore, objectBefore));
        if (!document.NamedViews.RestoreWithAspectRatio(namedViewIndex, previewViewport))
            return Failure(request, "Rhino could not restore the named view for preview capture.");
        var effectiveDisplayModeId = request.Key.DisplayModeId ?? fallbackDisplayModeId;
        if (effectiveDisplayModeId is { } displayModeId)
        {
            requestedDisplayMode = DisplayModeDescription.GetDisplayMode(displayModeId);
            if (requestedDisplayMode is null)
                return Failure(request, "The requested display mode is unavailable.");
            previewViewport.DisplayMode = requestedDisplayMode;
        }

        ApplyAppearance(
            document,
            previewViewport.Id,
            request.Appearance,
            layerBefore,
            objectBefore);

        using var captureSettings = new ViewCaptureSettings
        {
            Document = document,
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
        captureSettings.SetViewport(previewViewport);
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

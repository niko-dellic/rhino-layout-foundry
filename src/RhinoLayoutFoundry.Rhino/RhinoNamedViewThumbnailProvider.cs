using System.Drawing.Imaging;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
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
        // live RhinoView. The projection is restored immediately and is never
        // redrawn to screen, avoiding a temporary floating viewport or a 3DM edit.
        var view = document.Views.GetStandardRhinoViews().FirstOrDefault();
        if (view is null)
            return Failure(request, "No standard Rhino viewport is available for preview capture.");
        using var previous = new ViewInfo(view.ActiveViewport);
        var previousDisplayModeId = view.ActiveViewport.DisplayMode.Id;
        DisplayModeDescription? requestedDisplayMode = null;
        try
        {
            if (!document.NamedViews.RestoreWithAspectRatio(namedViewIndex, view.ActiveViewport))
                return Failure(request, "Rhino could not restore the named view for preview capture.");
            if (request.Key.DisplayModeId is { } displayModeId)
            {
                requestedDisplayMode = DisplayModeDescription.GetDisplayMode(displayModeId);
                if (requestedDisplayMode is null)
                    return Failure(request, "The requested display mode is unavailable.");
                view.ActiveViewport.DisplayMode = requestedDisplayMode;
            }

            var capture = new ViewCapture
            {
                Width = request.Key.Width,
                Height = request.Key.Height,
                ScaleScreenItems = false,
                DrawAxes = false,
                DrawGrid = false,
                DrawGridAxes = false,
                TransparentBackground = false,
            };
            using var bitmap = capture.CaptureToBitmap(view);
            if (bitmap is null)
                return Failure(request, "Rhino did not return a named-view preview.");

            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return new NamedViewThumbnailResult(request.Key, stream.ToArray());
        }
        finally
        {
            view.ActiveViewport.SetViewProjection(previous.Viewport, false);
            using var previousDisplayMode = DisplayModeDescription.GetDisplayMode(previousDisplayModeId);
            if (previousDisplayMode is not null)
                view.ActiveViewport.DisplayMode = previousDisplayMode;
            requestedDisplayMode?.Dispose();
        }
    }

    private static NamedViewThumbnailResult Failure(
        NamedViewThumbnailRequest request,
        string message) => new(request.Key, null, message);
}

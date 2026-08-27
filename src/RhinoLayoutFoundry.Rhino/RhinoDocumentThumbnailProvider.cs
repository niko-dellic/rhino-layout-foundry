using System.Drawing.Imaging;
using Rhino;
using Rhino.Display;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Rhino;

internal sealed class RhinoDocumentThumbnailProvider : IDocumentThumbnailProvider
{
    public Task<OverviewThumbnailResult> CaptureAsync(
        OverviewThumbnailRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!RhinoApp.InvokeRequired)
        {
            return Task.FromResult(CaptureOnUiThread(request, cancellationToken));
        }

        var completion = new TaskCompletionSource<OverviewThumbnailResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        RhinoApp.InvokeOnUiThread((Action)(() =>
        {
            try
            {
                completion.SetResult(CaptureOnUiThread(request, cancellationToken));
            }
            catch (Exception exception)
            {
                completion.SetResult(new OverviewThumbnailResult(
                    request.Key,
                    null,
                    exception.Message));
            }
        }));
        return completion.Task;
    }

    private static OverviewThumbnailResult CaptureOnUiThread(
        OverviewThumbnailRequest request,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new OverviewThumbnailResult(request.Key, null, "Thumbnail capture was cancelled.");
        }

        var document = RhinoDoc.FromRuntimeSerialNumber(request.Key.DocumentRuntimeSerialNumber);
        var page = document?.Views.GetPageViews()
            .FirstOrDefault(candidate => candidate.MainViewport.Id == request.Key.SheetPageViewId);
        if (page is null)
        {
            return new OverviewThumbnailResult(request.Key, null, "The layout sheet no longer exists.");
        }

        var capture = new ViewCapture
        {
            Width = request.Key.Width,
            Height = request.Key.Height,
            DrawGrid = false,
            DrawAxes = false,
            DrawGridAxes = false,
            ScaleScreenItems = false,
            TransparentBackground = false,
        };
        using var bitmap = capture.CaptureToBitmap(page);
        if (bitmap is null)
        {
            return new OverviewThumbnailResult(request.Key, null, "Rhino did not return a page preview.");
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return new OverviewThumbnailResult(request.Key, stream.ToArray());
    }
}

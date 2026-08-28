using System.Drawing.Imaging;
using Rhino;
using Rhino.Display;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Rhino;

internal sealed class RhinoDocumentThumbnailProvider : IDocumentThumbnailProvider
{
    public async Task<OverviewThumbnailResult> CaptureAsync(
        OverviewThumbnailRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var enteredGate = false;
        try
        {
            await RhinoThumbnailCaptureGate.Gate.WaitAsync(cancellationToken);
            enteredGate = true;
            if (!RhinoApp.InvokeRequired)
            {
                return CaptureOnUiThread(request, cancellationToken);
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
            return await completion.Task;
        }
        catch (OperationCanceledException)
        {
            return new OverviewThumbnailResult(request.Key, null, "Thumbnail capture was cancelled.");
        }
        finally
        {
            if (enteredGate) RhinoThumbnailCaptureGate.Gate.Release();
        }
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

        // Capturing the RhinoPageView directly captures its on-screen viewport,
        // including the gray area surrounding the paper. Build print-preview
        // settings from the page instead so the bitmap media is the sheet itself.
        using var pageSettings = new ViewCaptureSettings(page, 72.0)
        {
            // Thumbnail and canvas previews represent the printed sheet, not
            // Rhino's configured viewport background color.
            DrawBackground = false,
            DrawBackgroundBitmap = false,
            DrawWallpaper = false,
            DrawGrid = false,
            DrawAxis = false,
            RasterMode = true,
            OutputColor = ViewCaptureSettings.ColorMode.PrintColor,
            UsePrintWidths = false,
        };
        using var previewSettings = pageSettings.CreatePreviewSettings(
            new System.Drawing.Size(request.Key.Width, request.Key.Height));
        if (previewSettings is null)
        {
            return new OverviewThumbnailResult(
                request.Key,
                null,
                "Rhino could not create page-only preview settings.");
        }

        using var bitmap = ViewCapture.CaptureToBitmap(previewSettings);
        if (bitmap is null)
        {
            return new OverviewThumbnailResult(request.Key, null, "Rhino did not return a page preview.");
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return new OverviewThumbnailResult(request.Key, stream.ToArray());
    }
}

internal static class RhinoThumbnailCaptureGate
{
    internal static readonly SemaphoreSlim Gate = new(1, 1);
}

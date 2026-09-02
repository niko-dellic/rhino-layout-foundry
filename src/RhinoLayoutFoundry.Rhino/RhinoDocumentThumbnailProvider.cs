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

        var pageWidthInches = page.PageWidth * RhinoMath.UnitScale(
            document!.PageUnitSystem,
            UnitSystem.Inches);
        var pageHeightInches = page.PageHeight * RhinoMath.UnitScale(
            document.PageUnitSystem,
            UnitSystem.Inches);
        if (!double.IsFinite(pageWidthInches) || pageWidthInches <= 0 ||
            !double.IsFinite(pageHeightInches) || pageHeightInches <= 0)
        {
            return new OverviewThumbnailResult(
                request.Key,
                null,
                "The layout sheet has an invalid paper size.");
        }

        var requestedSize = new System.Drawing.Size(request.Key.Width, request.Key.Height);
        var captureDpi = Math.Min(
            request.Key.Width / pageWidthInches,
            request.Key.Height / pageHeightInches);

        // CreatePreviewSettings scales an existing print-preview projection.
        // Rhino 8 on Retina macOS can allocate the requested 2x bitmap while
        // leaving page/detail drawing at the previous 1x projection, producing
        // a correct white media rectangle with all content confined to its
        // upper-left quadrant. Construct the capture at its final media size and
        // DPI instead so Rhino builds every detail viewport at the same scale.
        using var captureSettings = new ViewCaptureSettings(page, requestedSize, captureDpi)
        {
            // Preserve Rhino's canonical per-detail display pipeline while
            // presenting page and object colors as Rhino would for print. The
            // framebuffer hook below supplies Foundry's preview surface color.
            DrawBackground = false,
            DrawBackgroundBitmap = false,
            DrawWallpaper = false,
            DrawGrid = false,
            DrawAxis = false,
            RasterMode = true,
            OutputColor = ViewCaptureSettings.ColorMode.PrintColor,
            UsePrintWidths = false,
        };
        captureSettings.SetLayout(
            requestedSize,
            new System.Drawing.Rectangle(System.Drawing.Point.Empty, requestedSize));

        var bitmap = CaptureToBitmap(captureSettings, request.Key.BackgroundArgb);

        using (bitmap)
        {
            if (bitmap is null)
            {
                return new OverviewThumbnailResult(request.Key, null, "Rhino did not return a page preview.");
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return new OverviewThumbnailResult(request.Key, stream.ToArray());
        }
    }

    internal static System.Drawing.Bitmap? CaptureToBitmap(
        ViewCaptureSettings captureSettings,
        uint backgroundArgb)
    {
        var requestedBackground = backgroundArgb == 0
            ? (System.Drawing.Color?)null
            : System.Drawing.Color.FromArgb(unchecked((int)backgroundArgb));
        EventHandler<InitFrameBufferEventArgs>? initializeFrameBuffer = requestedBackground is { } fill
            ? (_, eventArgs) => eventArgs.SetFill(fill)
            : null;
        if (initializeFrameBuffer is not null)
            DisplayPipeline.InitFrameBuffer += initializeFrameBuffer;

        try
        {
            // Rhino initializes a separate framebuffer for page and detail
            // display pipelines. Supplying the Foundry preview fill here keeps
            // both surfaces consistent without changing the user's Rhino
            // appearance or display-mode settings.
            return ViewCapture.CaptureToBitmap(captureSettings);
        }
        finally
        {
            if (initializeFrameBuffer is not null)
                DisplayPipeline.InitFrameBuffer -= initializeFrameBuffer;
        }
    }
}

internal static class RhinoThumbnailCaptureGate
{
    internal static readonly SemaphoreSlim Gate = new(1, 1);
}

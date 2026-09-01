using Rhino;
using Rhino.Display;
using Rhino.FileIO;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Rhino;

internal sealed class RhinoLayoutPdfExportService : ILayoutPdfExportService
{
    public Task<LayoutPdfExportResult> ExportAsync(
        LayoutPdfExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!RhinoApp.InvokeRequired)
        {
            return Task.FromResult(ExportOnUiThread(request, cancellationToken));
        }

        var completion = new TaskCompletionSource<LayoutPdfExportResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        RhinoApp.InvokeOnUiThread((Action)(() =>
        {
            try
            {
                completion.SetResult(ExportOnUiThread(request, cancellationToken));
            }
            catch (Exception exception)
            {
                completion.SetResult(new LayoutPdfExportResult(false, 0, exception.Message));
            }
        }));
        return completion.Task;
    }

    private static LayoutPdfExportResult ExportOnUiThread(
        LayoutPdfExportRequest request,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new LayoutPdfExportResult(false, 0, "PDF export was cancelled.");
        }

        if (request.SheetPageViewIds.Count == 0)
        {
            return new LayoutPdfExportResult(false, 0, "There are no layouts in this print scope.");
        }

        if (request.DotsPerInch <= 0 || !double.IsFinite(request.DotsPerInch))
        {
            return new LayoutPdfExportResult(false, 0, "The PDF resolution is invalid.");
        }

        var document = RhinoDoc.FromRuntimeSerialNumber(request.DocumentRuntimeSerialNumber);
        if (document is null || RhinoDoc.ActiveDoc?.RuntimeSerialNumber != request.DocumentRuntimeSerialNumber)
        {
            return new LayoutPdfExportResult(
                false,
                0,
                "The target Rhino document was closed or is no longer active.");
        }

        var pagesById = document.Views.GetPageViews()
            .ToDictionary(page => page.MainViewport.Id);
        var pages = new List<RhinoPageView>(request.SheetPageViewIds.Count);
        foreach (var pageViewId in request.SheetPageViewIds)
        {
            if (!pagesById.TryGetValue(pageViewId, out var page))
            {
                return new LayoutPdfExportResult(
                    false,
                    0,
                    "One or more layouts no longer exist. Refresh and try again.");
            }

            pages.Add(page);
        }

        try
        {
            var finalPath = Path.GetFullPath(request.FilePath);
            var directory = Path.GetDirectoryName(finalPath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return new LayoutPdfExportResult(false, 0, "The selected output folder no longer exists.");
            }

            var temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileNameWithoutExtension(finalPath)}.{Guid.NewGuid():N}.tmp.pdf");
            var pdf = FilePdf.Create();
            var addedPages = 0;
            try
            {
                foreach (var page in pages)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return new LayoutPdfExportResult(false, addedPages, "PDF export was cancelled.");
                    }

                    // Match Rhino's print-oriented output rather than the layout's
                    // on-screen display. In particular, layout/detail background
                    // colors are preview chrome and must not tint the PDF page.
                    using var settings = new ViewCaptureSettings(page, request.DotsPerInch)
                    {
                        OutputColor = ViewCaptureSettings.ColorMode.PrintColor,
                        UsePrintWidths = true,
                        RasterMode = false,
                        DrawBackground = false,
                        DrawBackgroundBitmap = false,
                        DrawWallpaper = false,
                        DrawGrid = false,
                        DrawAxis = false,
                        DrawMargins = false,
                    };
                    // FilePdf does not initialize layout/detail framebuffers with
                    // Rhino Print's white media fill on macOS. Without this hook,
                    // the on-screen gray layout surface leaks into the PDF even
                    // when DrawBackground is false.
                    DisplayPipeline.InitFrameBuffer += InitializePrintFrameBuffer;
                    try
                    {
                        pdf.AddPage(settings);
                    }
                    finally
                    {
                        DisplayPipeline.InitFrameBuffer -= InitializePrintFrameBuffer;
                    }
                    addedPages++;
                }

                pdf.Write(temporaryPath);
                if (!File.Exists(temporaryPath) || new FileInfo(temporaryPath).Length == 0)
                {
                    return new LayoutPdfExportResult(false, 0, "Rhino did not create the PDF file.");
                }

                File.Move(temporaryPath, finalPath, overwrite: true);
                return new LayoutPdfExportResult(true, addedPages);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        catch (Exception exception)
        {
            return new LayoutPdfExportResult(false, 0, $"Rhino could not create the PDF: {exception.Message}");
        }
    }

    private static void InitializePrintFrameBuffer(
        object? sender,
        InitFrameBufferEventArgs eventArgs) =>
        eventArgs.SetFill(System.Drawing.Color.White);
}

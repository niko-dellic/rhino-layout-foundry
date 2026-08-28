using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Rhino;
using Rhino.Display;
using RhinoLayoutFoundry.Core.Observer;
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
            RasterMode = false,
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

        // CreatePreviewSettings scales the page media but Rhino versions have
        // differed in which output flags they copy. Reassert print-preview
        // behavior on the final settings passed to CaptureToBitmap.
        previewSettings.DrawBackground = false;
        previewSettings.DrawBackgroundBitmap = false;
        previewSettings.DrawWallpaper = false;
        previewSettings.DrawGrid = false;
        previewSettings.DrawAxis = false;
        previewSettings.RasterMode = false;
        previewSettings.OutputColor = ViewCaptureSettings.ColorMode.PrintColor;
        previewSettings.UsePrintWidths = false;

        using var capturedBitmap = ViewCapture.CaptureToBitmap(previewSettings);
        if (capturedBitmap is null)
        {
            return new OverviewThumbnailResult(request.Key, null, "Rhino did not return a page preview.");
        }

        using var bitmap = NormalizePrintBackground(capturedBitmap, page);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return new OverviewThumbnailResult(request.Key, stream.ToArray());
    }

    private static System.Drawing.Bitmap NormalizePrintBackground(
        System.Drawing.Bitmap source,
        RhinoPageView page)
    {
        var bitmap = new System.Drawing.Bitmap(
            source.Width,
            source.Height,
            PixelFormat.Format32bppArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.DrawImageUnscaled(source, 0, 0);
        }

        var regions = new List<System.Drawing.Rectangle>
        {
            new(0, 0, bitmap.Width, bitmap.Height),
        };
        foreach (var detail in page.GetDetailViews())
        {
            var box = detail.DetailGeometry.GetBoundingBox(true);
            if (!box.IsValid) continue;
            var normalized = ObserverDetailBounds.FromPageCoordinates(
                box.Min.X,
                box.Min.Y,
                box.Max.X,
                box.Max.Y,
                page.PageWidth,
                page.PageHeight);
            var left = Math.Clamp((int)Math.Round(normalized.X * bitmap.Width), 0, bitmap.Width - 1);
            var top = Math.Clamp((int)Math.Round(normalized.Y * bitmap.Height), 0, bitmap.Height - 1);
            var right = Math.Clamp(
                (int)Math.Round((normalized.X + normalized.Width) * bitmap.Width),
                left + 1,
                bitmap.Width);
            var bottom = Math.Clamp(
                (int)Math.Round((normalized.Y + normalized.Height) * bitmap.Height),
                top + 1,
                bitmap.Height);
            regions.Add(new System.Drawing.Rectangle(left, top, right - left, bottom - top));
        }

        WhitenSampledBackgrounds(bitmap, regions);
        return bitmap;
    }

    private static void WhitenSampledBackgrounds(
        System.Drawing.Bitmap bitmap,
        IReadOnlyList<System.Drawing.Rectangle> regions)
    {
        var bounds = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = Math.Abs(data.Stride);
            var pixels = new byte[rowBytes * bitmap.Height];
            for (var y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(
                    IntPtr.Add(data.Scan0, y * data.Stride),
                    pixels,
                    y * rowBytes,
                    rowBytes);
            }
            foreach (var region in regions)
            {
                var palette = SampleBackgroundPalette(pixels, rowBytes, region);
                if (palette.Count == 0) continue;
                WhitenMatchingPixels(pixels, rowBytes, region, palette);
            }
            for (var y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(
                    pixels,
                    y * rowBytes,
                    IntPtr.Add(data.Scan0, y * data.Stride),
                    rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static IReadOnlyList<Rgb> SampleBackgroundPalette(
        byte[] pixels,
        int rowBytes,
        System.Drawing.Rectangle region)
    {
        if (region.Width < 3 || region.Height < 3) return [];
        var inset = Math.Max(2, Math.Min(region.Width, region.Height) / 20);
        var left = Math.Min(region.Right - 1, region.Left + inset);
        var right = Math.Max(region.Left, region.Right - 1 - inset);
        var top = Math.Min(region.Bottom - 1, region.Top + inset);
        var bottom = Math.Max(region.Top, region.Bottom - 1 - inset);
        var middleX = region.Left + region.Width / 2;
        var middleY = region.Top + region.Height / 2;
        var samples = new[]
        {
            ReadRgb(pixels, rowBytes, left, top),
            ReadRgb(pixels, rowBytes, right, top),
            ReadRgb(pixels, rowBytes, left, bottom),
            ReadRgb(pixels, rowBytes, right, bottom),
            ReadRgb(pixels, rowBytes, middleX, top),
            ReadRgb(pixels, rowBytes, middleX, bottom),
            ReadRgb(pixels, rowBytes, left, middleY),
            ReadRgb(pixels, rowBytes, right, middleY),
        };

        var clusters = new List<(Rgb Color, int Count)>();
        foreach (var sample in samples)
        {
            var index = clusters.FindIndex(cluster => ColorsMatch(cluster.Color, sample, 12));
            if (index < 0)
            {
                clusters.Add((sample, 1));
                continue;
            }
            var cluster = clusters[index];
            clusters[index] = (cluster.Color, cluster.Count + 1);
        }

        return clusters
            .Where(cluster => cluster.Count >= 2 && !IsNearlyWhite(cluster.Color))
            .OrderByDescending(cluster => cluster.Count)
            .Select(cluster => cluster.Color)
            .ToArray();
    }

    private static void WhitenMatchingPixels(
        byte[] pixels,
        int rowBytes,
        System.Drawing.Rectangle region,
        IReadOnlyList<Rgb> palette)
    {
        for (var y = region.Top; y < region.Bottom; y++)
        {
            for (var x = region.Left; x < region.Right; x++)
            {
                var offset = PixelOffset(rowBytes, x, y);
                var color = new Rgb(pixels[offset + 2], pixels[offset + 1], pixels[offset]);
                if (!MatchesAny(palette, color)) continue;
                pixels[offset] = 255;
                pixels[offset + 1] = 255;
                pixels[offset + 2] = 255;
                pixels[offset + 3] = 255;
            }
        }
    }

    private static bool MatchesAny(IReadOnlyList<Rgb> palette, Rgb color)
    {
        for (var index = 0; index < palette.Count; index++)
            if (ColorsMatch(palette[index], color, 12)) return true;
        return false;
    }

    private static Rgb ReadRgb(byte[] pixels, int rowBytes, int x, int y)
    {
        var offset = PixelOffset(rowBytes, x, y);
        return new Rgb(pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }

    private static int PixelOffset(int rowBytes, int x, int y) => y * rowBytes + x * 4;

    private static bool ColorsMatch(Rgb first, Rgb second, int tolerance) =>
        Math.Abs(first.Red - second.Red) <= tolerance &&
        Math.Abs(first.Green - second.Green) <= tolerance &&
        Math.Abs(first.Blue - second.Blue) <= tolerance;

    private static bool IsNearlyWhite(Rgb color) =>
        color.Red >= 245 && color.Green >= 245 && color.Blue >= 245;

    private readonly record struct Rgb(byte Red, byte Green, byte Blue);
}

internal static class RhinoThumbnailCaptureGate
{
    internal static readonly SemaphoreSlim Gate = new(1, 1);
}

using Rhino;
using Rhino.Display;
using RhinoLayoutFoundry.Extensibility;

namespace RhinoLayoutFoundry.Rhino;

internal static class RhinoModelInspectionCapture
{
    internal static async Task<AutomationCaptureResult> CaptureAsync(uint serial, AutomationCaptureRequest request, CancellationToken token)
    {
        await RhinoThumbnailCaptureGate.Gate.WaitAsync(token);
        try
        {
            var completion = new TaskCompletionSource<AutomationCaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Capture()
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    var document = RhinoDoc.FromRuntimeSerialNumber(serial) ?? throw new InvalidOperationException("Document closed.");
                    var views = document.Views.GetStandardRhinoViews();
                    var view = views.FirstOrDefault(v => v == document.Views.ActiveView) ?? views.FirstOrDefault()
                        ?? throw new InvalidOperationException("No model viewport is available.");
                    using var viewport = new RhinoViewport(view.ActiveViewport);
                    var size = new System.Drawing.Size(request.Width, request.Height);
                    viewport.Size = size;
                    // Use a legible inspection appearance without changing the user's wireframe colors/camera.
                    using var shaded = DisplayModeDescription.GetDisplayMode(DisplayModeDescription.ShadedId);
                    if (shaded is not null) viewport.DisplayMode = shaded;
                    viewport.ZoomExtents();
                    using var settings = new ViewCaptureSettings { Document = document, RasterMode = true,
                        DrawGrid = false, DrawAxis = false, DrawBackground = false };
                    settings.SetViewport(viewport);
                    settings.SetLayout(size, new System.Drawing.Rectangle(System.Drawing.Point.Empty, size));
                    using var bitmap = RhinoDocumentThumbnailProvider.CaptureToBitmap(settings, request.BackgroundArgb)
                        ?? throw new InvalidOperationException("Rhino returned no model capture.");
                    using var stream = new MemoryStream();
                    bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                    completion.SetResult(new AutomationCaptureResult(true, "image/png", stream.ToArray(), ""));
                }
                catch (Exception e) { completion.SetResult(AutomationCaptureResult.Failure(e.Message)); }
            }
            if (RhinoApp.InvokeRequired) RhinoApp.InvokeOnUiThread((Action)Capture); else Capture();
            return await completion.Task;
        }
        finally { RhinoThumbnailCaptureGate.Gate.Release(); }
    }
}

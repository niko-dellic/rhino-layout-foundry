using Rhino;
using Rhino.Display;
using RhinoLayoutFoundry.Extensibility;

namespace RhinoLayoutFoundry.Rhino;

internal static class RhinoModelInspectionCapture
{
    // One bounded capture, no retained document/view references. The capture gate serializes access.
    private static string? _lastState;
    private static byte[]? _lastImage;
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
                    var views = document.Views.GetStandardRhinoViews() ?? [];
                    var view = views.FirstOrDefault(v => v == document.Views.ActiveView) ?? views.FirstOrDefault()
                        ?? throw new InvalidOperationException("No model viewport is available.");
                    var size = new System.Drawing.Size(request.Width, request.Height);
                    var viewport = view.ActiveViewport ?? throw new InvalidOperationException("Model viewport is not ready; wait until Rhino finishes opening the document.");
                    string? state = null;
                    try
                    {
                    viewport.GetFrustum(out var left, out var right, out var bottom, out var top, out var near, out var far);
                    state = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        serial, request.Width, request.Height, request.BackgroundArgb,
                        viewport.Id, Camera = new[] { viewport.CameraLocation.X, viewport.CameraLocation.Y, viewport.CameraLocation.Z },
                        Target = new[] { viewport.CameraTarget.X, viewport.CameraTarget.Y, viewport.CameraTarget.Z },
                        Up = new[] { viewport.CameraUp.X, viewport.CameraUp.Y, viewport.CameraUp.Z }, viewport.Camera35mmLensLength, viewport.IsParallelProjection,
                        Size = viewport.Size.ToString(), Display = viewport.DisplayMode.Id,
                        Frustum = new[] { left, right, bottom, top, near, far },
                        DisplaySettings = DisplayState(viewport.DisplayMode.DisplayAttributes),
                        Objects = document.Objects.Select(o => new { o.Id, Geometry = o.Geometry.DataCRC(0), Attributes = o.Attributes.ToJSON(new global::Rhino.FileIO.SerializationOptions()), o.IsHidden, Selected = o.IsSelected(false) }).ToArray(),
                        Layers = document.Layers.Select(layer => layer.DataCRC(0)).ToArray(),
                        Materials = document.Materials.Select(material => material.ToJSON(new global::Rhino.FileIO.SerializationOptions())).ToArray(),
                    });
                    }
                    catch (Exception fingerprintError)
                    {
                        // Cache diagnostics must not prevent a valid native capture.
                        // An incomplete fingerprint must never reuse stale evidence.
                        _lastState = null;
                        _lastImage = null;
                        System.Diagnostics.Trace.TraceWarning("Foundry model capture fingerprint: {0}", fingerprintError);
                    }
                    if (state == _lastState && _lastImage is not null)
                    {
                        completion.SetResult(new AutomationCaptureResult(true, "image/png", _lastImage, "Reused unchanged model capture."));
                        return;
                    }
                    // Capture the view read-only. Output dimensions belong to capture settings,
                    // never RhinoViewport.Size: a copied live viewport can retain its window
                    // association. Do not reframe, change display modes, or activate the view.
                    // The source-view constructor also initializes native capture state that
                    // SetViewport on a detached viewport does not supply on macOS.
                    using var settings = new ViewCaptureSettings(view, size, 96) { Document = document, RasterMode = true,
                        DrawGrid = false, DrawAxis = false, DrawBackground = false };
                    using var bitmap = RhinoDocumentThumbnailProvider.CaptureToBitmap(settings, request.BackgroundArgb)
                        ?? throw new InvalidOperationException("Rhino returned no model capture.");
                    using var stream = new MemoryStream();
                    bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                    var bytes = stream.ToArray();
                    _lastState = bytes.Length <= 4 * 1024 * 1024 ? state : null;
                    _lastImage = _lastState is null ? null : bytes;
                    completion.SetResult(new AutomationCaptureResult(true, "image/png", bytes, ""));
                }
                catch (Exception e)
                {
                    System.Diagnostics.Trace.TraceError("Foundry model capture: {0}", e);
                    completion.SetResult(AutomationCaptureResult.Failure("Model capture failed: " + e.Message));
                }
            }
            if (RhinoApp.InvokeRequired) RhinoApp.InvokeOnUiThread((Action)Capture); else Capture();
            return await completion.Task;
        }
        finally { RhinoThumbnailCaptureGate.Gate.Release(); }
    }

    private static Dictionary<string, object?> DisplayState(DisplayPipelineAttributes attributes)
    {
        // Rhino exposes its display settings through ISerializable. Read the values only;
        // no formatter deserialization or executable payload is used.
#pragma warning disable SYSLIB0050
        var info = new System.Runtime.Serialization.SerializationInfo(typeof(DisplayPipelineAttributes), new System.Runtime.Serialization.FormatterConverter());
        attributes.GetObjectData(info, new System.Runtime.Serialization.StreamingContext());
#pragma warning restore SYSLIB0050
        var result = new Dictionary<string, object?>();
        foreach (System.Runtime.Serialization.SerializationEntry entry in info) result[entry.Name] = entry.Value;
        return result;
    }
}

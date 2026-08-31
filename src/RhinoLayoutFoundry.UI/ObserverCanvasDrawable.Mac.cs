#if FOUNDRY_MACOS_NATIVE_GESTURES
using AppKit;
using CoreGraphics;
using Eto.Drawing;
using Eto.Forms;
using Foundation;
using RhinoLayoutFoundry.Core.Observer;

namespace RhinoLayoutFoundry.UI;

internal sealed partial class ObserverCanvasDrawable
{
    private const double MagnificationSensitivity = 1;
    private NSView? _nativeCanvasView;
    private NSObject? _nativeScrollMonitor;
    private NSObject? _nativeMagnificationMonitor;

    partial void AttachNativeTrackpadInput()
    {
        DetachNativeTrackpadInput();
        _nativeCanvasView = MacOSHelpers.ToNative(this, false);
        if (_nativeCanvasView is null) return;

        _nativeScrollMonitor = NSEvent.AddLocalMonitorForEventsMatchingMask(
            NSEventMask.ScrollWheel,
            HandleNativeScroll);
        _nativeMagnificationMonitor = NSEvent.AddLocalMonitorForEventsMatchingMask(
            NSEventMask.EventMagnify,
            HandleNativeMagnification);
    }

    partial void DetachNativeTrackpadInput()
    {
        if (_nativeScrollMonitor is not null)
        {
            NSEvent.RemoveMonitor(_nativeScrollMonitor);
            _nativeScrollMonitor.Dispose();
            _nativeScrollMonitor = null;
        }

        if (_nativeMagnificationMonitor is not null)
        {
            NSEvent.RemoveMonitor(_nativeMagnificationMonitor);
            _nativeMagnificationMonitor.Dispose();
            _nativeMagnificationMonitor = null;
        }

        _nativeCanvasView = null;
    }

    private NSEvent HandleNativeScroll(NSEvent nativeEvent)
    {
        if (!nativeEvent.HasPreciseScrollingDeltas ||
            !TryGetCanvasPoint(nativeEvent, out var canvasPoint) ||
            IsCanvasOverlay(canvasPoint))
            return nativeEvent;

        // ScrollingDeltaX/Y already include the direction selected in macOS.
        // ObserverCamera.PanScreen expects an on-screen content translation,
        // which is the inverse of AppKit's system-mapped scroll delta.
        QueueCameraPan(
            -(double)nativeEvent.ScrollingDeltaX,
            -(double)nativeEvent.ScrollingDeltaY);
        return null!;
    }

    private NSEvent HandleNativeMagnification(NSEvent nativeEvent)
    {
        if (!TryGetCanvasPoint(nativeEvent, out var canvasPoint) ||
            IsCanvasOverlay(canvasPoint))
            return nativeEvent;

        var magnification = (double)nativeEvent.Magnification;
        if (Math.Abs(magnification) < double.Epsilon) return null!;
        QueueCameraZoom(
            Math.Exp(magnification * MagnificationSensitivity),
            new ObserverPoint(canvasPoint.X, canvasPoint.Y));
        return null!;
    }

    private bool TryGetCanvasPoint(NSEvent nativeEvent, out PointF canvasPoint)
    {
        canvasPoint = default;
        if (_nativeCanvasView is null ||
            nativeEvent.Window is null ||
            nativeEvent.Window != _nativeCanvasView.Window)
            return false;

        var nativePoint = _nativeCanvasView.ConvertPointFromView(nativeEvent.LocationInWindow, null);
        if (!_nativeCanvasView.Bounds.Contains(nativePoint)) return false;
        canvasPoint = ToCanvasPoint(nativePoint);
        return true;
    }

    private PointF ToCanvasPoint(CGPoint nativePoint)
    {
        if (_nativeCanvasView is null) return PointF.Empty;
        var y = _nativeCanvasView.IsFlipped
            ? nativePoint.Y
            : _nativeCanvasView.Bounds.Height - nativePoint.Y;
        return new PointF((float)nativePoint.X, (float)y);
    }
}
#else
namespace RhinoLayoutFoundry.UI;

internal sealed partial class ObserverCanvasDrawable
{
    partial void AttachNativeTrackpadInput()
    {
    }

    partial void DetachNativeTrackpadInput()
    {
    }
}
#endif

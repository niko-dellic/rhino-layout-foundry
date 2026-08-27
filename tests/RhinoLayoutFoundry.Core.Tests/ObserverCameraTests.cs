using RhinoLayoutFoundry.Core.Observer;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class ObserverCameraTests
{
    [Fact]
    public void WorldScreenTransformsRoundTripAtExtremeZooms()
    {
        var viewport = new ObserverSize(1200, 800);
        var world = new ObserverPoint(1523.25, -804.5);
        foreach (var zoom in new[] { ObserverCamera.MinimumZoom, 0.5, 1d, ObserverCamera.MaximumZoom })
        {
            var camera = new ObserverCamera(new ObserverPoint(250, -100), zoom);
            var restored = camera.ScreenToWorld(camera.WorldToScreen(world, viewport), viewport);
            Assert.True(Math.Abs(world.X - restored.X) < 1e-8);
            Assert.True(Math.Abs(world.Y - restored.Y) < 1e-8);
        }
    }

    [Fact]
    public void PointerCenteredZoomKeepsAnchorStable()
    {
        var viewport = new ObserverSize(900, 600);
        var anchor = new ObserverPoint(127, 441);
        var camera = new ObserverCamera(new ObserverPoint(100, 50), 0.75);
        var before = camera.ScreenToWorld(anchor, viewport);

        var zoomed = camera.ZoomAt(anchor, 1.8, viewport);

        var after = zoomed.ScreenToWorld(anchor, viewport);
        Assert.True(Math.Abs(before.X - after.X) < 1e-8);
        Assert.True(Math.Abs(before.Y - after.Y) < 1e-8);
    }

    [Fact]
    public void ReverseDirectionSelectionRectangleProducesNormalizedScreenBounds()
    {
        var camera = new ObserverCamera(new ObserverPoint(0, 0), 2);
        var screen = camera.WorldToScreen(
            new ObserverRect(100, 80, -60, -30),
            new ObserverSize(800, 600));

        Assert.True(screen.Width > 0);
        Assert.True(screen.Height > 0);
        Assert.Equal(120d, screen.Width);
        Assert.Equal(60d, screen.Height);
    }

    [Fact]
    public void ReverseDirectionSelectionRectangleUsesNormalizedContainment()
    {
        var selection = new ObserverRect(100, 100, -80, -60);

        Assert.True(selection.Contains(new ObserverRect(30, 50, 40, 30)));
        Assert.False(selection.Contains(new ObserverRect(10, 50, 40, 30)));
    }

    [Fact]
    public void FitContainsBoundsInsidePaddedViewport()
    {
        var bounds = new ObserverRect(100, 200, 1200, 700);
        var viewport = new ObserverSize(1000, 700);
        var camera = ObserverCamera.Fit(bounds, viewport, 50);
        var screen = camera.WorldToScreen(bounds, viewport);

        Assert.True(screen.Left >= 49.9);
        Assert.True(screen.Top >= 49.9);
        Assert.True(screen.Right <= 950.1);
        Assert.True(screen.Bottom <= 650.1);
    }
}

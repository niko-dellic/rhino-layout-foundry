using RhinoFoundry.UI.Primitives;

namespace RhinoLayoutFoundry.Core.Observer;

public readonly record struct ObserverPoint(double X, double Y)
{
    public static ObserverPoint operator +(ObserverPoint point, ObserverPoint delta) =>
        new(point.X + delta.X, point.Y + delta.Y);

    public static ObserverPoint operator -(ObserverPoint point, ObserverPoint delta) =>
        new(point.X - delta.X, point.Y - delta.Y);

    public static ObserverPoint operator *(ObserverPoint point, double scale) =>
        new(point.X * scale, point.Y * scale);
}

public readonly record struct ObserverSize(double Width, double Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public readonly record struct ObserverRect(double X, double Y, double Width, double Height)
{
    public double Left => Math.Min(X, X + Width);
    public double Top => Math.Min(Y, Y + Height);
    public double Right => Math.Max(X, X + Width);
    public double Bottom => Math.Max(Y, Y + Height);
    public ObserverPoint Center => new((Left + Right) / 2, (Top + Bottom) / 2);
    public bool IsEmpty => Width == 0 || Height == 0;

    public bool Contains(ObserverPoint point) =>
        point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;

    public bool Contains(ObserverRect other) =>
        other.Left >= Left && other.Right <= Right &&
        other.Top >= Top && other.Bottom <= Bottom;

    public bool Intersects(ObserverRect other) =>
        other.Right >= Left && other.Left <= Right &&
        other.Bottom >= Top && other.Top <= Bottom;

    public ObserverRect Inflate(double amount) => new(
        Left - amount,
        Top - amount,
        Right - Left + amount * 2,
        Bottom - Top + amount * 2);

    public ObserverRect Translate(ObserverPoint delta) =>
        new(X + delta.X, Y + delta.Y, Width, Height);

    public static ObserverRect Union(ObserverRect first, ObserverRect second)
    {
        if (first.IsEmpty) return second;
        if (second.IsEmpty) return first;
        var left = Math.Min(first.Left, second.Left);
        var top = Math.Min(first.Top, second.Top);
        var right = Math.Max(first.Right, second.Right);
        var bottom = Math.Max(first.Bottom, second.Bottom);
        return new ObserverRect(left, top, right - left, bottom - top);
    }
}

/// <summary>Retains Layout's public and persisted value shape while delegating camera math.</summary>
public sealed record ObserverCamera(ObserverPoint WorldCenter, double Zoom)
{
    public const double MinimumZoom = FoundryCamera.MinimumZoom;
    public const double MaximumZoom = FoundryCamera.MaximumZoom;
    public static ObserverCamera Default { get; } = FromShared(FoundryCamera.Default);
    public FoundryCamera ToShared() => new(new(WorldCenter.X, WorldCenter.Y), Zoom);
    public static ObserverCamera FromShared(FoundryCamera camera) => new(new(camera.WorldCenter.X, camera.WorldCenter.Y), camera.Zoom);
    private static FoundrySize Size(ObserverSize viewport) => new(viewport.Width, viewport.Height);
    private static ObserverRect Rect(FoundryRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
    public ObserverPoint WorldToScreen(ObserverPoint world, ObserverSize viewport)
    {
        var point = ToShared().WorldToScreen(new FoundryPoint(world.X, world.Y), Size(viewport));
        return new(point.X, point.Y);
    }
    public ObserverPoint ScreenToWorld(ObserverPoint screen, ObserverSize viewport)
    {
        var point = ToShared().ScreenToWorld(new(screen.X, screen.Y), Size(viewport));
        return new(point.X, point.Y);
    }
    public ObserverRect WorldToScreen(ObserverRect world, ObserverSize viewport) =>
        Rect(ToShared().WorldToScreen(new FoundryRect(world.X, world.Y, world.Width, world.Height), Size(viewport)));
    public ObserverRect VisibleWorld(ObserverSize viewport) => Rect(ToShared().VisibleWorld(Size(viewport)));
    public ObserverCamera PanScreen(double deltaX, double deltaY) => FromShared(ToShared().PanScreen(deltaX, deltaY));
    public ObserverCamera ZoomAt(ObserverPoint screenAnchor, double factor, ObserverSize viewport) =>
        FromShared(ToShared().ZoomAt(new(screenAnchor.X, screenAnchor.Y), factor, Size(viewport)));
    public static ObserverCamera Fit(ObserverRect bounds, ObserverSize viewport, double padding = 48) =>
        FromShared(FoundryCamera.Fit(new(bounds.X, bounds.Y, bounds.Width, bounds.Height), Size(viewport), padding));
}

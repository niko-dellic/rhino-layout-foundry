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

public sealed record ObserverCamera(
    ObserverPoint WorldCenter,
    double Zoom)
{
    public const double MinimumZoom = 0.05;
    public const double MaximumZoom = 16;

    public static ObserverCamera Default { get; } = new(new ObserverPoint(0, 0), 1);

    public ObserverPoint WorldToScreen(ObserverPoint world, ObserverSize viewport) => new(
        (world.X - WorldCenter.X) * Zoom + viewport.Width / 2,
        (world.Y - WorldCenter.Y) * Zoom + viewport.Height / 2);

    public ObserverPoint ScreenToWorld(ObserverPoint screen, ObserverSize viewport) => new(
        (screen.X - viewport.Width / 2) / Zoom + WorldCenter.X,
        (screen.Y - viewport.Height / 2) / Zoom + WorldCenter.Y);

    public ObserverRect WorldToScreen(ObserverRect world, ObserverSize viewport)
    {
        var topLeft = WorldToScreen(new ObserverPoint(world.Left, world.Top), viewport);
        return new ObserverRect(
            topLeft.X,
            topLeft.Y,
            (world.Right - world.Left) * Zoom,
            (world.Bottom - world.Top) * Zoom);
    }

    public ObserverRect VisibleWorld(ObserverSize viewport)
    {
        var topLeft = ScreenToWorld(new ObserverPoint(0, 0), viewport);
        var bottomRight = ScreenToWorld(new ObserverPoint(viewport.Width, viewport.Height), viewport);
        return new ObserverRect(
            topLeft.X,
            topLeft.Y,
            bottomRight.X - topLeft.X,
            bottomRight.Y - topLeft.Y);
    }

    public ObserverCamera PanScreen(double deltaX, double deltaY) => this with
    {
        WorldCenter = new ObserverPoint(
            WorldCenter.X - deltaX / Zoom,
            WorldCenter.Y - deltaY / Zoom),
    };

    public ObserverCamera ZoomAt(
        ObserverPoint screenAnchor,
        double factor,
        ObserverSize viewport)
    {
        var before = ScreenToWorld(screenAnchor, viewport);
        var nextZoom = Math.Clamp(Zoom * factor, MinimumZoom, MaximumZoom);
        var provisional = this with { Zoom = nextZoom };
        var after = provisional.ScreenToWorld(screenAnchor, viewport);
        return provisional with { WorldCenter = WorldCenter + (before - after) };
    }

    public static ObserverCamera Fit(ObserverRect bounds, ObserverSize viewport, double padding = 48)
    {
        if (bounds.IsEmpty || viewport.IsEmpty)
        {
            return Default;
        }

        var availableWidth = Math.Max(1, viewport.Width - padding * 2);
        var availableHeight = Math.Max(1, viewport.Height - padding * 2);
        var zoom = Math.Clamp(
            Math.Min(availableWidth / bounds.Width, availableHeight / bounds.Height),
            MinimumZoom,
            MaximumZoom);
        return new ObserverCamera(bounds.Center, zoom);
    }
}

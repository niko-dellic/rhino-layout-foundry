namespace RhinoLayoutFoundry.Core.Domain;

public enum FoundryViewProjection
{
    Parallel,
    Perspective,
}

public readonly record struct Point3Coordinates(double X, double Y, double Z)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);

    public double DistanceTo(Point3Coordinates other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        var dz = Z - other.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}

public readonly record struct Vector3Coordinates(double X, double Y, double Z)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    public double Dot(Vector3Coordinates other) => X * other.X + Y * other.Y + Z * other.Z;
}

public sealed record ModelBoundsSnapshot(
    Point3Coordinates Minimum,
    Point3Coordinates Maximum)
{
    public bool IsValid =>
        Minimum.IsFinite &&
        Maximum.IsFinite &&
        Maximum.X >= Minimum.X &&
        Maximum.Y >= Minimum.Y &&
        Maximum.Z >= Minimum.Z;
}

public sealed record NamedViewDefinition(
    string Name,
    Point3Coordinates CameraLocation,
    Point3Coordinates CameraTarget,
    Vector3Coordinates CameraUp,
    FoundryViewProjection Projection,
    double LensLength = 50,
    string SessionId = "");

public sealed record ClippingPlaneDefinition(
    string Name,
    Point3Coordinates Origin,
    Vector3Coordinates Normal,
    Vector3Coordinates XAxis,
    double Width,
    double Height,
    IReadOnlyList<Guid> ViewportIds,
    string SessionId = "");

public sealed record NamedViewSnapshot(
    string Name,
    Point3Coordinates CameraLocation,
    Point3Coordinates CameraTarget,
    Vector3Coordinates CameraUp,
    FoundryViewProjection Projection);

public sealed record ClippingPlaneSnapshot(
    Guid ObjectId,
    string Name,
    Point3Coordinates Origin,
    Vector3Coordinates Normal,
    double Width,
    double Height,
    IReadOnlyList<Guid> ViewportIds,
    string SessionId);

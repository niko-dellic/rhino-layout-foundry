namespace RhinoLayoutFoundry.Core.Domain;

/// <summary>Physical spacing for generated layouts, independent of model scale.</summary>
public sealed record LayoutSpacing(
    double PageEdge,
    double DetailGap,
    double TitleBlockGap,
    double TitleBlockEdge,
    string UnitSystem)
{
    public static LayoutSpacing Uniform(double value, string unitSystem) =>
        new(value, value, value, value, unitSystem);

    public static LayoutSpacing Default(string unitSystem)
    {
        var imperial = unitSystem is "Inches" or "Inch" or "Feet" or "Foot";
        return Uniform(imperial ? 6.35 : 10, "Millimeters").InUnits(unitSystem);
    }

    public LayoutSpacing InUnits(string unitSystem)
    {
        var scale = AdaptiveTitleBlockLayoutSolver.UnitsPerMillimeter(unitSystem) /
                    AdaptiveTitleBlockLayoutSolver.UnitsPerMillimeter(UnitSystem);
        return new(PageEdge * scale, DetailGap * scale, TitleBlockGap * scale,
            TitleBlockEdge * scale, unitSystem);
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => new[] { PageEdge, DetailGap, TitleBlockGap, TitleBlockEdge }
        .All(value => double.IsFinite(value) && value >= 0);
}

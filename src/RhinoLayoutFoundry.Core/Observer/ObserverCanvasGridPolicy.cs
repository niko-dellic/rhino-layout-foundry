namespace RhinoLayoutFoundry.Core.Observer;

public static class ObserverCanvasGridPolicy
{
    public const double BaseWorldSpacing = 40;
    public const double MinimumProjectedSpacingPixels = 28;

    public static double EffectiveWorldSpacing(double zoom)
    {
        if (!double.IsFinite(zoom) || zoom <= 0) return BaseWorldSpacing;
        var projectedBaseSpacing = BaseWorldSpacing * zoom;
        if (projectedBaseSpacing >= MinimumProjectedSpacingPixels)
            return BaseWorldSpacing;

        var requiredMultiplier = MinimumProjectedSpacingPixels / projectedBaseSpacing;
        var power = Math.Clamp((int)Math.Ceiling(Math.Log2(requiredMultiplier)), 0, 30);
        return BaseWorldSpacing * Math.Pow(2, power);
    }
}

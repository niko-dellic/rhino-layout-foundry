using RhinoFoundry.UI.Primitives;

namespace RhinoLayoutFoundry.Core.Observer;

public static class ObserverCanvasGridPolicy
{
    public const double BaseWorldSpacing = FoundryCanvasGridPolicy.BaseWorldSpacing;
    public const double MinimumProjectedSpacingPixels = FoundryCanvasGridPolicy.MinimumProjectedSpacingPixels;
    public static double EffectiveWorldSpacing(double zoom) => FoundryCanvasGridPolicy.EffectiveWorldSpacing(zoom);
}

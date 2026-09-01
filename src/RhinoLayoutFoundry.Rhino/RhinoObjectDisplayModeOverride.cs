using Rhino.Display;
using Rhino.DocObjects;

namespace RhinoLayoutFoundry.Rhino;

internal static class RhinoObjectDisplayModeOverride
{
    internal static bool TrySet(
        ObjectAttributes attributes,
        DisplayModeDescription mode,
        Guid viewportId)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(mode);

        // RhinoCommon can return false here even when it writes the override.
        // Validate the resulting attributes instead of trusting that return value.
        attributes.SetDisplayModeOverride(mode, viewportId);
        return attributes.HasDisplayModeOverride(viewportId) &&
               attributes.GetDisplayModeOverride(viewportId) == mode.Id;
    }
}

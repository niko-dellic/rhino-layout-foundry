using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Rhino;

internal sealed class RhinoMutationCapabilityProvider : IMutationCapabilityProvider
{
    public FoundryMutationCapabilities Capture()
    {
        return FoundryMutationCapabilities.Unavailable;
    }
}

namespace RhinoLayoutFoundry.UI;

internal static class SharedUiPlatform
{
    internal static void Initialize()
    {
#if FOUNDRY_SHARED_MACOS
        RhinoFoundry.UI.MacOS.FoundryMacOS.Initialize();
#endif
    }
}

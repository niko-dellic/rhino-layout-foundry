using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class MutationCapabilitiesTests
{
    [Fact]
    public void UnsupportedPageRenameCannotBePresentedAsUndoSafe()
    {
        var capabilities = FoundryMutationCapabilities.Unavailable;

        Assert.False(capabilities.PageRenameUndo.IsSupported);
        Assert.Equal(
            MutationCapabilityState.Unsupported,
            capabilities.PageRenameUndo.State);
        Assert.Contains(
            "Undo",
            capabilities.PageRenameUndo.Reason,
            StringComparison.Ordinal);
    }
}

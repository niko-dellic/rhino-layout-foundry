namespace RhinoLayoutFoundry.Core.Operations;

public enum MutationCapabilityState
{
    Supported,
    Unsupported,
    Unverified,
}

public sealed record MutationCapability(
    MutationCapabilityState State,
    string Reason)
{
    public bool IsSupported => State == MutationCapabilityState.Supported;
}

public sealed record FoundryMutationCapabilities(
    MutationCapability PageRenameUndo,
    MutationCapability AtomicBatchUndo)
{
    public static FoundryMutationCapabilities Unavailable { get; } = new(
        new MutationCapability(
            MutationCapabilityState.Unsupported,
            "Rhino page-name changes are not recorded by Rhino's document Undo stack."),
        new MutationCapability(
            MutationCapabilityState.Unverified,
            "Batch mutation remains gated until every included property has a supported Undo path."));
}

public interface IMutationCapabilityProvider
{
    FoundryMutationCapabilities Capture();
}

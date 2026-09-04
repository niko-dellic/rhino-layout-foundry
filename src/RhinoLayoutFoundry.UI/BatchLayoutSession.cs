using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

/// <summary>Dialog-owned draft data and asynchronous preview lifetime; never persisted.</summary>
internal sealed class BatchLayoutSession : IDisposable
{
    internal List<CreationDraft> Drafts { get; } = [];
    internal CancellationTokenSource NamedViewCancellation { get; } = new();
    internal CancellationTokenSource LayoutCancellation { get; } = new();
    internal LatestPreviewScheduler DraftPreview { get; } = new();
    internal LatestPreviewScheduler EditPreview { get; } = new();

    public void Dispose()
    {
        NamedViewCancellation.Cancel();
        LayoutCancellation.Cancel();
        NamedViewCancellation.Dispose();
        LayoutCancellation.Dispose();
    }
}

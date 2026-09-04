namespace RhinoLayoutFoundry.Core.Overview;

/// <summary>
/// UI-context-owned, single-flight preview scheduler. Coalesces queued changes;
/// renderers must check IsCurrent after awaits before presenting a result.
/// </summary>
public sealed class LatestPreviewScheduler
{
    private long _version;
    private bool _running;
    public bool IsCurrent(long version) => version == _version;

    public async Task RequestAsync(Func<long, Task> render, CancellationToken cancellationToken, Action<Exception> failed)
    {
        _version++;
        if (_running || cancellationToken.IsCancellationRequested) return;
        _running = true;
        try
        {
            long rendered;
            do
            {
                rendered = _version;
                try { await render(rendered); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
                catch (Exception exception) { if (!cancellationToken.IsCancellationRequested) failed(exception); }
            }
            while (rendered != _version && !cancellationToken.IsCancellationRequested);
        }
        finally { _running = false; }
    }
}

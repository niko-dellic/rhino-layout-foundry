namespace RhinoLayoutFoundry.Extensibility;

public static class FoundryAutomation
{
    private static readonly object SyncRoot = new();
    private static IFoundryAutomationHost? _current;

    public static IFoundryAutomationHost? Current
    {
        get
        {
            lock (SyncRoot) return _current;
        }
    }

    public static IDisposable Register(IFoundryAutomationHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        lock (SyncRoot)
        {
            if (_current is not null && !ReferenceEquals(_current, host))
                throw new InvalidOperationException("A Foundry automation host is already registered.");
            _current = host;
        }

        return new Registration(host);
    }

    private sealed class Registration(IFoundryAutomationHost host) : IDisposable
    {
        private IFoundryAutomationHost? _host = host;

        public void Dispose()
        {
            var registered = Interlocked.Exchange(ref _host, null);
            if (registered is null) return;
            lock (SyncRoot)
            {
                if (ReferenceEquals(_current, registered)) _current = null;
            }
        }
    }
}

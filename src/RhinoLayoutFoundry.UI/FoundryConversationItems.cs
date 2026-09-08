using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

/// <summary>Companion-owned conversation storage. Callbacks execute on the host UI thread.</summary>
public sealed record FoundryConversationProvider(
    Func<uint, IReadOnlyList<ConversationOverview>> List,
    Func<uint, Guid, string> Read,
    Func<uint, Guid, Guid, string, string, Guid> Write,
    Action<uint, Guid> Delete);

public static class FoundryConversationItems
{
    public static FoundryConversationProvider? Current { get; private set; }
    public static IDisposable Register(FoundryConversationProvider provider)
    {
        if (Current is not null) throw new InvalidOperationException("Conversation provider already registered.");
        Current = provider;
        return new Registration(provider);
    }
    private sealed class Registration(FoundryConversationProvider provider) : IDisposable
    {
        public void Dispose() { if (ReferenceEquals(Current, provider)) Current = null; }
    }
}

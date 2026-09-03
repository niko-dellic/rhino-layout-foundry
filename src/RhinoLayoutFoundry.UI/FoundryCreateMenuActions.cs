using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

/// <summary>
/// A separately distributed companion action that appears in Layout Foundry's
/// create menu without becoming part of the open-source plug-in.
/// </summary>
public sealed record FoundryCreateMenuAction(
    string Id,
    string Label,
    Func<Image> CreateIcon,
    Action<Control> Invoke);

public static class FoundryCreateMenuActions
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, FoundryCreateMenuAction> Registrations =
        new(StringComparer.Ordinal);

    internal static IReadOnlyList<FoundryCreateMenuAction> Snapshot()
    {
        lock (SyncRoot) return Registrations.Values.ToArray();
    }

    public static IDisposable Register(FoundryCreateMenuAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (string.IsNullOrWhiteSpace(action.Id))
            throw new ArgumentException("An action ID is required.", nameof(action));
        if (string.IsNullOrWhiteSpace(action.Label))
            throw new ArgumentException("An action label is required.", nameof(action));
        ArgumentNullException.ThrowIfNull(action.CreateIcon);
        ArgumentNullException.ThrowIfNull(action.Invoke);

        var normalized = action with { Id = action.Id.Trim(), Label = action.Label.Trim() };
        lock (SyncRoot)
        {
            if (!Registrations.TryAdd(normalized.Id, normalized))
                throw new InvalidOperationException(
                    $"A Foundry create-menu action called '{normalized.Id}' is already registered.");
        }

        return new Registration(normalized.Id);
    }

    public static bool TryInvoke(string actionId, Control owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (string.IsNullOrWhiteSpace(actionId)) return false;

        FoundryCreateMenuAction? action;
        lock (SyncRoot)
            Registrations.TryGetValue(actionId.Trim(), out action);
        if (action is null) return false;

        action.Invoke(owner);
        return true;
    }

    private sealed class Registration(string actionId) : IDisposable
    {
        private string? _actionId = actionId;

        public void Dispose()
        {
            var id = Interlocked.Exchange(ref _actionId, null);
            if (id is null) return;
            lock (SyncRoot) Registrations.Remove(id);
        }
    }
}

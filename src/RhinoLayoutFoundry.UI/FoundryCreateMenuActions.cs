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
    Action<Control, IReadOnlyDictionary<string, object?>> Invoke);

public static class FoundryCreateMenuActions
{
    private const string SharedStateKey =
        "RhinoLayoutFoundry.UI.FoundryCreateMenuActions.v2";
    private static readonly object SyncRoot = string.Intern(SharedStateKey);

    internal static IReadOnlyList<FoundryCreateMenuAction> Snapshot()
    {
        lock (SyncRoot)
        {
            return Registrations.Values
                .Select(entry => new FoundryCreateMenuAction(
                    (string)entry[0],
                    (string)entry[1],
                    (Func<Image>)entry[2],
                    (Action<Control, IReadOnlyDictionary<string, object?>>)entry[3]))
                .ToArray();
        }
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
            if (!Registrations.TryAdd(normalized.Id,
                    [normalized.Id, normalized.Label, normalized.CreateIcon, normalized.Invoke]))
                throw new InvalidOperationException(
                    $"A Foundry create-menu action called '{normalized.Id}' is already registered.");
        }

        return new Registration(normalized.Id);
    }

    public static bool TryInvoke(
        string actionId,
        Control owner,
        IReadOnlyDictionary<string, object?> context)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(actionId)) return false;

        Action<Control, IReadOnlyDictionary<string, object?>>? invoke = null;
        lock (SyncRoot)
        {
            if (Registrations.TryGetValue(actionId.Trim(), out var entry))
                invoke = (Action<Control, IReadOnlyDictionary<string, object?>>)entry[3];
        }
        if (invoke is null) return false;

        invoke(owner, context);
        return true;
    }

    private static Dictionary<string, object[]> Registrations
    {
        get
        {
            if (AppDomain.CurrentDomain.GetData(SharedStateKey) is
                Dictionary<string, object[]> registrations)
                return registrations;

            registrations = new Dictionary<string, object[]>(StringComparer.Ordinal);
            AppDomain.CurrentDomain.SetData(SharedStateKey, registrations);
            return registrations;
        }
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

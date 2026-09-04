namespace RhinoLayoutFoundry.Core.Operations;

/// <summary>
/// Owns compensating actions in acquisition order. Rollback attempts every action
/// in reverse order and returns failures; callers must not claim full recovery when any failed.
/// </summary>
public sealed class CompensationJournal
{
    private readonly Stack<(string Label, Action Restore)> _actions = new();

    public void Register(string label, Action restore)
    {
        ArgumentNullException.ThrowIfNull(restore);
        _actions.Push((label, restore));
    }

    public void Commit() => _actions.Clear();

    public IReadOnlyList<string> Rollback()
    {
        var failures = new List<string>();
        while (_actions.TryPop(out var action))
        {
            try { action.Restore(); }
            catch (Exception exception) { failures.Add($"{action.Label}: {exception.Message}"); }
        }
        return failures;
    }
}

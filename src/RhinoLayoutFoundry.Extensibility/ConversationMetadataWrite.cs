namespace RhinoLayoutFoundry.Extensibility;

/// <summary>
/// Marks synchronous conversation-store writes on the document's UI thread.
/// Only document-property notifications are presentation-only in this scope;
/// geometry, layer, view and command events must continue to invalidate plans.
/// Never wrap drawing mutations or asynchronous work in this scope.
/// </summary>
public static class ConversationMetadataWrite
{
    [ThreadStatic] private static uint? _document;

    public static bool IsActive(uint documentSerial) => _document == documentSerial;

    public static void Run(uint documentSerial, Action write)
    {
        ArgumentNullException.ThrowIfNull(write);
        var previous = _document;
        _document = documentSerial;
        try { write(); }
        finally { _document = previous; }
    }
}

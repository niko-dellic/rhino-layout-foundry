using Rhino;
using Rhino.Collections;
using Rhino.FileIO;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Persistence;

namespace RhinoLayoutFoundry.Rhino;

/// <summary>UI-thread-owned state. Reads never reconcile or mark the Rhino document modified.</summary>
internal sealed class DocumentStateStore
{
    private const string PayloadKey = "Payload";
    private const string SchemaVersionKey = "SchemaVersion";
    private readonly Dictionary<uint, Entry> _entries = new();

    private Entry Find(RhinoDoc document)
    {
        if (!_entries.TryGetValue(document.RuntimeSerialNumber, out var entry))
            _entries[document.RuntimeSerialNumber] = entry = new(
                new(DocumentStateLoadStatus.Loaded, DocumentState.Empty()), null);
        return entry;
    }

    public DocumentState Get(RhinoDoc document) => Find(document).Loaded.State;
    public string? Diagnostic(RhinoDoc document) => Find(document).Loaded.Diagnostic;
    public bool CanWrite(RhinoDoc document) => Find(document).Loaded.CanWrite;

    public void EnsureWritable(RhinoDoc document)
    {
        if (!CanWrite(document)) throw new InvalidOperationException(Diagnostic(document));
    }

    // Explicitly called at a mutation boundary, not during snapshots or archive writes.
    public DocumentState Reconcile(RhinoDoc document, DocumentState state) =>
        state.RemoveMissingReferences(document.Views.GetPageViews()
            .Select(page => page.MainViewport.Id).ToHashSet(), document.Views.GetPageViews().SelectMany(page => page.GetDetailViews()).Select(detail => detail.Viewport.Id).ToHashSet());

    public void Set(RhinoDoc document, DocumentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        EnsureWritable(document);
        DocumentStateSerializer.Validate(state);
        _entries[document.RuntimeSerialNumber] = new(
            new(DocumentStateLoadStatus.Loaded, state), null);
    }

    internal Action CaptureRestoreAction(RhinoDoc document)
    {
        var original = Find(document);
        return () => _entries[document.RuntimeSerialNumber] = original;
    }
    public void Remove(RhinoDoc document) => _entries.Remove(document.RuntimeSerialNumber);

    public void Write(RhinoDoc document, BinaryArchiveWriter archive)
    {
        var entry = Find(document);
        if (entry.OriginalEnvelope is { } original)
        {
            // Preserve old, unsupported, and invalid envelopes until an intentional successful mutation.
            archive.WriteDictionary(original);
            return;
        }
        if (!entry.Loaded.CanWrite)
            throw new InvalidOperationException("Foundry cannot safely save an unreadable metadata archive. Save a recovery copy of the original file.");
        var envelope = new ArchivableDictionary(1, "RhinoLayoutFoundry.DocumentState");
        envelope.Set(SchemaVersionKey, DocumentState.CurrentSchemaVersion);
        envelope.Set(PayloadKey, DocumentStateSerializer.Serialize(entry.Loaded.State));
        archive.WriteDictionary(envelope);
    }

    public void Read(RhinoDoc document, BinaryArchiveReader archive)
    {
        ArchivableDictionary? envelope = null;
        DocumentStateLoadResult loaded;
        try
        {
            envelope = archive.ReadDictionary();
            loaded = envelope is null ? DocumentStateLoadResult.Invalid("The archive envelope was unreadable.") : DocumentStateLoadResult.Read(
                envelope.ContainsKey(SchemaVersionKey) && envelope[SchemaVersionKey] is int version ? version : null,
                envelope.ContainsKey(PayloadKey) ? envelope[PayloadKey] as string : null);
        }
        // Archive and deserialization failures must fail closed, including Rhino BinaryArchiveException.
        catch (Exception exception)
        {
            loaded = DocumentStateLoadResult.Invalid(exception.Message);
        }
        _entries[document.RuntimeSerialNumber] = new(loaded, envelope);
        if (loaded.Diagnostic is { } diagnostic) RhinoApp.WriteLine(diagnostic);
    }

    private sealed record Entry(DocumentStateLoadResult Loaded, ArchivableDictionary? OriginalEnvelope);
}

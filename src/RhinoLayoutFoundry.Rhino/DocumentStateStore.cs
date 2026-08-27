using Rhino;
using Rhino.Collections;
using Rhino.FileIO;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Persistence;

namespace RhinoLayoutFoundry.Rhino;

internal sealed class DocumentStateStore
{
    private const string PayloadKey = "Payload";
    private const string SchemaVersionKey = "SchemaVersion";
    private readonly Dictionary<uint, DocumentState> _states = new();

    public DocumentState Get(RhinoDoc document)
    {
        if (_states.TryGetValue(document.RuntimeSerialNumber, out var state))
        {
            return state;
        }

        state = DocumentState.Empty();
        _states[document.RuntimeSerialNumber] = state;
        return state;
    }

    public void Set(RhinoDoc document, DocumentState state)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(state);
        _states[document.RuntimeSerialNumber] = state;
    }

    public void Remove(RhinoDoc document)
    {
        _states.Remove(document.RuntimeSerialNumber);
    }

    public void Write(RhinoDoc document, BinaryArchiveWriter archive)
    {
        var state = Get(document);
        var envelope = new ArchivableDictionary(1, "RhinoLayoutFoundry.DocumentState");
        envelope.Set(SchemaVersionKey, state.SchemaVersion);
        envelope.Set(PayloadKey, DocumentStateSerializer.Serialize(state));
        archive.WriteDictionary(envelope);
    }

    public void Read(RhinoDoc document, BinaryArchiveReader archive)
    {
        try
        {
            var envelope = archive.ReadDictionary();
            if (!envelope.ContainsKey(SchemaVersionKey) || !envelope.ContainsKey(PayloadKey))
            {
                _states[document.RuntimeSerialNumber] = DocumentState.Empty();
                return;
            }

            var schemaVersion = (int)envelope[SchemaVersionKey];
            var payload = envelope[PayloadKey] as string;
            if ((schemaVersion != 1 && schemaVersion != DocumentState.CurrentSchemaVersion) ||
                string.IsNullOrWhiteSpace(payload))
            {
                _states[document.RuntimeSerialNumber] = DocumentState.Empty();
                return;
            }

            _states[document.RuntimeSerialNumber] = DocumentStateSerializer.Deserialize(payload);
        }
        catch (Exception exception) when (
            exception is InvalidCastException or NotSupportedException or System.Text.Json.JsonException)
        {
            _states[document.RuntimeSerialNumber] = DocumentState.Empty();
            RhinoApp.WriteLine(
                "Rhino Layout Foundry could not read its document metadata and opened with empty metadata: {0}",
                exception.Message);
        }
    }
}

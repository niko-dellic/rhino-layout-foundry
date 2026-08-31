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
    private readonly Dictionary<uint, int> _writeSchemaVersions = new();

    public DocumentState Get(RhinoDoc document)
    {
        if (_states.TryGetValue(document.RuntimeSerialNumber, out var state))
        {
            return RemoveTemplatesForMissingSources(document, state);
        }

        state = DocumentState.Empty();
        _states[document.RuntimeSerialNumber] = state;
        _writeSchemaVersions[document.RuntimeSerialNumber] = DocumentState.CurrentSchemaVersion;
        return RemoveTemplatesForMissingSources(document, state);
    }

    private DocumentState RemoveTemplatesForMissingSources(RhinoDoc document, DocumentState state)
    {
        var pageViewIds = document.Views.GetPageViews()
            .Select(page => page.MainViewport.Id)
            .ToHashSet();
        var cleaned = state.RemoveTemplatesForMissingSources(pageViewIds);
        if (ReferenceEquals(cleaned, state)) return state;
        _states[document.RuntimeSerialNumber] = cleaned;
        document.Modified = true;
        return cleaned;
    }

    public void Set(RhinoDoc document, DocumentState state)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(state);
        var serial = document.RuntimeSerialNumber;
        if (_states.TryGetValue(serial, out var before) &&
            (!ObserverCanvasStateComparer.ContentEquals(before.Canvas, state.Canvas) ||
             !before.AppearanceRules.SequenceEqual(state.AppearanceRules) ||
             !before.TemplateRegistrations.SequenceEqual(state.TemplateRegistrations) ||
             !before.TemplateLinks.SequenceEqual(state.TemplateLinks)))
        {
            _writeSchemaVersions[serial] = DocumentState.CurrentSchemaVersion;
        }

        _states[serial] = state;
    }

    public void SetCurrentSchema(RhinoDoc document, DocumentState state)
    {
        Set(document, state with { SchemaVersion = DocumentState.CurrentSchemaVersion });
        _writeSchemaVersions[document.RuntimeSerialNumber] = DocumentState.CurrentSchemaVersion;
    }

    public void Remove(RhinoDoc document)
    {
        _states.Remove(document.RuntimeSerialNumber);
        _writeSchemaVersions.Remove(document.RuntimeSerialNumber);
    }

    public void Write(RhinoDoc document, BinaryArchiveWriter archive)
    {
        var state = Get(document);
        var writeVersion = _writeSchemaVersions.GetValueOrDefault(
            document.RuntimeSerialNumber,
            DocumentState.CurrentSchemaVersion);
        var persistedState = state with
        {
            SchemaVersion = writeVersion,
            ObserverCanvas = writeVersion >= 4 ? state.Canvas : null,
            ImportRecovery = writeVersion >= 5 ? state.Recovery : null,
            DedicatedDetailLayerId = writeVersion >= 6 ? state.DedicatedDetailLayerId : null,
            ProjectData = writeVersion >= 7 ? state.ProjectInfo : null,
            ViewportRuleSets = writeVersion >= 9 ? state.AppearanceRules : null,
            CapabilityTemplates = writeVersion >= 9 ? state.TemplateRegistrations : null,
            CapabilityLinks = writeVersion >= 9 ? state.TemplateLinks : null,
        };
        var envelope = new ArchivableDictionary(1, "RhinoLayoutFoundry.DocumentState");
        envelope.Set(SchemaVersionKey, writeVersion);
        envelope.Set(PayloadKey, DocumentStateSerializer.Serialize(persistedState));
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
                _writeSchemaVersions[document.RuntimeSerialNumber] = DocumentState.CurrentSchemaVersion;
                return;
            }

            var schemaVersion = (int)envelope[SchemaVersionKey];
            var payload = envelope[PayloadKey] as string;
            if ((schemaVersion is < 1 or > DocumentState.CurrentSchemaVersion) ||
                string.IsNullOrWhiteSpace(payload))
            {
                _states[document.RuntimeSerialNumber] = DocumentState.Empty();
                _writeSchemaVersions[document.RuntimeSerialNumber] = DocumentState.CurrentSchemaVersion;
                return;
            }

            _states[document.RuntimeSerialNumber] = DocumentStateSerializer.Deserialize(payload);
            _writeSchemaVersions[document.RuntimeSerialNumber] = schemaVersion;
        }
        catch (Exception exception) when (
            exception is InvalidCastException or NotSupportedException or System.Text.Json.JsonException)
        {
            _states[document.RuntimeSerialNumber] = DocumentState.Empty();
            _writeSchemaVersions[document.RuntimeSerialNumber] = DocumentState.CurrentSchemaVersion;
            RhinoApp.WriteLine(
                "Rhino Layout Foundry could not read its document metadata and opened with empty metadata: {0}",
                exception.Message);
        }
    }
}

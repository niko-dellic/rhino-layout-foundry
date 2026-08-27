using System.Text.Json;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Persistence;

public static class DocumentStateSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = false,
    };

    public static string Serialize(DocumentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return JsonSerializer.Serialize(state, Options);
    }

    public static DocumentState Deserialize(string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        var state = JsonSerializer.Deserialize<DocumentState>(payload, Options)
            ?? throw new JsonException("The document state payload was empty.");

        if (state.SchemaVersion is 1 or 2 or 3)
        {
            return state with
            {
                SchemaVersion = DocumentState.CurrentSchemaVersion,
                SheetTemplates = state.SchemaVersion == 1 ? [] : state.Templates,
                Sheets = state.SchemaVersion <= 2
                    ? state.Sheets.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value with { IncludeInPrintAll = true })
                    : state.Sheets,
                ObserverCanvas = ObserverCanvasState.Empty,
            };
        }

        if (state.SchemaVersion != DocumentState.CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Document state schema {state.SchemaVersion} is not supported; expected {DocumentState.CurrentSchemaVersion}.");
        }

        return state with { ObserverCanvas = state.Canvas };
    }
}

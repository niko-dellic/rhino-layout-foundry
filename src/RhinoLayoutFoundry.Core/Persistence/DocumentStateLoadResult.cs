using System.Text.Json;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Persistence;

public enum DocumentStateLoadStatus { Loaded, Unsupported, Invalid }

/// <summary>
/// A failed read is a protected document, never an empty writable document.
/// The host retains the original archive envelope for lossless pass-through.
/// </summary>
public sealed record DocumentStateLoadResult(
    DocumentStateLoadStatus Status, DocumentState State, string? Diagnostic = null)
{
    public bool CanWrite => Status == DocumentStateLoadStatus.Loaded;

    public static DocumentStateLoadResult Read(int? envelopeVersion, string? payload)
    {
        if (envelopeVersion is null || string.IsNullOrWhiteSpace(payload))
            return Invalid("The metadata envelope is incomplete.");
        try
        {
            using var json = JsonDocument.Parse(payload);
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty("SchemaVersion", out var version) ||
                version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var payloadVersion) || payloadVersion != envelopeVersion)
                return Invalid("The metadata envelope and payload schema versions do not match.");
            if (payloadVersion != DocumentState.CurrentSchemaVersion)
                return new(DocumentStateLoadStatus.Unsupported, DocumentState.Empty(),
                    $"Foundry metadata schema {payloadVersion} is unsupported. Foundry changes are disabled; original metadata is preserved on save.");
            return new(DocumentStateLoadStatus.Loaded, DocumentStateSerializer.Deserialize(payload));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            return Invalid(exception.Message);
        }
    }

    public static DocumentStateLoadResult Invalid(string reason) => new(
        DocumentStateLoadStatus.Invalid, DocumentState.Empty(),
        $"Foundry metadata could not be loaded: {reason} Foundry changes are disabled; recoverable original metadata is preserved on save.");
}

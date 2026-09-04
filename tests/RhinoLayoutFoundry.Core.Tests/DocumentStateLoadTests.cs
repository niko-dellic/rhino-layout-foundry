using System.Text.Json.Nodes;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Persistence;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class DocumentStateLoadTests
{
    [Fact]
    public void CurrentMetadataLoadsWritable()
    {
        var result = DocumentStateLoadResult.Read(DocumentState.CurrentSchemaVersion,
            DocumentStateSerializer.Serialize(DocumentState.Empty()));
        Assert.True(result.CanWrite);
        Assert.Null(result.Diagnostic);
    }

    [Fact]
    public void UnsupportedPayloadRemainsProtected()
    {
        var json = JsonNode.Parse(DocumentStateSerializer.Serialize(DocumentState.Empty()))!;
        json["SchemaVersion"] = 99;
        var result = DocumentStateLoadResult.Read(99, json.ToJsonString());
        Assert.Equal(DocumentStateLoadStatus.Unsupported, result.Status);
        Assert.False(result.CanWrite);
    }

    [Fact]
    public void InvalidEnvelopesNeverBecomeWritableEmptyState()
    {
        var payload = DocumentStateSerializer.Serialize(DocumentState.Empty());
        foreach (var result in new[] {
            DocumentStateLoadResult.Read(null, payload),
            DocumentStateLoadResult.Read(1, payload),
            DocumentStateLoadResult.Read(15, "{"),
            DocumentStateLoadResult.Read(15, null),
            DocumentStateLoadResult.Read(15, "null") })
        {
            Assert.Equal(DocumentStateLoadStatus.Invalid, result.Status);
            Assert.False(result.CanWrite);
            Assert.NotNull(result.Diagnostic);
        }
    }

    [Fact]
    public void NullCollectionsAndEntriesAreRejectedBeforeNormalization()
    {
        foreach (var name in new[] { "Folders", "Sheets", "DisplayRules", "Metadata" })
        {
            var json = JsonNode.Parse(DocumentStateSerializer.Serialize(DocumentState.Empty()))!;
            json[name] = null;
            Assert.False(DocumentStateLoadResult.Read(15, json.ToJsonString()).CanWrite);
        }
        var entries = JsonNode.Parse(DocumentStateSerializer.Serialize(DocumentState.Empty()))!;
        entries["Folders"] = new JsonArray((JsonNode?)null);
        Assert.False(DocumentStateLoadResult.Read(15, entries.ToJsonString()).CanWrite);
    }

    [Fact]
    public void CyclicFoldersAreRejected()
    {
        var state = DocumentState.Empty();
        state = state with { Folders = [state.Folders[0] with { ParentId = state.RootFolderId }] };
        Assert.False(DocumentStateLoadResult.Read(15, DocumentStateSerializer.Serialize(state)).CanWrite);
    }
}

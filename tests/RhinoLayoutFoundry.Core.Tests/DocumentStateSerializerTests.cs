using System.Text.Json;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Persistence;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class DocumentStateSerializerTests
{
    [Fact]
    public void PopulatedStateRoundTrips()
    {
        var folder = new FolderRecord(Guid.NewGuid(), WellKnownIds.UnorganizedFolderId, "Plans", 1);
        var sheetId = Guid.NewGuid();
        var titleBlock = new TitleBlockRole(Guid.NewGuid(), Guid.NewGuid(), "LowerRight");
        var sheet = new SheetRecord(
            sheetId,
            folder.Id,
            2,
            ["Issue A", "Permit"],
            new Dictionary<string, string> { ["discipline"] = "A" },
            titleBlock,
            IncludeInPrintAll: false);
        var rule = new DisplayRule(
            Guid.NewGuid(),
            "Plans shaded",
            true,
            10,
            [Guid.NewGuid()],
            [new HierarchySelector(HierarchySelectorKind.Folder, folder.Id)],
            Guid.NewGuid());
        var state = new DocumentState(
            DocumentState.CurrentSchemaVersion,
            WellKnownIds.UnorganizedFolderId,
            [
                new FolderRecord(WellKnownIds.UnorganizedFolderId, null, "Unorganized", 0),
                folder,
            ],
            new Dictionary<Guid, SheetRecord> { [sheetId] = sheet },
            [rule],
            new Dictionary<string, string> { ["project"] = "Foundry" },
            [new SheetTemplateRecipe(
                Guid.NewGuid(),
                SheetTemplateRecipe.CurrentRecipeVersion,
                "A3 plan",
                new PaperRecipe(420, 297, "Millimeters"),
                [new DetailSlotRecipe(Guid.NewGuid(), "Plan", 10, 10, 410, 270,
                    "Top", 0.02, true, null, "Level 1")],
                null,
                ["Permit"],
                new Dictionary<string, string> { ["discipline"] = "A" },
                "A-{index:000}")]);

        var restored = DocumentStateSerializer.Deserialize(DocumentStateSerializer.Serialize(state));

        Assert.Equal(DocumentState.CurrentSchemaVersion, restored.SchemaVersion);
        Assert.Equal(state.RootFolderId, restored.RootFolderId);
        Assert.Equal(2, restored.Folders.Count);
        Assert.Equal("Plans", restored.Folders.Single(item => item.Id == folder.Id).Name);
        Assert.Equal("A", restored.Sheets[sheetId].Metadata["discipline"]);
        Assert.Contains("Permit", restored.Sheets[sheetId].Tags);
        Assert.Equal(titleBlock.InstanceObjectId, restored.Sheets[sheetId].TitleBlock!.InstanceObjectId);
        Assert.False(restored.Sheets[sheetId].IncludeInPrintAll);
        Assert.Equal(rule.DisplayModeId, restored.DisplayRules.Single().DisplayModeId);
        Assert.Equal("Foundry", restored.Metadata["project"]);
        Assert.Equal("A3 plan", restored.Templates.Single().Name);
    }

    [Fact]
    public void VersionOneStateMigratesWithAnEmptyTemplateLibrary()
    {
        var payload = DocumentStateSerializer.Serialize(DocumentState.Empty())
            .Replace("\"SchemaVersion\":3", "\"SchemaVersion\":1", StringComparison.Ordinal)
            .Replace(",\"SheetTemplates\":[]", string.Empty, StringComparison.Ordinal);

        var restored = DocumentStateSerializer.Deserialize(payload);

        Assert.Equal(DocumentState.CurrentSchemaVersion, restored.SchemaVersion);
        Assert.Empty(restored.Templates);
    }

    [Fact]
    public void VersionTwoStateMigratesExistingSheetsAsIncludedInPrintAll()
    {
        var sheetId = Guid.NewGuid();
        var state = DocumentState.Empty() with
        {
            Sheets = new Dictionary<Guid, SheetRecord>
            {
                [sheetId] = new(sheetId, WellKnownIds.UnorganizedFolderId, 0, [],
                    new Dictionary<string, string>(), null),
            },
        };
        var payload = DocumentStateSerializer.Serialize(state)
            .Replace("\"SchemaVersion\":3", "\"SchemaVersion\":2", StringComparison.Ordinal)
            .Replace(",\"IncludeInPrintAll\":true", string.Empty, StringComparison.Ordinal);

        var restored = DocumentStateSerializer.Deserialize(payload);

        Assert.Equal(DocumentState.CurrentSchemaVersion, restored.SchemaVersion);
        Assert.True(restored.Sheets[sheetId].IncludeInPrintAll);
    }

    [Fact]
    public void UnsupportedSchemaIsRejected()
    {
        var payload = DocumentStateSerializer.Serialize(DocumentState.Empty())
            .Replace("\"SchemaVersion\":3", "\"SchemaVersion\":99", StringComparison.Ordinal);

        Assert.Throws<NotSupportedException>(() => DocumentStateSerializer.Deserialize(payload));
    }

    [Fact]
    public void InvalidJsonIsRejected()
    {
        Assert.Throws<JsonException>(() => DocumentStateSerializer.Deserialize("{not-json}"));
    }
}

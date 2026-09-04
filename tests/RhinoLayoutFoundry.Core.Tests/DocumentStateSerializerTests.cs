using System.Text.Json;
using System.Text.Json.Nodes;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Persistence;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class DocumentStateSerializerTests
{
    [Fact]
    public void CurrentStateRoundTripsWithLiveRegistrationAndManagedTitleBlock()
    {
        var state = DocumentState.Empty();
        var sheetId = Guid.NewGuid();
        var detailId = Guid.NewGuid();
        var appearanceId = Guid.NewGuid();
        var scope = new HierarchyScope(HierarchyScopeKind.Sheet, sheetId);
        state = state with
        {
            Sheets = new Dictionary<Guid, SheetRecord>
            {
                [sheetId] = new(
            sheetId,
state.RootFolderId,
0,
            new Dictionary<string, string> { ["discipline"] = "A" },
new TitleBlockRole(InstanceObjectId: Guid.NewGuid(),
    InstanceDefinitionId: Guid.NewGuid(),
    BuiltInKind: BuiltInTitleBlockKind.RightSidebar),
    TitleBlockData: new SheetTitleBlockData("A-01", [new("P1", "2026-09-04", "Issue", "ND", "QA")]), NamingBinding: new SheetNamingBinding("{folder}-{index}", 1, "Unorganized-1"))
                {
                    DetailNamedViews = new Dictionary<Guid, string>
                    {
                        [detailId] = "Plan"
                    }
                },
            },
            TemplateRegistrations = [new (
            Guid.NewGuid(),
scope)],
            AppearanceStates = [new(appearanceId, state.RootFolderId, 0, "Linework", [], [])],
            StateAssignments = [
                new (
                Guid.NewGuid(),
scope, appearanceId)],
            Recovery = [new("import", "fixture", "Recovered")],
            ProjectInfo = ProjectInformation.Empty with
            {
                ProjectName = "Foundry"
            },
        };
        var payload = DocumentStateSerializer.Serialize(state);
        var restored = DocumentStateSerializer.Deserialize(payload);
        Assert.Equal(16, restored.SchemaVersion);
        Assert.Equal(payload, DocumentStateSerializer.Serialize(restored));
        Assert.Equal("Plan", restored.Sheets[sheetId].DetailNamedViews[detailId]);
        Assert.Equal(BuiltInTitleBlockKind.RightSidebar, restored.Sheets[sheetId].TitleBlock!.BuiltInKind);
        Assert.Single(restored.Sheets[sheetId].TitleBlockData!.Revisions);
        Assert.DoesNotContain("Tags", payload);
        Assert.DoesNotContain("Templates", payload);
        Assert.False(JsonNode.Parse(payload)!.AsObject().ContainsKey("DisplayRules"));
    }

    [Theory]
    [InlineData("Folders")]
    [InlineData("Sheets")]
    [InlineData("Metadata")]
    [InlineData("Canvas")]
    [InlineData("Recovery")]
    [InlineData("ProjectInfo")]
    [InlineData("AppearanceRules")]
    [InlineData("TemplateRegistrations")]
    [InlineData("AppearanceStates")]
    [InlineData("StateAssignments")]
    public void RequiredPropertiesRejectMissingAndNull(string property)
    {
        var json = JsonNode.Parse(DocumentStateSerializer.Serialize(DocumentState.Empty()))!.AsObject();
        json.Remove(property);
        Assert.Equal(DocumentStateLoadStatus.Invalid, DocumentStateLoadResult.Read(16, json.ToJsonString()).Status);
        json[property] = null;
        var loaded = DocumentStateLoadResult.Read(16, json.ToJsonString());
        Assert.Equal(DocumentStateLoadStatus.Invalid, loaded.Status);
        Assert.False(loaded.CanWrite);
    }

    [Theory]
    [InlineData("Canvas", "StatePlacements")]
    [InlineData("Canvas", "FolderOrigins")]
    [InlineData("ProjectInfo", "CustomFields")]
    [InlineData("ProjectInfo", "ContentOptions")]
    public void RequiredNestedPropertiesRejectMissingAndNull(string parent, string property)
    {
        var json = JsonNode.Parse(DocumentStateSerializer.Serialize(DocumentState.Empty()))!.AsObject();
        json[parent]!.AsObject().Remove(property);
        Assert.Equal(DocumentStateLoadStatus.Invalid, DocumentStateLoadResult.Read(16, json.ToJsonString()).Status);
        json[parent]![property] = null;
        Assert.Equal(DocumentStateLoadStatus.Invalid, DocumentStateLoadResult.Read(16, json.ToJsonString()).Status);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(17)]
    public void OtherSchemasAreUnsupportedWithoutConversion(int version)
    {
        var payload = $$"""{"SchemaVersion":{{version}}}""";
        var result = DocumentStateLoadResult.Read(version, payload);
        Assert.Equal(DocumentStateLoadStatus.Unsupported, result.Status);
        Assert.False(result.CanWrite);
    }

    [Fact]
    public void UnknownFieldsAreInvalidRatherThanSilentlyDiscarded()
    {
        var json = JsonNode.Parse(DocumentStateSerializer.Serialize(DocumentState.Empty()))!;
        json["Templates"] = new JsonArray();
        Assert.Equal(DocumentStateLoadStatus.Invalid, DocumentStateLoadResult.Read(16, json.ToJsonString()).Status);
    }

    [Theory]
    [InlineData("orphan")]
    [InlineData("cycle")]
    [InlineData("duplicate")]
    public void InvalidHierarchyIsRejected(string kind)
    {
        var state = DocumentState.Empty();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        state = state with
        {
            Folders = kind switch
            {
                "orphan" => [..state.Folders,
                new (a, b, "Orphan", 1)],
                "cycle" => [..state.Folders, new(a, b, "A", 1),
new(b, a, "B", 2)],
                _ => [.. state.Folders, state.Folders[0]],
            }
        };
        Assert.Throws<JsonException>(() => DocumentStateSerializer.Serialize(state));
    }

    [Fact]
    public void ReconciliationRemovesDeletedSourcesWithoutMutatingReadState()
    {
        var pageId = Guid.NewGuid();
        var detailId = Guid.NewGuid();
        var state = DocumentState.Empty() with
        {
            TemplateRegistrations = [new(Guid.NewGuid(), new(HierarchyScopeKind.Sheet, pageId)), new(Guid.NewGuid(), new(HierarchyScopeKind.Detail, detailId))],
        };

        var restored = DocumentStateSerializer.Deserialize(DocumentStateSerializer.Serialize(state));

        Assert.Equal(2, restored.TemplateRegistrations.Count);
        var reconciled = restored.RemoveMissingReferences(new HashSet<Guid> { pageId }, new HashSet<Guid>());
        Assert.Equal(pageId, Assert.Single(reconciled.TemplateRegistrations).Source.Id);
        Assert.Equal(2, restored.TemplateRegistrations.Count);
        Assert.Empty(reconciled.RemoveMissingReferences(new HashSet<Guid>(), new HashSet<Guid>())
            .TemplateRegistrations);
    }
}

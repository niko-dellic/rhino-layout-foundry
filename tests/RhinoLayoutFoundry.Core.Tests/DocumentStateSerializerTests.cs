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
        var detailLayerId = Guid.NewGuid();
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
                "A-{index:000}")
            {
                SourcePageViewId = sheetId,
            }],
            new ObserverCanvasState(
                1,
                new Dictionary<Guid, ObserverPointRecord>
                {
                    [folder.Id] = new(12.5, -4),
                },
                new Dictionary<Guid, ObserverPointRecord>
                {
                    [sheetId] = new(240, 80),
                }),
            DedicatedDetailLayerId: detailLayerId,
            ProjectData: ProjectInformation.Empty with
            {
                ProjectName = "Civic Library",
                FirmName = "Foundry Architects",
                DefaultRevision = new SheetRevisionRecord("P01", "2026-08-28", "Permit", "ND", "QA"),
                TitleBlockOptions = new TitleBlockContentOptions(
                    [TitleBlockContentField.ProjectName], [], true),
            });

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
        Assert.Equal(sheetId, restored.Templates.Single().SourcePageViewId);
        Assert.Equal(new ObserverPointRecord(12.5, -4), restored.Canvas.FolderOrigins[folder.Id]);
        Assert.Equal(new ObserverPointRecord(240, 80), restored.Canvas.SheetPlacements[sheetId]);
        Assert.Equal(detailLayerId, restored.DedicatedDetailLayerId);
        Assert.Equal("Civic Library", restored.ProjectInfo.ProjectName);
        Assert.Equal("P01", restored.ProjectInfo.DefaultRevision!.Code);
        Assert.True(restored.ProjectInfo.ContentOptions.ReserveRevisionArea);
        Assert.True(restored.ProjectInfo.ContentOptions.Includes(TitleBlockContentField.ProjectName));
    }

    [Fact]
    public void VersionOneStateMigratesWithAnEmptyTemplateLibrary()
    {
        var payload = DocumentStateSerializer.Serialize(DocumentState.Empty())
            .Replace($"\"SchemaVersion\":{DocumentState.CurrentSchemaVersion}", "\"SchemaVersion\":1", StringComparison.Ordinal)
            .Replace(",\"SheetTemplates\":[]", string.Empty, StringComparison.Ordinal)
            .Replace(",\"ObserverCanvas\":{\"LayoutAlgorithmVersion\":1,\"FolderOrigins\":{},\"SheetPlacements\":{}}", string.Empty, StringComparison.Ordinal);

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
            .Replace($"\"SchemaVersion\":{DocumentState.CurrentSchemaVersion}", "\"SchemaVersion\":2", StringComparison.Ordinal)
            .Replace(",\"IncludeInPrintAll\":true", string.Empty, StringComparison.Ordinal);

        var restored = DocumentStateSerializer.Deserialize(payload);

        Assert.Equal(DocumentState.CurrentSchemaVersion, restored.SchemaVersion);
        Assert.True(restored.Sheets[sheetId].IncludeInPrintAll);
    }

    [Fact]
    public void VersionThreeStateMigratesWithEmptyObserverBoard()
    {
        var payload = DocumentStateSerializer.Serialize(DocumentState.Empty() with
            {
                ObserverCanvas = new ObserverCanvasState(
                    1,
                    new Dictionary<Guid, ObserverPointRecord> { [Guid.NewGuid()] = new(1, 2) },
                    new Dictionary<Guid, ObserverPointRecord>()),
            })
            .Replace($"\"SchemaVersion\":{DocumentState.CurrentSchemaVersion}", "\"SchemaVersion\":3", StringComparison.Ordinal)
            .Replace(",\"ObserverCanvas\":{\"LayoutAlgorithmVersion\":1,\"FolderOrigins\":{", ",\"ObserverCanvas\":{\"LayoutAlgorithmVersion\":1,\"FolderOrigins\":{", StringComparison.Ordinal);

        var restored = DocumentStateSerializer.Deserialize(payload);

        Assert.Equal(DocumentState.CurrentSchemaVersion, restored.SchemaVersion);
        Assert.Empty(restored.Canvas.FolderOrigins);
        Assert.Empty(restored.Canvas.SheetPlacements);
    }

    [Fact]
    public void VersionFourStateMigratesWithEmptyImportRecovery()
    {
        var payload = DocumentStateSerializer.Serialize(DocumentState.Empty() with
            {
                ImportRecovery =
                [
                    new ImportRecoveryRecord("layer", "A-Wall", "Missing source layer"),
                ],
            })
            .Replace($"\"SchemaVersion\":{DocumentState.CurrentSchemaVersion}", "\"SchemaVersion\":4", StringComparison.Ordinal)
            .Replace(",\"ImportRecovery\":[{\"Kind\":\"layer\",\"Name\":\"A-Wall\",\"Message\":\"Missing source layer\",\"EntityId\":null,\"Data\":null}]", string.Empty, StringComparison.Ordinal);

        var restored = DocumentStateSerializer.Deserialize(payload);

        Assert.Equal(DocumentState.CurrentSchemaVersion, restored.SchemaVersion);
        Assert.Empty(restored.Recovery);
    }

    [Fact]
    public void VersionFiveStateMigratesWithoutADedicatedDetailLayer()
    {
        var detailLayerId = Guid.NewGuid();
        var payload = DocumentStateSerializer.Serialize(DocumentState.Empty() with
            {
                DedicatedDetailLayerId = detailLayerId,
                ImportRecovery = [new ImportRecoveryRecord("layer", "A-Wall", "Missing source layer")],
            })
            .Replace($"\"SchemaVersion\":{DocumentState.CurrentSchemaVersion}", "\"SchemaVersion\":5", StringComparison.Ordinal)
            .Replace($",\"DedicatedDetailLayerId\":\"{detailLayerId}\"", string.Empty, StringComparison.Ordinal);

        var restored = DocumentStateSerializer.Deserialize(payload);

        Assert.Equal(DocumentState.CurrentSchemaVersion, restored.SchemaVersion);
        Assert.Null(restored.DedicatedDetailLayerId);
        Assert.Single(restored.Recovery);
    }

    [Fact]
    public void VersionSixStateMigratesWithEmptyProjectInformation()
    {
        var detailLayerId = Guid.NewGuid();
        var payload = DocumentStateSerializer.Serialize(DocumentState.Empty() with
            {
                DedicatedDetailLayerId = detailLayerId,
                ProjectData = ProjectInformation.Empty with { ProjectName = "Not in schema six" },
            })
            .Replace($"\"SchemaVersion\":{DocumentState.CurrentSchemaVersion}", "\"SchemaVersion\":6", StringComparison.Ordinal)
            .Replace(",\"ProjectData\":{\"ProjectName\":\"Not in schema six\",\"ProjectNumber\":\"\",\"ClientName\":\"\",\"SiteAddress\":\"\",\"ProjectPhase\":\"\",\"ProjectStatus\":\"\",\"FirmName\":\"\",\"FirmAddress\":\"\",\"FirmPhone\":\"\",\"FirmEmail\":\"\",\"FirmWebsite\":\"\",\"FirmRegistration\":\"\",\"IssueDate\":\"\",\"IssuePurpose\":\"\",\"DrawnBy\":\"\",\"CheckedBy\":\"\",\"ApprovedBy\":\"\",\"CustomFields\":{},\"Logo\":null,\"DefaultRevision\":null,\"TitleBlockOptions\":null}", string.Empty, StringComparison.Ordinal);

        var restored = DocumentStateSerializer.Deserialize(payload);

        Assert.Equal(detailLayerId, restored.DedicatedDetailLayerId);
        Assert.Equal(string.Empty, restored.ProjectInfo.ProjectName);
    }

    [Fact]
    public void VersionSevenProjectInformationGainsConventionalOptionsAndOrderedCustomFields()
    {
        var state = DocumentState.Empty() with
        {
            ProjectData = ProjectInformation.Empty with
            {
                ProjectName = "Existing project",
                CustomFields = new Dictionary<string, string>
                {
                    ["Consultant"] = "Acme",
                    ["Owner"] = "City",
                },
            },
        };
        var payload = DocumentStateSerializer.Serialize(state)
            .Replace($"\"SchemaVersion\":{DocumentState.CurrentSchemaVersion}", "\"SchemaVersion\":7",
                StringComparison.Ordinal)
            .Replace(",\"TitleBlockOptions\":null", string.Empty, StringComparison.Ordinal);

        var restored = DocumentStateSerializer.Deserialize(payload);

        Assert.Equal(DocumentState.CurrentSchemaVersion, restored.SchemaVersion);
        Assert.Equal("Existing project", restored.ProjectInfo.ProjectName);
        Assert.True(restored.ProjectInfo.ContentOptions.Includes(TitleBlockContentField.ProjectName));
        Assert.Equal(["Consultant", "Owner"],
            restored.ProjectInfo.ContentOptions.CustomFields.Select(field => field.Label).ToArray());
        Assert.All(restored.ProjectInfo.ContentOptions.CustomFields, field => Assert.True(field.IsIncluded));
    }

    [Fact]
    public void ObserverPayloadNeverContainsCameraOrTransientInteractionState()
    {
        var payload = DocumentStateSerializer.Serialize(DocumentState.Empty() with
        {
            ObserverCanvas = new ObserverCanvasState(
                1,
                new Dictionary<Guid, ObserverPointRecord> { [Guid.NewGuid()] = new(4, 8) },
                new Dictionary<Guid, ObserverPointRecord>()),
        });

        Assert.False(payload.Contains("Camera", StringComparison.OrdinalIgnoreCase));
        Assert.False(payload.Contains("Selection", StringComparison.OrdinalIgnoreCase));
        Assert.False(payload.Contains("Bitmap", StringComparison.OrdinalIgnoreCase));
        Assert.False(payload.Contains("Hover", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnsupportedSchemaIsRejected()
    {
        var payload = DocumentStateSerializer.Serialize(DocumentState.Empty())
            .Replace($"\"SchemaVersion\":{DocumentState.CurrentSchemaVersion}", "\"SchemaVersion\":99", StringComparison.Ordinal);

        Assert.Throws<NotSupportedException>(() => DocumentStateSerializer.Deserialize(payload));
    }

    [Fact]
    public void InvalidJsonIsRejected()
    {
        Assert.Throws<JsonException>(() => DocumentStateSerializer.Deserialize("{not-json}"));
    }
}

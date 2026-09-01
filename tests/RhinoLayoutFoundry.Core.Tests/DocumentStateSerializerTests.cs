using System.Text.Json;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Persistence;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class DocumentStateSerializerTests
{
    [Fact]
    public void MissingSourcePagesRemoveOnlyDocumentBackedTemplates()
    {
        var retainedPageId = Guid.NewGuid();
        var missingPageId = Guid.NewGuid();
        SheetTemplateRecipe Template(string name, Guid? sourcePageViewId) => new(
            Guid.NewGuid(),
            SheetTemplateRecipe.CurrentRecipeVersion,
            name,
            new PaperRecipe(420, 297, "Millimeters"),
            [],
            null,
            [],
            new Dictionary<string, string>(),
            "{index}")
        {
            SourcePageViewId = sourcePageViewId,
        };
        var retained = Template("Retained", retainedPageId);
        var missing = Template("Missing", missingPageId);
        var imported = Template("Imported", null);
        var state = DocumentState.Empty() with { SheetTemplates = [retained, missing, imported] };

        var cleaned = state.RemoveTemplatesForMissingSources(new HashSet<Guid> { retainedPageId });

        Assert.Equal([retained.Id, imported.Id], cleaned.Templates.Select(template => template.Id));
    }

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
    public void VersionEightTemplatesMigrateToCapabilityRegistrations()
    {
        var sheetId = Guid.NewGuid();
        var template = new SheetTemplateRecipe(
            Guid.NewGuid(),
            SheetTemplateRecipe.CurrentRecipeVersion,
            "Existing sheet",
            new PaperRecipe(420, 297, "Millimeters"),
            [],
            new TitleBlockTemplateRecipe(Guid.NewGuid(), "Border", IdentityTransform(),
                "Bottom right", new Dictionary<string, string>()),
            [],
            new Dictionary<string, string>(),
            "{index}")
        {
            SourcePageViewId = sheetId,
        };
        var payload = DocumentStateSerializer.Serialize(DocumentState.Empty() with
            {
                SheetTemplates = [template],
            })
            .Replace($"\"SchemaVersion\":{DocumentState.CurrentSchemaVersion}", "\"SchemaVersion\":8",
                StringComparison.Ordinal)
            .Replace(",\"ViewportRuleSets\":null", string.Empty, StringComparison.Ordinal)
            .Replace(",\"CapabilityTemplates\":null", string.Empty, StringComparison.Ordinal)
            .Replace(",\"CapabilityLinks\":null", string.Empty, StringComparison.Ordinal);

        var restored = DocumentStateSerializer.Deserialize(payload);

        var registration = Assert.Single(restored.TemplateRegistrations);
        Assert.Equal(template.Id, registration.Id);
        Assert.Equal(new HierarchyScope(HierarchyScopeKind.Sheet, sheetId), registration.Source);
        Assert.True(registration.Capabilities.HasFlag(TemplateCapability.Layout));
        Assert.True(registration.Capabilities.HasFlag(TemplateCapability.TitleBlock));
        Assert.Empty(restored.AppearanceRules);
        Assert.Empty(restored.TemplateLinks);
    }

    [Fact]
    public void VersionNineSplitAppearanceMetadataIsDiscardedWithoutConsumingLocalOverrides()
    {
        var source = new HierarchyScope(HierarchyScopeKind.Folder, WellKnownIds.UnorganizedFolderId);
        var target = new HierarchyScope(HierarchyScopeKind.Sheet, Guid.NewGuid());
        var layerId = Guid.NewGuid();
        var registration = new CapabilityTemplateRegistration(
            Guid.NewGuid(), source, TemplateCapability.Layout);
        var sourceRule = new HierarchyViewportRuleSet(source,
            [new LayerVisibilityRule(new LayerReference(layerId, "Walls"), LayerVisibilityOverride.Hidden)], []);
        var localRule = new HierarchyViewportRuleSet(target,
            [new LayerVisibilityRule(new LayerReference(layerId, "Walls"), LayerVisibilityOverride.Visible)], []);
        var state = DocumentState.Empty() with
        {
            ViewportRuleSets = [sourceRule, localRule],
            CapabilityTemplates = [registration],
            CapabilityLinks = [],
        };
        var payload = DocumentStateSerializer.Serialize(state)
            .Replace($"\"SchemaVersion\":{DocumentState.CurrentSchemaVersion}", "\"SchemaVersion\":9",
                StringComparison.Ordinal)
            .Replace(",\"AppearanceStateResources\":null", string.Empty, StringComparison.Ordinal)
            .Replace(",\"AppearanceStateAssignments\":null", string.Empty, StringComparison.Ordinal);

        var restored = DocumentStateSerializer.Deserialize(payload);

        Assert.Empty(restored.AppearanceStates);
        Assert.Empty(restored.StateAssignments);
        var restoredLocal = Assert.Single(restored.AppearanceRules, item => item.Scope == target);
        Assert.Equal(localRule.LayerRules, restoredLocal.LayerRules);
        Assert.Empty(restored.TemplateLinks);
        Assert.Equal(TemplateCapability.Layout, Assert.Single(restored.TemplateRegistrations).Capabilities);
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
    public void SplitAppearanceSchemaIsIntentionallyNotMigrated()
    {
        var payload = DocumentStateSerializer.Serialize(DocumentState.Empty())
            .Replace($"\"SchemaVersion\":{DocumentState.CurrentSchemaVersion}", "\"SchemaVersion\":11",
                StringComparison.Ordinal);

        Assert.Throws<NotSupportedException>(() => DocumentStateSerializer.Deserialize(payload));
    }

    [Fact]
    public void VersionTwelveAppearanceStatesGainEmptyNotes()
    {
        var appearanceState = new AppearanceStateRecord(
            Guid.NewGuid(), WellKnownIds.UnorganizedFolderId, 0, "Existing state", [], []);
        var payload = DocumentStateSerializer.Serialize(DocumentState.Empty() with
            {
                AppearanceStateResources = [appearanceState],
            })
            .Replace($"\"SchemaVersion\":{DocumentState.CurrentSchemaVersion}", "\"SchemaVersion\":12",
                StringComparison.Ordinal)
            .Replace(",\"Notes\":\"\"", string.Empty, StringComparison.Ordinal);

        var restored = DocumentStateSerializer.Deserialize(payload);

        Assert.Equal(DocumentState.CurrentSchemaVersion, restored.SchemaVersion);
        Assert.Equal(string.Empty, Assert.Single(restored.AppearanceStates).Notes);
    }

    [Fact]
    public void InvalidJsonIsRejected()
    {
        Assert.Throws<JsonException>(() => DocumentStateSerializer.Deserialize("{not-json}"));
    }

    private static IReadOnlyList<double> IdentityTransform() =>
    [
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1,
    ];
}

using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Naming;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Persistence;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class LinkedSheetNamingTests
{
    [Fact]
    public void FolderRenameRegeneratesNameAndKeepsFrozenIndex()
    {
        var snapshot = LinkedSnapshot("{folder}-{index:00}", 7, "Plans-07");

        var plan = new RenameFolderPlanner().Plan(
            new RenameFolderRequest(42, 1, TestSnapshots.ChildFolderId, "Plans", "General"),
            snapshot);

        Assert.True(plan.CanApply);
        var linked = Assert.IsType<UpdateLinkedSheetNamesChange>(plan.Changes[1]);
        Assert.Equal("General-07", linked.NewNames[TestSnapshots.SheetOneId]);
        Assert.Equal(7, linked.NewBindings[TestSnapshots.SheetOneId]!.Index);
    }

    [Fact]
    public void MovingSheetUpdatesOnlyFolderToken()
    {
        var snapshot = LinkedSnapshot("{folder}-{index:000}", 12, "Plans-012");

        var plan = new MoveSheetsPlanner().Plan(
            new MoveSheetsRequest(42, 1, TestSnapshots.OtherFolderId, [TestSnapshots.SheetOneId]),
            snapshot);

        Assert.True(plan.CanApply);
        var linked = Assert.IsType<UpdateLinkedSheetNamesChange>(plan.Changes.Last());
        Assert.Equal("Details-012", linked.NewNames[TestSnapshots.SheetOneId]);
        Assert.Equal(12, linked.NewBindings[TestSnapshots.SheetOneId]!.Index);
    }

    [Fact]
    public void DuplicateGeneratedNameBlocksSourceChange()
    {
        var snapshot = LinkedSnapshot("{folder}-{index:00}", 7, "Plans-07");
        snapshot = snapshot with
        {
            Sheets = snapshot.Sheets.ToDictionary(
                pair => pair.Key,
                pair => pair.Key == TestSnapshots.SheetTwoId
                    ? pair.Value with { Name = "General-07" }
                    : pair.Value),
        };

        var plan = new RenameFolderPlanner().Plan(
            new RenameFolderRequest(42, 1, TestSnapshots.ChildFolderId, "Plans", "General"),
            snapshot);

        Assert.False(plan.CanApply);
        Assert.Empty(plan.Changes);
        Assert.Contains(plan.Diagnostics, item => item.Code == "linked_name.duplicate");
    }

    [Fact]
    public void BatchNamingAttachesExistingSheetWithAssignedIndex()
    {
        var snapshot = TestSnapshots.Create();
        var plan = new BatchUpdateSheetsPlanner().Plan(new BatchUpdateSheetsRequest(
            42, 1, [TestSnapshots.SheetOneId], "{folder}-{index:00}", 9, 1,
            null, null, null, null), snapshot);

        Assert.True(plan.CanApply);
        var change = Assert.IsType<BatchUpdateSheetsChange>(Assert.Single(plan.Changes));
        var binding = change.NamingBindings![TestSnapshots.SheetOneId];
        Assert.Equal(9, binding.Index);
        Assert.Equal("Plans-09", binding.LastGeneratedName);
    }

    [Fact]
    public void NamedViewAssignmentRegeneratesViewToken()
    {
        var snapshot = LinkedSnapshot("{view}-{index}", 3, "Old View-3");
        var detail = new DetailSnapshot(
            TestSnapshots.DetailOneId,
            "Detail 1",
            TestSnapshots.DisplayModeOneId,
            "Wireframe");
        snapshot = snapshot with
        {
            Sheets = snapshot.Sheets.ToDictionary(
                pair => pair.Key,
                pair => pair.Key == TestSnapshots.SheetOneId
                    ? pair.Value with { DetailSettings = [detail] }
                    : pair.Value),
            NamedViewNames = new HashSet<string>(["New View"], StringComparer.OrdinalIgnoreCase),
        };

        var plan = new AssignNamedViewPlanner().Plan(new AssignNamedViewRequest(
            42, 1, [TestSnapshots.DetailOneId], "New View"), snapshot);

        Assert.True(plan.CanApply);
        var linked = Assert.IsType<UpdateLinkedSheetNamesChange>(plan.Changes.Last());
        Assert.Equal("New View-3", linked.NewNames[TestSnapshots.SheetOneId]);
        Assert.Equal("New View",
            linked.NewBindings[TestSnapshots.SheetOneId]!.NamedViews[TestSnapshots.DetailOneId]);
    }

    [Fact]
    public void MetadataAndFirstTagRemainLiveWithoutChangingIndex()
    {
        var pattern = "{project}-{discipline}-{tag}-{index:00}";
        var snapshot = LinkedSnapshot(pattern, 5, "Alpha-A-Permit-05");
        snapshot = snapshot with
        {
            DocumentMetadata = new Dictionary<string, string> { ["project"] = "Beta" },
            Sheets = snapshot.Sheets.ToDictionary(
                pair => pair.Key,
                pair => pair.Key == TestSnapshots.SheetOneId
                    ? pair.Value with
                    {
                        Metadata = new Dictionary<string, string> { ["discipline"] = "S" },
                        SheetTags = ["Issue"],
                    }
                    : pair.Value),
        };

        var preview = LinkedSheetNaming.Preview(
            snapshot,
            affectedSheetIds: new HashSet<Guid> { TestSnapshots.SheetOneId });

        Assert.True(preview.CanApply);
        Assert.Equal("Beta-S-Issue-05", preview.Change!.NewNames[TestSnapshots.SheetOneId]);
        Assert.Equal(5, preview.Change.NewBindings[TestSnapshots.SheetOneId]!.Index);
    }

    [Fact]
    public void ExternalNameMismatchDetachesInsteadOfOverwriting()
    {
        var snapshot = LinkedSnapshot("{folder}-{index:00}", 7, "Plans-07");
        snapshot = snapshot with
        {
            Sheets = snapshot.Sheets.ToDictionary(
                pair => pair.Key,
                pair => pair.Key == TestSnapshots.SheetOneId
                    ? pair.Value with { Name = "Manual override" }
                    : pair.Value),
        };

        var preview = LinkedSheetNaming.Preview(
            snapshot,
            folderNameOverrides: new Dictionary<Guid, string>
            {
                [TestSnapshots.ChildFolderId] = "General",
            },
            affectedSheetIds: new HashSet<Guid> { TestSnapshots.SheetOneId });

        Assert.True(preview.CanApply);
        Assert.Empty(preview.Change!.NewNames);
        Assert.Null(preview.Change.NewBindings[TestSnapshots.SheetOneId]);
    }

    [Fact]
    public void NamingBindingRoundTripsAndSchemaTenMigratesDetached()
    {
        var state = DocumentState.Empty();
        var sheetId = Guid.NewGuid();
        var binding = new SheetNamingBinding("{folder}-{index:00}", 4, "Plans-04");
        state = state with
        {
            Sheets = new Dictionary<Guid, SheetRecord>
            {
                [sheetId] = new(sheetId, state.RootFolderId, 0, [],
                    new Dictionary<string, string>(), null, NamingBinding: binding),
            },
        };

        var restored = DocumentStateSerializer.Deserialize(DocumentStateSerializer.Serialize(state));
        Assert.Equal(binding, restored.Sheets[sheetId].NamingBinding);

        var legacyPayload = DocumentStateSerializer.Serialize(state)
            .Replace($"\"SchemaVersion\":{DocumentState.CurrentSchemaVersion}", "\"SchemaVersion\":10",
                StringComparison.Ordinal);
        var migrated = DocumentStateSerializer.Deserialize(legacyPayload);
        Assert.Null(migrated.Sheets[sheetId].NamingBinding);
    }

    private static DocumentSnapshot LinkedSnapshot(string pattern, int index, string name)
    {
        var snapshot = TestSnapshots.Create();
        var binding = new SheetNamingBinding(
            pattern,
            index,
            name,
            new Dictionary<Guid, string> { [TestSnapshots.DetailOneId] = "Old View" });
        return snapshot with
        {
            Sheets = snapshot.Sheets.ToDictionary(
                pair => pair.Key,
                pair => pair.Key == TestSnapshots.SheetOneId
                    ? pair.Value with
                    {
                        Name = name,
                        NamingBinding = binding,
                        SheetTags = ["Permit"],
                    }
                    : pair.Value),
        };
    }
}

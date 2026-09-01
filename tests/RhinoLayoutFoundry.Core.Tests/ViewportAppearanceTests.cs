using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class ViewportAppearanceTests
{
    private static readonly Guid RootLayerId = Guid.Parse("60000000-0000-0000-0000-000000000001");
    private static readonly Guid ChildLayerId = Guid.Parse("60000000-0000-0000-0000-000000000002");

    [Fact]
    public void MostSpecificScopeAndExactObjectRuleWin()
    {
        var objectId = TestSnapshots.ObjectId;
        var scopes = new[]
        {
            new HierarchyScope(HierarchyScopeKind.Folder, TestSnapshots.RootFolderId),
            new HierarchyScope(HierarchyScopeKind.Folder, TestSnapshots.ChildFolderId),
            new HierarchyScope(HierarchyScopeKind.Sheet, TestSnapshots.SheetOneId),
            new HierarchyScope(HierarchyScopeKind.Detail, TestSnapshots.DetailOneId),
        };
        var rootLayer = new LayerSnapshot(RootLayerId, null, "Model", true);
        var childLayer = new LayerSnapshot(ChildLayerId, RootLayerId, "Model::Walls", true);
        var layers = new Dictionary<Guid, LayerSnapshot>
        {
            [RootLayerId] = rootLayer,
            [ChildLayerId] = childLayer,
        };
        var objects = new Dictionary<Guid, ModelObjectSnapshot>
        {
            [objectId] = new(objectId, "Wall", ChildLayerId, childLayer.FullPath, false),
        };
        var folderLayerRule = ModeForLayer(RootLayerId, rootLayer.FullPath,
            TestSnapshots.DisplayModeOneId, "Wireframe");
        var sheetLayerRule = ModeForLayer(ChildLayerId, childLayer.FullPath,
            TestSnapshots.DisplayModeTwoId, "Rendered");
        var detailObjectRule = new ObjectDisplayRule(
            new ObjectDisplaySelector(ObjectDisplaySelectorKind.ExactObject, ObjectId: objectId),
            TestSnapshots.DisplayModeOneId,
            "Wireframe");
        var rules = new Dictionary<HierarchyScope, HierarchyViewportRuleSet>
        {
            [scopes[0]] = new(scopes[0],
                [new LayerVisibilityRule(new LayerReference(ChildLayerId, childLayer.FullPath), LayerVisibilityOverride.Hidden)],
                [folderLayerRule]),
            [scopes[2]] = new(scopes[2],
                [new LayerVisibilityRule(new LayerReference(ChildLayerId, childLayer.FullPath), LayerVisibilityOverride.Visible)],
                [sheetLayerRule]),
            [scopes[3]] = new(scopes[3], [], [detailObjectRule]),
        };

        var result = ViewportAppearanceResolver.Resolve(scopes, rules, layers, objects);

        Assert.Equal(LayerVisibilityOverride.Visible, result.Layers[ChildLayerId]);
        Assert.Equal("Wireframe", result.Objects[objectId].DisplayModeName);
    }

    [Fact]
    public void CapabilityPolicyIsContextAware()
    {
        Assert.False(TemplateCapabilityPolicy.AllowedFor(HierarchyScopeKind.Folder)
            .HasFlag(TemplateCapability.TitleBlock));
        Assert.True(TemplateCapabilityPolicy.AllowedFor(HierarchyScopeKind.Sheet)
            .HasFlag(TemplateCapability.TitleBlock));
        Assert.False(TemplateCapabilityPolicy.AllowedFor(HierarchyScopeKind.Detail)
            .HasFlag(TemplateCapability.TitleBlock));
    }

    [Fact]
    public void RulePlannerValidatesAndStagesOneScope()
    {
        var layer = new LayerSnapshot(ChildLayerId, null, "Walls", true);
        var objectSnapshot = new ModelObjectSnapshot(
            TestSnapshots.ObjectId, "Wall", ChildLayerId, "Walls", false);
        var snapshot = TestSnapshots.Create() with
        {
            LayerSettings = new Dictionary<Guid, LayerSnapshot> { [layer.Id] = layer },
            ModelObjectSettings = new Dictionary<Guid, ModelObjectSnapshot>
            {
                [objectSnapshot.Id] = objectSnapshot,
            },
        };
        var scope = new HierarchyScope(HierarchyScopeKind.Sheet, TestSnapshots.SheetOneId);
        var plan = new SetHierarchyViewportRulesPlanner().Plan(
            new SetHierarchyViewportRulesRequest(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision, scope,
                [new LayerVisibilityRule(new LayerReference(layer.Id, layer.FullPath), LayerVisibilityOverride.Hidden)],
                [new ObjectDisplayRule(
                    new ObjectDisplaySelector(ObjectDisplaySelectorKind.ExactObject,
                        ObjectId: objectSnapshot.Id),
                    TestSnapshots.DisplayModeOneId,
                    "Wireframe")]),
            snapshot);

        Assert.True(plan.CanApply);
        var change = Assert.IsType<SetHierarchyViewportRulesChange>(Assert.Single(plan.Changes));
        Assert.Equal(scope, change.Scope);
        Assert.Single(change.NewRules!.LayerRules);
        Assert.Single(change.NewRules.ObjectDisplayRules);
    }

    [Fact]
    public void NewAppearanceStateStagesItsNameAndAllRulesInOneChange()
    {
        var layer = new LayerSnapshot(ChildLayerId, null, "Walls", true);
        var rule = new LayerVisibilityRule(
            new LayerReference(layer.Id, layer.FullPath),
            LayerVisibilityOverride.Hidden);
        var objectSnapshot = new ModelObjectSnapshot(
            TestSnapshots.ObjectId, "Wall", layer.Id, layer.FullPath, false);
        var objectRule = new ObjectDisplayRule(
            new ObjectDisplaySelector(ObjectDisplaySelectorKind.ExactObject,
                ObjectId: objectSnapshot.Id),
            TestSnapshots.DisplayModeOneId,
            "Wireframe");
        var snapshot = TestSnapshots.Create() with
        {
            LayerSettings = new Dictionary<Guid, LayerSnapshot> { [layer.Id] = layer },
            ModelObjectSettings = new Dictionary<Guid, ModelObjectSnapshot>
            {
                [objectSnapshot.Id] = objectSnapshot,
            },
        };

        var plan = new CreateAppearanceStatePlanner().Plan(
            new CreateAppearanceStateRequest(
                snapshot.DocumentRuntimeSerialNumber,
                snapshot.Revision,
                TestSnapshots.RootFolderId,
                "Presentation appearance",
                LayerRules: [rule],
                ObjectDisplayRules: [objectRule],
                Notes: "Presentation-only overrides"),
            snapshot);

        Assert.True(plan.CanApply);
        var change = Assert.IsType<SetAppearanceStateResourceChange>(Assert.Single(plan.Changes));
        Assert.Equal("Presentation appearance", change.NewState!.Name);
        Assert.Equal(rule, Assert.Single(change.NewState.LayerRules));
        Assert.Equal(objectRule, Assert.Single(change.NewState.ObjectDisplayRules));
        Assert.Equal("Presentation-only overrides", change.NewState.Notes);
    }

    [Fact]
    public void AppearanceStatesProvideBasisAndLocalRulesWinAtEachSpecificity()
    {
        var layer = new LayerSnapshot(ChildLayerId, null, "Walls", true);
        var folder = new HierarchyScope(HierarchyScopeKind.Folder, TestSnapshots.ChildFolderId);
        var sheet = new HierarchyScope(HierarchyScopeKind.Sheet, TestSnapshots.SheetOneId);
        var detail = new HierarchyScope(HierarchyScopeKind.Detail, TestSnapshots.DetailOneId);
        var folderState = new AppearanceStateRecord(Guid.NewGuid(), TestSnapshots.ChildFolderId, 0,
            "Folder basis",
            [new LayerVisibilityRule(new LayerReference(layer.Id, layer.FullPath), LayerVisibilityOverride.Hidden)], []);
        var sheetState = new AppearanceStateRecord(Guid.NewGuid(), TestSnapshots.ChildFolderId, 1,
            "Sheet basis",
            [new LayerVisibilityRule(new LayerReference(layer.Id, layer.FullPath), LayerVisibilityOverride.Visible)], []);
        var rules = new Dictionary<HierarchyScope, HierarchyViewportRuleSet>
        {
            [detail] = new(detail,
                [new LayerVisibilityRule(new LayerReference(layer.Id, layer.FullPath), LayerVisibilityOverride.Hidden)], []),
        };
        var assignments = new[]
        {
            new AppearanceStateAssignment(Guid.NewGuid(), folder, folderState.Id),
            new AppearanceStateAssignment(Guid.NewGuid(), sheet, sheetState.Id),
        };

        var resolved = ViewportAppearanceResolver.Resolve(
            [folder, sheet, detail],
            rules,
            new Dictionary<Guid, LayerSnapshot> { [layer.Id] = layer },
            new Dictionary<Guid, ModelObjectSnapshot>(),
            appearanceStates: new Dictionary<Guid, AppearanceStateRecord>
            {
                [folderState.Id] = folderState,
                [sheetState.Id] = sheetState,
            },
            stateAssignments: assignments);

        Assert.Equal(LayerVisibilityOverride.Hidden, resolved.Layers[layer.Id]);
    }

    [Fact]
    public void ClearingAssignmentDoesNotChangeLocalOverrides()
    {
        var state = new AppearanceStateRecord(Guid.NewGuid(), TestSnapshots.ChildFolderId, 0,
            "Walls", [], []);
        var target = new HierarchyScope(HierarchyScopeKind.Sheet, TestSnapshots.SheetOneId);
        var assignment = new AppearanceStateAssignment(Guid.NewGuid(), target, state.Id);
        var local = new HierarchyViewportRuleSet(target,
            [new LayerVisibilityRule(new LayerReference(ChildLayerId, "Walls"), LayerVisibilityOverride.Visible)], []);
        var snapshot = TestSnapshots.Create() with
        {
            AppearanceStateResources = [state],
            AppearanceStateAssignments = [assignment],
            ViewportRuleSets = [local],
        };

        var plan = new AssignAppearanceStatePlanner().Plan(new AssignAppearanceStateRequest(
            snapshot.DocumentRuntimeSerialNumber, snapshot.Revision, target,
            null), snapshot);

        var change = Assert.IsType<SetAppearanceStateAssignmentChange>(Assert.Single(plan.Changes));
        Assert.Null(change.NewAssignment);
        Assert.Equal(local, Assert.Single(snapshot.AppearanceRules));
    }

    private static ObjectDisplayRule ModeForLayer(
        Guid layerId,
        string fullPath,
        Guid displayModeId,
        string displayModeName) => new(
        new ObjectDisplaySelector(ObjectDisplaySelectorKind.Layer,
            LayerId: layerId,
            LayerFullPath: fullPath),
        displayModeId,
        displayModeName);
}

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
            TestSnapshots.DisplayModeOneId, "Wireframe", includeChildren: true);
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
    public void CapabilityLinkPlannerRejectsCycles()
    {
        var sheet = new HierarchyScope(HierarchyScopeKind.Sheet, TestSnapshots.SheetOneId);
        var other = new HierarchyScope(HierarchyScopeKind.Sheet, TestSnapshots.SheetTwoId);
        var firstRegistration = new CapabilityTemplateRegistration(
            Guid.NewGuid(), sheet, TemplateCapability.LayerStates);
        var secondRegistration = new CapabilityTemplateRegistration(
            Guid.NewGuid(), other, TemplateCapability.LayerStates);
        var snapshot = TestSnapshots.Create() with
        {
            CapabilityTemplates = [firstRegistration, secondRegistration],
            CapabilityLinks =
            [
                new CapabilityTemplateLink(Guid.NewGuid(), other, firstRegistration.Id,
                    TemplateCapability.LayerStates, [], new TemplateCapabilityPayload()),
            ],
        };

        var plan = new LinkTemplateCapabilityPlanner().Plan(new LinkTemplateCapabilityRequest(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            sheet,
            secondRegistration.Id,
            TemplateCapability.LayerStates), snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, diagnostic => diagnostic.Code == "template.link_cycle");
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
    public void LiveCapabilityLinkFeedsTargetBeforeItsLocalOverrides()
    {
        var source = new HierarchyScope(HierarchyScopeKind.Sheet, TestSnapshots.SheetOneId);
        var target = new HierarchyScope(HierarchyScopeKind.Sheet, TestSnapshots.SheetTwoId);
        var layer = new LayerSnapshot(ChildLayerId, null, "Walls", true);
        var registration = new CapabilityTemplateRegistration(
            Guid.NewGuid(), source, TemplateCapability.LayerStates);
        var rules = new Dictionary<HierarchyScope, HierarchyViewportRuleSet>
        {
            [source] = new(source,
                [new LayerVisibilityRule(
                    new LayerReference(layer.Id, layer.FullPath),
                    LayerVisibilityOverride.Hidden)], []),
            [target] = new(target,
                [new LayerVisibilityRule(
                    new LayerReference(layer.Id, layer.FullPath),
                    LayerVisibilityOverride.Visible)], []),
        };
        var link = new CapabilityTemplateLink(
            Guid.NewGuid(), target, registration.Id, TemplateCapability.LayerStates, [],
            new TemplateCapabilityPayload());

        var resolved = ViewportAppearanceResolver.Resolve(
            [target],
            rules,
            new Dictionary<Guid, LayerSnapshot> { [layer.Id] = layer },
            new Dictionary<Guid, ModelObjectSnapshot>(),
            [link],
            new Dictionary<Guid, CapabilityTemplateRegistration> { [registration.Id] = registration });

        Assert.Equal(LayerVisibilityOverride.Visible, resolved.Layers[layer.Id]);
    }

    [Fact]
    public void MissingSheetTemplateSourceDetachesToItsLastResolvedValues()
    {
        var source = new HierarchyScope(HierarchyScopeKind.Sheet, TestSnapshots.SheetOneId);
        var target = new HierarchyScope(HierarchyScopeKind.Sheet, TestSnapshots.SheetTwoId);
        var registration = new CapabilityTemplateRegistration(
            Guid.NewGuid(), source, TemplateCapability.LayerStates);
        var frozenRule = new LayerVisibilityRule(
            new LayerReference(ChildLayerId, "Walls"), LayerVisibilityOverride.Hidden);
        var state = DocumentState.Empty() with
        {
            CapabilityTemplates = [registration],
            CapabilityLinks =
            [
                new CapabilityTemplateLink(
                    Guid.NewGuid(), target, registration.Id, TemplateCapability.LayerStates, [],
                    new TemplateCapabilityPayload(LayerRules: [frozenRule])),
            ],
        };

        var cleaned = state.RemoveTemplatesForMissingSources(
            new HashSet<Guid> { TestSnapshots.SheetTwoId });

        Assert.Empty(cleaned.TemplateRegistrations);
        Assert.Empty(cleaned.TemplateLinks);
        Assert.Equal(frozenRule,
            Assert.Single(Assert.Single(cleaned.AppearanceRules).LayerRules));
    }

    private static ObjectDisplayRule ModeForLayer(
        Guid layerId,
        string fullPath,
        Guid displayModeId,
        string displayModeName,
        bool includeChildren = false) => new(
        new ObjectDisplaySelector(ObjectDisplaySelectorKind.Layer,
            LayerId: layerId,
            LayerFullPath: fullPath,
            IncludeChildLayers: includeChildren),
        displayModeId,
        displayModeName);
}

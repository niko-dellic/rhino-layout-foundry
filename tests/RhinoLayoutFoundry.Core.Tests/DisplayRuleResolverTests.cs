using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Rules;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class DisplayRuleResolverTests
{
    [Fact]
    public void LaterRuleWinsForSameObjectAndDetail()
    {
        var first = Rule(1, TestSnapshots.DisplayModeOneId);
        var second = Rule(2, TestSnapshots.DisplayModeTwoId);

        var result = DisplayRuleResolver.Resolve(TestSnapshots.Create(), [first, second]);

        var key = new ObjectDetailKey(TestSnapshots.ObjectId, TestSnapshots.DetailOneId);
        Assert.Equal(TestSnapshots.DisplayModeTwoId, result.Overrides[key]);
    }

    [Fact]
    public void DisabledRulesDoNotContributeOverrides()
    {
        var rule = Rule(1, TestSnapshots.DisplayModeOneId) with { Enabled = false };

        var result = DisplayRuleResolver.Resolve(TestSnapshots.Create(), [rule]);

        Assert.Empty(result.Overrides);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void FolderTargetFollowsCurrentMembership()
    {
        var rule = Rule(1, TestSnapshots.DisplayModeOneId);
        var before = DisplayRuleResolver.Resolve(TestSnapshots.Create(), [rule]);
        var after = DisplayRuleResolver.Resolve(
            TestSnapshots.Create(TestSnapshots.ChildFolderId),
            [rule]);

        Assert.Single(before.Overrides);
        Assert.Equal(2, after.Overrides.Count);
        Assert.Contains(
            new ObjectDetailKey(TestSnapshots.ObjectId, TestSnapshots.DetailTwoId),
            after.Overrides.Keys);
    }

    [Fact]
    public void MissingReferencesRemainDiagnosable()
    {
        var rule = Rule(1, TestSnapshots.DisplayModeOneId) with
        {
            ObjectIds = [Guid.NewGuid()],
            Targets = [new HierarchySelector(HierarchySelectorKind.Detail, Guid.NewGuid())],
        };

        var result = DisplayRuleResolver.Resolve(TestSnapshots.Create(), [rule]);

        Assert.Empty(result.Overrides);
        Assert.Contains(result.Diagnostics, item => item.Code == "RULE_OBJECT_MISSING");
        Assert.Contains(result.Diagnostics, item => item.Code == "RULE_TARGET_MISSING");
    }

    [Fact]
    public void MissingDisplayModeSkipsRule()
    {
        var rule = Rule(1, Guid.NewGuid());

        var result = DisplayRuleResolver.Resolve(TestSnapshots.Create(), [rule]);

        Assert.Empty(result.Overrides);
        Assert.Contains(result.Diagnostics, item => item.Code == "RULE_DISPLAY_MODE_MISSING");
    }

    private static DisplayRule Rule(int priority, Guid displayModeId)
    {
        return new DisplayRule(
            Guid.NewGuid(),
            $"Rule {priority}",
            true,
            priority,
            [TestSnapshots.ObjectId],
            [new HierarchySelector(HierarchySelectorKind.Folder, TestSnapshots.ChildFolderId)],
            displayModeId);
    }
}


using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class LayoutTemplateRegistrationTests
{
    [Theory]
    [InlineData(HierarchyScopeKind.Sheet)]
    [InlineData(HierarchyScopeKind.Detail)]
    public void RegistrationCanBeEnabledAndCleared(HierarchyScopeKind kind)
    {
        var snapshot = TestSnapshots.Create();
        var source = new HierarchyScope(kind, kind == HierarchyScopeKind.Sheet ? TestSnapshots.SheetOneId : TestSnapshots.DetailOneId);
        var planner = new SetLayoutTemplateRegistrationPlanner();
        var request = new SetLayoutTemplateRegistrationRequest(42, 1, source, true);
        var plan = planner.Plan(request, snapshot);
        Assert.True(plan.CanApply);
        var registered = Assert.IsType<SetLayoutTemplateRegistrationChange>(Assert.Single(plan.Changes)).NewRegistration!;
        Assert.Equal(source, registered.Source);
        snapshot = snapshot with
        {
            TemplateRegistrations = [registered]
        };
        var clear = planner.Plan(request with { Registered = false }, snapshot);
        Assert.True(clear.CanApply);
        var change = Assert.IsType<SetLayoutTemplateRegistrationChange>(Assert.Single(clear.Changes));
        Assert.Equal(registered, change.ExpectedRegistration);
        Assert.Null(change.NewRegistration);
    }

    [Fact]
    public void FolderAndMissingSourcesAreRejected()
    {
        var snapshot = TestSnapshots.Create();
        foreach (var source in new[]
        {
            new HierarchyScope(HierarchyScopeKind.Folder, snapshot.RootFolderId),
            new HierarchyScope(HierarchyScopeKind.Sheet, Guid.NewGuid())
        }

        )
        {
            var plan = new SetLayoutTemplateRegistrationPlanner().Plan(new(42, 1, source, true), snapshot);
            Assert.False(plan.CanApply);
            Assert.Empty(plan.Changes);
        }
    }

    [Fact]
    public void CreationRequiresSpecificationsAndRejectsDeletedTemplate()
    {
        var snapshot = TestSnapshots.Create();
        var planner = new BatchCreateSheetsPlanner();
        var empty = new BatchCreateSheetsRequest(42, 1, snapshot.RootFolderId, [], "A-{index}", 1, 1);
        Assert.False(planner.Plan(empty, snapshot).CanApply);
        Assert.False(planner.Plan(empty with { CreationSpecs = null! }, snapshot).CanApply);
        var missing = empty with
        {
            CreationSpecs = [new(1, new PaperRecipe(420, 297, "Millimeters"), TemplateId: Guid.NewGuid())]
        };
        Assert.False(planner.Plan(missing, snapshot).CanApply);
    }
}

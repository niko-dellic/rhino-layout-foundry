using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class DuplicateFolderPlannerTests
{
    [Fact]
    public void FolderSubtreeGetsStableNewFolderIds()
    {
        var nested = Guid.NewGuid();
        var snapshot = TestSnapshots.Create() with
        {
            Folders = new Dictionary<Guid, FolderRecord>(TestSnapshots.Create().Folders)
            {
                [nested] = new(nested, TestSnapshots.ChildFolderId, "Nested", 0),
            },
        };
        var plan = new DuplicateFolderPlanner().Plan(new DuplicateFolderRequest(
            42, 1, TestSnapshots.ChildFolderId, "Plans"), snapshot);

        Assert.True(plan.CanApply);
        var change = (DuplicateFolderChange)plan.Changes.Single();
        Assert.Equal("Plans copy", change.NewName);
        Assert.Equal(2, change.FolderIdMap.Count);
        Assert.DoesNotContain(Guid.Empty, change.FolderIdMap.Values);
    }

    [Fact]
    public void RepeatedCopyNameGetsNumericSuffix()
    {
        var copyId = Guid.NewGuid();
        var snapshot = TestSnapshots.Create() with
        {
            Folders = new Dictionary<Guid, FolderRecord>(TestSnapshots.Create().Folders)
            {
                [copyId] = new(copyId, TestSnapshots.RootFolderId, "Plans copy", 3),
            },
        };
        var plan = new DuplicateFolderPlanner().Plan(new DuplicateFolderRequest(
            42, 1, TestSnapshots.ChildFolderId, "Plans"), snapshot);

        Assert.Equal("Plans copy 2", ((DuplicateFolderChange)plan.Changes.Single()).NewName);
    }
}

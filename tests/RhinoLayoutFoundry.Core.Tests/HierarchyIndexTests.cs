using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Hierarchy;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class HierarchyIndexTests
{
    [Fact]
    public void FolderSelectorIncludesDescendantFolders()
    {
        var index = new HierarchyIndex(TestSnapshots.Create());

        var details = index.ResolveDetails(
            [new HierarchySelector(HierarchySelectorKind.Folder, TestSnapshots.RootFolderId)]);

        Assert.Equal([TestSnapshots.DetailOneId, TestSnapshots.DetailTwoId], details);
    }

    [Fact]
    public void OverlappingSelectorsAreDeduplicated()
    {
        var index = new HierarchyIndex(TestSnapshots.Create());

        var details = index.ResolveDetails(
        [
            new HierarchySelector(HierarchySelectorKind.Folder, TestSnapshots.ChildFolderId),
            new HierarchySelector(HierarchySelectorKind.Sheet, TestSnapshots.SheetOneId),
            new HierarchySelector(HierarchySelectorKind.Detail, TestSnapshots.DetailOneId),
        ]);

        Assert.Equal([TestSnapshots.DetailOneId], details);
    }

    [Fact]
    public void MissingSelectorCannotResolve()
    {
        var index = new HierarchyIndex(TestSnapshots.Create());

        var found = index.TryResolveDetails(
            new HierarchySelector(HierarchySelectorKind.Detail, Guid.NewGuid()),
            out var details);

        Assert.False(found);
        Assert.Empty(details);
    }

    [Fact]
    public void FolderCyclesAreRejected()
    {
        var root = TestSnapshots.RootFolderId;
        var child = TestSnapshots.ChildFolderId;
        var folders = new Dictionary<Guid, FolderRecord>
        {
            [root] = new(root, null, "Root", 0),
            [child] = new(child, TestSnapshots.OtherFolderId, "Child", 0),
            [TestSnapshots.OtherFolderId] = new(TestSnapshots.OtherFolderId, child, "Other", 0),
        };
        var snapshot = TestSnapshots.Create() with { Folders = folders };

        var error = Assert.Throws<ArgumentException>(() => new HierarchyIndex(snapshot));

        Assert.Contains("cycle", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DetailCannotBelongToTwoSheets()
    {
        var snapshot = TestSnapshots.Create();
        var duplicate = snapshot.Sheets[TestSnapshots.SheetTwoId] with
        {
            DetailIds = [TestSnapshots.DetailOneId],
        };
        var sheets = snapshot.Sheets.ToDictionary(item => item.Key, item => item.Value);
        sheets[TestSnapshots.SheetTwoId] = duplicate;

        var error = Assert.Throws<ArgumentException>(
            () => new HierarchyIndex(snapshot with { Sheets = sheets }));

        Assert.Contains("more than one sheet", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}


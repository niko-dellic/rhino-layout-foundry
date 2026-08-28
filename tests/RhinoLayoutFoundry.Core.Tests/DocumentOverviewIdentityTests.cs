using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class DocumentOverviewIdentityTests
{
    [Fact]
    public void SaveAsNameChangeInvalidatesSameDocumentWithSameSheetCount()
    {
        var rootId = Guid.NewGuid();
        var overview = new DocumentOverview(
            42,
            "Untitled Rhino document",
            rootId,
            [new FolderOverview(rootId, null, "Root", 0)],
            []);

        var renamed = new DocumentOverviewIdentity(42, 0, "smoke-test");

        Assert.False(renamed.Matches(overview));
    }

    [Fact]
    public void UnchangedNameAndSheetCountMatchCurrentOverview()
    {
        var rootId = Guid.NewGuid();
        var overview = new DocumentOverview(
            42,
            "smoke-test",
            rootId,
            [new FolderOverview(rootId, null, "Root", 0)],
            []);

        var identity = new DocumentOverviewIdentity(42, 0, "smoke-test");

        Assert.True(identity.Matches(overview));
    }
}

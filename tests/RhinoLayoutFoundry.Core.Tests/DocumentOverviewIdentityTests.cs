using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class DocumentOverviewIdentityTests
{
    [Fact]
    public void SaveAsNameChangeInvalidatesSameDocumentWithSameSheetCount()
    {
        var rootId = Guid.NewGuid();
        var overview = new DocumentOverview(
            DocumentRuntimeSerialNumber: 42,
            DocumentName: "Untitled Rhino document",
            RootFolderId: rootId,
            Folders: [new FolderOverview(Id: rootId, ParentId: null, Name: "Root", Order: 0)],
            Sheets: []);

        var renamed = new DocumentOverviewIdentity(42, 0, "smoke-test");

        Assert.False(renamed.Matches(overview));
    }

    [Fact]
    public void UnchangedNameAndSheetCountMatchCurrentOverview()
    {
        var rootId = Guid.NewGuid();
        var overview = new DocumentOverview(
            DocumentRuntimeSerialNumber: 42,
            DocumentName: "smoke-test",
            RootFolderId: rootId,
            Folders: [new FolderOverview(Id: rootId, ParentId: null, Name: "Root", Order: 0)],
            Sheets: []);

        var identity = new DocumentOverviewIdentity(42, 0, "smoke-test");

        Assert.True(identity.Matches(overview));
    }
}

using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class LayoutPrintScopeResolverTests
{
    [Fact]
    public void ResolveAllMatchesVisibleRecursiveTreeOrder()
    {
        var overview = CreateOverview();

        var result = LayoutPrintScopeResolver.Resolve(overview, null);

        Assert.True(result.Exists);
        Assert.Equal("All Layouts", result.Name);
        Assert.Equal([NestedSheetId, FolderSheetId, RootSheetId], result.SheetPageViewIds);
    }

    [Fact]
    public void ResolveFolderIncludesNestedFoldersAndDirectSheets()
    {
        var result = LayoutPrintScopeResolver.Resolve(CreateOverview(), FolderId);

        Assert.True(result.Exists);
        Assert.Equal("Issue Set", result.Name);
        Assert.Equal([NestedSheetId, FolderSheetId], result.SheetPageViewIds);
    }

    [Fact]
    public void ResolveEmptyFolderReturnsAnExistingEmptyScope()
    {
        var result = LayoutPrintScopeResolver.Resolve(CreateOverview(), EmptyFolderId);

        Assert.True(result.Exists);
        Assert.False(result.HasSheets);
        Assert.Empty(result.SheetPageViewIds);
    }

    [Fact]
    public void ResolveMissingFolderIsDiagnosable()
    {
        var result = LayoutPrintScopeResolver.Resolve(CreateOverview(), Guid.NewGuid());

        Assert.False(result.Exists);
        Assert.Empty(result.SheetPageViewIds);
    }

    [Fact]
    public void ResolveAllOmitsLayoutsExcludedFromPrintAll()
    {
        var overview = CreateOverview();
        overview = overview with
        {
            Sheets = overview.Sheets.Select(sheet => sheet.PageViewId == FolderSheetId
                ? sheet with { IncludeInPrintAll = false }
                : sheet).ToArray(),
        };

        var result = LayoutPrintScopeResolver.Resolve(overview, null);

        Assert.Equal([NestedSheetId, RootSheetId], result.SheetPageViewIds);
    }

    private static readonly Guid RootId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid FolderId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid NestedFolderId = Guid.Parse("10000000-0000-0000-0000-000000000003");
    private static readonly Guid EmptyFolderId = Guid.Parse("10000000-0000-0000-0000-000000000004");
    private static readonly Guid RootSheetId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid FolderSheetId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid NestedSheetId = Guid.Parse("20000000-0000-0000-0000-000000000003");

    private static DocumentOverview CreateOverview()
    {
        return new DocumentOverview(
            DocumentRuntimeSerialNumber: 42,
            DocumentName: "Print fixture",
            RootFolderId: RootId,
            Folders: [
                new FolderOverview(Id: RootId, ParentId: null, Name: "Root", Order: 0),
                new FolderOverview(Id: FolderId, ParentId: RootId, Name: "Issue Set", Order: 1),
                new FolderOverview(Id: NestedFolderId, ParentId: FolderId, Name: "Nested", Order: 0),
                new FolderOverview(Id: EmptyFolderId, ParentId: RootId, Name: "Empty", Order: 0),
            ],
                    Sheets: [
                Sheet(RootSheetId, RootId, "Root sheet", 0),
                Sheet(FolderSheetId, FolderId, "Folder sheet", 0),
                Sheet(NestedSheetId, NestedFolderId, "Nested sheet", 0),
            ]);
    }

    private static SheetOverview Sheet(Guid id, Guid folderId, string name, int order)
    {
        return new SheetOverview(PageViewId: id, FolderId: folderId, Name: name, Order: order, Details: []);
    }
}

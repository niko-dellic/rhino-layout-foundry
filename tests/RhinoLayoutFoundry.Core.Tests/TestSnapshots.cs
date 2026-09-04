using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

internal static class TestSnapshots
{
    internal static readonly Guid RootFolderId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    internal static readonly Guid ChildFolderId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    internal static readonly Guid OtherFolderId = Guid.Parse("10000000-0000-0000-0000-000000000003");
    internal static readonly Guid SheetOneId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    internal static readonly Guid SheetTwoId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    internal static readonly Guid DetailOneId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    internal static readonly Guid DetailTwoId = Guid.Parse("30000000-0000-0000-0000-000000000002");
    internal static readonly Guid ObjectId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    internal static readonly Guid DisplayModeOneId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    internal static readonly Guid DisplayModeTwoId = Guid.Parse("50000000-0000-0000-0000-000000000002");

    internal static DocumentSnapshot Create(Guid? sheetTwoFolderId = null)
    {
        var folders = new Dictionary<Guid, FolderRecord>
        {
            [RootFolderId] = new(RootFolderId, null, "Root", 0),
            [ChildFolderId] = new(ChildFolderId, RootFolderId, "Plans", 0),
            [OtherFolderId] = new(OtherFolderId, RootFolderId, "Details", 1),
        };

        var sheets = new Dictionary<Guid, SheetSnapshot>
        {
            [SheetOneId] = new(
                SheetOneId,
                ChildFolderId,
                0,
                "A-001",
                [DetailOneId],
                new Dictionary<string, string>()),
            [SheetTwoId] = new(
                SheetTwoId,
                sheetTwoFolderId ?? OtherFolderId,
                1,
                "A-002",
                [DetailTwoId],
                new Dictionary<string, string>()),
        };

        return new DocumentSnapshot(
            DocumentRuntimeSerialNumber: 42,
            Revision: 1,
            RootFolderId: RootFolderId,
            Folders: folders,
            Sheets: sheets,
            ExistingObjectIds: new HashSet<Guid> { ObjectId },
            DisplayModeIds: new HashSet<Guid> { DisplayModeOneId, DisplayModeTwoId });
    }

    internal static DocumentOverview Overview(int sheetCount, int detailsPerSheet)
    {
        var sheets = Enumerable.Range(0, sheetCount)
            .Select(sheetIndex => new SheetOverview(
                PageViewId: Guid.Parse($"20000000-0000-0000-0000-{sheetIndex + 1:000000000000}"),
                FolderId: RootFolderId,
                Name: $"A-{sheetIndex + 1:000}",
                Order: sheetIndex,
                Details: Enumerable.Range(0, detailsPerSheet)
                    .Select(detailIndex => new DetailOverview(
                        DetailViewportId: Guid.Parse($"30000000-0000-{sheetIndex + 1:0000}-0000-{detailIndex + 1:000000000000}"),
                        Name: $"Detail {detailIndex + 1}",
                        Order: detailIndex))
                    .ToArray()))
            .ToArray();

        return new DocumentOverview(
            DocumentRuntimeSerialNumber: 42,
            DocumentName: "Test model",
            RootFolderId: RootFolderId,
            Folders: [new FolderOverview(Id: RootFolderId, ParentId: null, Name: "Unorganized", Order: 0)],
            Sheets: sheets);
    }
}

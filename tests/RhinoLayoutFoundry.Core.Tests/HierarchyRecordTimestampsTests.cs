using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Persistence;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class HierarchyRecordTimestampsTests
{
    private static readonly DateTimeOffset Earlier =
        new(2026, 9, 4, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = Earlier.AddDays(1);

    [Fact]
    public void NewFolderAndSheetReceiveMatchingCreationAndModificationDates()
    {
        var before = DocumentState.Empty();
        var folderId = Guid.NewGuid();
        var sheetId = Guid.NewGuid();
        var after = before with
        {
            Folders = [.. before.Folders, new(folderId, before.RootFolderId, "Plans", 1)],
            Sheets = new Dictionary<Guid, SheetRecord>
            {
                [sheetId] = new(sheetId, folderId, 0, new Dictionary<string, string>(), null),
            },
        };

        var stamped = HierarchyRecordTimestamps.ApplyChanges(before, after, Now);

        var folder = stamped.Folders.Single(item => item.Id == folderId);
        Assert.Equal(Now, folder.CreatedUtc);
        Assert.Equal(Now, folder.LastModifiedUtc);
        Assert.Equal(Now, stamped.Sheets[sheetId].CreatedUtc);
        Assert.Equal(Now, stamped.Sheets[sheetId].LastModifiedUtc);
    }

    [Fact]
    public void ChangedAndExplicitlyTouchedRecordsPreserveCreationAndAdvanceModification()
    {
        var folderId = Guid.NewGuid();
        var sheetId = Guid.NewGuid();
        var before = DocumentState.Empty() with
        {
            Folders =
            [
                DocumentState.Empty().Folders[0],
                new(folderId, WellKnownIds.UnorganizedFolderId, "Plans", 1,
                    CreatedUtc: Earlier, LastModifiedUtc: Earlier),
            ],
            Sheets = new Dictionary<Guid, SheetRecord>
            {
                [sheetId] = new(sheetId, folderId, 0, new Dictionary<string, string>(), null,
                    CreatedUtc: Earlier, LastModifiedUtc: Earlier),
            },
        };
        var after = before with
        {
            Folders = before.Folders.Select(folder => folder.Id == folderId
                ? folder with { Name = "Issued Plans" }
                : folder).ToArray(),
        };

        var stamped = HierarchyRecordTimestamps.ApplyChanges(
            before,
            after,
            Now,
            touchedSheetIds: new HashSet<Guid> { sheetId });

        var folder = stamped.Folders.Single(item => item.Id == folderId);
        Assert.Equal(Earlier, folder.CreatedUtc);
        Assert.Equal(Now, folder.LastModifiedUtc);
        Assert.Equal(Earlier, stamped.Sheets[sheetId].CreatedUtc);
        Assert.Equal(Now, stamped.Sheets[sheetId].LastModifiedUtc);
    }

    [Fact]
    public void UnchangedRecordsKeepTheirOriginalDates()
    {
        var folder = new FolderRecord(Guid.NewGuid(), WellKnownIds.UnorganizedFolderId,
            "Plans", 1, CreatedUtc: Earlier, LastModifiedUtc: Earlier);
        var before = DocumentState.Empty() with
        {
            Folders = [.. DocumentState.Empty().Folders, folder],
        };

        var stamped = HierarchyRecordTimestamps.ApplyChanges(before, before, Now);

        Assert.Equal(Earlier, stamped.Folders.Single(item => item.Id == folder.Id).LastModifiedUtc);
    }

    [Fact]
    public void FirstEditOfAnUntrackedNativeSheetEstablishesBothDates()
    {
        var sheetId = Guid.NewGuid();
        var before = DocumentState.Empty() with
        {
            Sheets = new Dictionary<Guid, SheetRecord>
            {
                [sheetId] = new(sheetId, WellKnownIds.UnorganizedFolderId, 0,
                    new Dictionary<string, string>(), null),
            },
        };

        var stamped = HierarchyRecordTimestamps.ApplyChanges(
            before,
            before,
            Now,
            touchedSheetIds: new HashSet<Guid> { sheetId });

        Assert.Equal(Now, stamped.Sheets[sheetId].CreatedUtc);
        Assert.Equal(Now, stamped.Sheets[sheetId].LastModifiedUtc);
        DocumentStateSerializer.Validate(stamped);
    }

    [Fact]
    public void LegacyBackfillUsesDocumentDatesAndClampsInvalidFileOrdering()
    {
        var state = DocumentState.Empty();

        var stamped = HierarchyRecordTimestamps.BackfillMissing(
            state,
            created: Now,
            lastModified: Earlier);

        Assert.Equal(Now, stamped.Folders[0].CreatedUtc);
        Assert.Equal(Now, stamped.Folders[0].LastModifiedUtc);
    }

    [Fact]
    public void FirstLegacyEditPreservesBackfilledCreationAndAdvancesModification()
    {
        var sheetId = Guid.NewGuid();
        var legacy = DocumentState.Empty() with
        {
            Sheets = new Dictionary<Guid, SheetRecord>
            {
                [sheetId] = new(sheetId, WellKnownIds.UnorganizedFolderId, 0,
                    new Dictionary<string, string>(), null),
            },
        };
        var backfilled = HierarchyRecordTimestamps.BackfillMissing(
            legacy,
            Earlier,
            Earlier.AddHours(2));

        var stamped = HierarchyRecordTimestamps.ApplyChanges(
            backfilled,
            backfilled,
            Now,
            touchedSheetIds: new HashSet<Guid> { sheetId });

        Assert.Equal(Earlier, stamped.Sheets[sheetId].CreatedUtc);
        Assert.Equal(Now, stamped.Sheets[sheetId].LastModifiedUtc);
        DocumentStateSerializer.Validate(stamped);
    }
}

namespace RhinoLayoutFoundry.Core.Domain;

/// <summary>
/// Maintains persisted creation and modification timestamps for Foundry-owned
/// folder and sheet records. Existing records are compared without their audit
/// fields so timestamp updates do not cause further modifications.
/// </summary>
public static class HierarchyRecordTimestamps
{
    public static DocumentState ApplyChanges(
        DocumentState previous,
        DocumentState current,
        DateTimeOffset timestamp,
        IReadOnlySet<Guid>? touchedFolderIds = null,
        IReadOnlySet<Guid>? touchedSheetIds = null)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        var now = timestamp.ToUniversalTime();
        var previousFolders = previous.Folders.ToDictionary(folder => folder.Id);
        var folders = current.Folders.Select(folder =>
        {
            if (!previousFolders.TryGetValue(folder.Id, out var prior))
                return folder with { CreatedUtc = now, LastModifiedUtc = now };

            var changed = touchedFolderIds?.Contains(folder.Id) == true ||
                          !FolderContentEquals(prior, folder);
            return changed
                ? folder with
                {
                    CreatedUtc = folder.CreatedUtc ?? prior.CreatedUtc ?? now,
                    LastModifiedUtc = now,
                }
                : folder;
        }).ToArray();

        var previousSheets = previous.Sheets;
        var sheets = current.Sheets.ToDictionary(pair => pair.Key, pair =>
        {
            var sheet = pair.Value;
            if (!previousSheets.TryGetValue(pair.Key, out var prior))
                return sheet with { CreatedUtc = now, LastModifiedUtc = now };

            var changed = touchedSheetIds?.Contains(pair.Key) == true ||
                          !SheetContentEquals(prior, sheet);
            return changed
                ? sheet with
                {
                    CreatedUtc = sheet.CreatedUtc ?? prior.CreatedUtc ?? now,
                    LastModifiedUtc = now,
                }
                : sheet;
        });

        return current with { Folders = folders, Sheets = sheets };
    }

    /// <summary>
    /// Supplies the best recoverable dates when migrating metadata that predates
    /// per-record audit fields. The host passes the document file dates.
    /// </summary>
    public static DocumentState BackfillMissing(
        DocumentState state,
        DateTimeOffset created,
        DateTimeOffset lastModified)
    {
        ArgumentNullException.ThrowIfNull(state);

        var createdUtc = created.ToUniversalTime();
        var modifiedUtc = lastModified.ToUniversalTime();
        if (modifiedUtc < createdUtc) modifiedUtc = createdUtc;

        return state with
        {
            Folders = state.Folders.Select(folder => folder with
            {
                CreatedUtc = folder.CreatedUtc ?? createdUtc,
                LastModifiedUtc = folder.LastModifiedUtc ?? modifiedUtc,
            }).ToArray(),
            Sheets = state.Sheets.ToDictionary(pair => pair.Key, pair => pair.Value with
            {
                CreatedUtc = pair.Value.CreatedUtc ?? createdUtc,
                LastModifiedUtc = pair.Value.LastModifiedUtc ?? modifiedUtc,
            }),
        };
    }

    private static bool FolderContentEquals(FolderRecord first, FolderRecord second) =>
        first with { CreatedUtc = null, LastModifiedUtc = null } ==
        second with { CreatedUtc = null, LastModifiedUtc = null };

    private static bool SheetContentEquals(SheetRecord first, SheetRecord second) =>
        first with { CreatedUtc = null, LastModifiedUtc = null } ==
        second with { CreatedUtc = null, LastModifiedUtc = null };
}

namespace RhinoLayoutFoundry.Core.Overview;

public sealed record OverviewSelectionSummary(
    int FolderCount,
    int SheetCount,
    int DetailCount,
    int AppearanceStateCount)
{
    public int TotalCount => FolderCount + SheetCount + DetailCount +
        AppearanceStateCount;

    public string DisplayText
    {
        get
        {
            if (TotalCount == 0)
            {
                return "No selection";
            }

            var parts = new List<string>();
            AddPart(parts, FolderCount, "folder");
            AddPart(parts, SheetCount, "sheet");
            AddPart(parts, DetailCount, "detail");
            AddPart(parts, AppearanceStateCount, "appearance state");
            return string.Join(" · ", parts) + " selected";
        }
    }

    public static OverviewSelectionSummary Create(IEnumerable<OverviewNodeKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var folders = 0;
        var sheets = 0;
        var details = 0;
        var appearanceStates = 0;
        foreach (var key in keys.Distinct())
        {
            switch (key.Kind)
            {
                case OverviewNodeKind.Folder:
                    folders++;
                    break;
                case OverviewNodeKind.Sheet:
                    sheets++;
                    break;
                case OverviewNodeKind.Detail:
                    details++;
                    break;
                case OverviewNodeKind.AppearanceState:
                    appearanceStates++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(keys), key.Kind, null);
            }
        }

        return new OverviewSelectionSummary(
            folders,
            sheets,
            details,
            appearanceStates);
    }

    private static void AddPart(ICollection<string> parts, int count, string singular)
    {
        if (count > 0)
        {
            parts.Add($"{count} {singular}{(count == 1 ? string.Empty : "s")}");
        }
    }
}

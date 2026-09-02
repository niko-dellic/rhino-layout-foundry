using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Diagnostics;

namespace RhinoLayoutFoundry.Core.Naming;

public enum NamingIndexMode
{
    PreserveCurrent,
    FolderPosition,
    FolderSameStemPosition,
    GlobalPosition,
    GlobalSameStemPosition,
}

public sealed record NamingIndexCandidate(
    NamingItem Item,
    Guid FolderId,
    int Order,
    bool IsTarget,
    int? PreservedIndex = null);

public sealed record AvailableNamingPreview(
    NamingPreview Preview,
    IReadOnlyDictionary<Guid, int> Indices);

public static class NamingIndexing
{
    public static AvailableNamingPreview PreviewAvailable(
        string pattern,
        IReadOnlyList<NamingItem> items,
        IReadOnlyDictionary<Guid, int> initialIndices,
        IEnumerable<string> reservedNames)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(initialIndices);
        ArgumentNullException.ThrowIfNull(reservedNames);

        var reserved = reservedNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entries = new List<NamingPreviewEntry>(items.Count);
        var diagnostics = new List<Diagnostic>();
        var indices = new Dictionary<Guid, int>();

        foreach (var item in items)
        {
            var index = initialIndices.GetValueOrDefault(item.SheetId, 1);
            string? previousCollision = null;
            while (true)
            {
                var preview = NamingEngine.PreviewWithIndices(
                    pattern,
                    [item],
                    new Dictionary<Guid, int> { [item.SheetId] = index });
                var entry = preview.Entries[0];
                if (!preview.CanApply)
                {
                    entries.Add(entry);
                    diagnostics.AddRange(preview.Diagnostics);
                    indices[item.SheetId] = index;
                    break;
                }

                if (reserved.Add(entry.ProposedName))
                {
                    entries.Add(entry);
                    diagnostics.AddRange(preview.Diagnostics);
                    indices[item.SheetId] = index;
                    break;
                }

                if (string.Equals(previousCollision, entry.ProposedName, StringComparison.OrdinalIgnoreCase))
                {
                    entries.Add(entry);
                    diagnostics.Add(new Diagnostic(
                        "NAME_RESERVED",
                        DiagnosticSeverity.Error,
                        $"The proposed layout name '{entry.ProposedName}' already exists and the pattern cannot produce a different name.",
                        item.SheetId));
                    indices[item.SheetId] = index;
                    break;
                }

                previousCollision = entry.ProposedName;
                try
                {
                    index = checked(index + 1);
                }
                catch (OverflowException)
                {
                    entries.Add(entry);
                    diagnostics.Add(new Diagnostic(
                        "NAME_INDEX_EXHAUSTED",
                        DiagnosticSeverity.Error,
                        "No available layout index could be assigned.",
                        item.SheetId));
                    indices[item.SheetId] = index;
                    break;
                }
            }
        }

        return new AvailableNamingPreview(new NamingPreview(entries, diagnostics), indices);
    }

    public static IReadOnlyDictionary<Guid, int> Resolve(
        string pattern,
        int start,
        int step,
        NamingIndexMode mode,
        Guid rootFolderId,
        IReadOnlyDictionary<Guid, FolderRecord> folders,
        IEnumerable<NamingIndexCandidate> candidates)
    {
        var all = candidates.ToArray();
        var folderOrder = FolderPreOrder(rootFolderId, folders)
            .Select((id, index) => (id, index))
            .ToDictionary(item => item.id, item => item.index);
        var ordered = all
            .OrderBy(item => folderOrder.GetValueOrDefault(item.FolderId, int.MaxValue))
            .ThenBy(item => item.Order)
            .ThenBy(item => item.Item.CurrentName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Item.SheetId)
            .ToArray();

        var folderPositions = Positions(ordered, item => item.FolderId);
        if (mode == NamingIndexMode.PreserveCurrent)
        {
            return ordered.Where(item => item.IsTarget).ToDictionary(
                item => item.Item.SheetId,
                item => item.PreservedIndex ?? Index(start, step, folderPositions[item.Item.SheetId]));
        }

        var sameStem = mode is NamingIndexMode.FolderSameStemPosition or NamingIndexMode.GlobalSameStemPosition;
        var global = mode is NamingIndexMode.GlobalPosition or NamingIndexMode.GlobalSameStemPosition;
        var eligible = ordered.Where(item => !sameStem || item.IsTarget ||
            NamingEngine.MatchesResolvedIndexPattern(pattern, item.Item)).ToArray();
        var positions = Positions(eligible, item =>
        {
            var scope = global ? Guid.Empty : item.FolderId;
            var stem = sameStem ? NamingEngine.ResolveStem(pattern, item.Item) : string.Empty;
            return (scope, stem.ToUpperInvariant());
        });
        return ordered.Where(item => item.IsTarget).ToDictionary(
            item => item.Item.SheetId,
            item => Index(start, step, positions[item.Item.SheetId]));
    }

    private static Dictionary<Guid, int> Positions<TKey>(
        IEnumerable<NamingIndexCandidate> candidates,
        Func<NamingIndexCandidate, TKey> keySelector) where TKey : notnull
    {
        var counts = new Dictionary<TKey, int>();
        var positions = new Dictionary<Guid, int>();
        foreach (var candidate in candidates)
        {
            var key = keySelector(candidate);
            var position = counts.GetValueOrDefault(key) + 1;
            counts[key] = position;
            positions[candidate.Item.SheetId] = position;
        }
        return positions;
    }

    private static int Index(int start, int step, int position) =>
        checked(start + (position - 1) * step);

    private static IReadOnlyList<Guid> FolderPreOrder(
        Guid rootFolderId,
        IReadOnlyDictionary<Guid, FolderRecord> folders)
    {
        var result = new List<Guid>();
        var seen = new HashSet<Guid>();
        void Visit(Guid id)
        {
            if (!seen.Add(id) || !folders.ContainsKey(id)) return;
            result.Add(id);
            foreach (var child in folders.Values
                         .Where(folder => folder.ParentId == id)
                         .OrderBy(folder => folder.Order)
                         .ThenBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase))
                Visit(child.Id);
        }
        Visit(rootFolderId);
        foreach (var orphan in folders.Values
                     .Where(folder => !seen.Contains(folder.Id))
                     .OrderBy(folder => folder.Order)
                     .ThenBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase))
            Visit(orphan.Id);
        return result;
    }
}

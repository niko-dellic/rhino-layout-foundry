using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Naming;

public sealed record LinkedSheetNamingPreview(
    UpdateLinkedSheetNamesChange? Change,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool CanApply => Diagnostics.All(item => item.Severity != DiagnosticSeverity.Error);
}

/// <summary>
/// Re-evaluates persisted sheet naming bindings against a proposed hierarchy or
/// named-view state. The persisted integer is always reused, so index tokens are
/// stable for the lifetime of a binding.
/// </summary>
public static class LinkedSheetNaming
{
    public static LinkedSheetNamingPreview Preview(
        DocumentSnapshot snapshot,
        IReadOnlyDictionary<Guid, Guid>? folderOverrides = null,
        IReadOnlyDictionary<Guid, string>? folderNameOverrides = null,
        IReadOnlyDictionary<Guid, SheetNamingBinding?>? bindingOverrides = null,
        IReadOnlySet<Guid>? affectedSheetIds = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        folderOverrides ??= new Dictionary<Guid, Guid>();
        folderNameOverrides ??= new Dictionary<Guid, string>();
        bindingOverrides ??= new Dictionary<Guid, SheetNamingBinding?>();

        var diagnostics = new List<Diagnostic>();
        var expectedNames = new Dictionary<Guid, string>();
        var newNames = new Dictionary<Guid, string>();
        var newBindings = new Dictionary<Guid, SheetNamingBinding?>();

        foreach (var sheet in snapshot.Sheets.Values)
        {
            var hasOverride = bindingOverrides.TryGetValue(sheet.PageViewId, out var overriddenBinding);
            var binding = hasOverride ? overriddenBinding : sheet.NamingBinding;
            if (binding is null) continue;

            // A name changed outside the linked naming workflow is an explicit override.
            // Persist the detachment the next time this sheet participates in a source change.
            if (!hasOverride && !string.Equals(
                    sheet.Name,
                    binding.LastGeneratedName,
                    StringComparison.Ordinal))
            {
                if (affectedSheetIds is null || affectedSheetIds.Contains(sheet.PageViewId))
                    newBindings[sheet.PageViewId] = null;
                continue;
            }

            if (affectedSheetIds is not null && !affectedSheetIds.Contains(sheet.PageViewId) && !hasOverride)
                continue;

            var folderId = folderOverrides.GetValueOrDefault(sheet.PageViewId, sheet.FolderId);
            var folderName = folderId == snapshot.RootFolderId
                ? string.Empty
                : folderNameOverrides.GetValueOrDefault(
                    folderId,
                    snapshot.Folders.GetValueOrDefault(folderId)?.Name ?? string.Empty);
            var tokens = new Dictionary<string, string>(snapshot.Metadata, StringComparer.OrdinalIgnoreCase)
            {
                ["folder"] = folderName,
                ["tag"] = sheet.Tags.FirstOrDefault() ?? string.Empty,
                ["view"] = FirstAssignedView(sheet, binding),
            };
            foreach (var pair in sheet.Metadata) tokens[pair.Key] = pair.Value;

            var naming = NamingEngine.Preview(new NamingRequest(
                binding.Pattern,
                [new NamingItem(sheet.PageViewId, sheet.Name, tokens)],
                binding.Index,
                1));
            diagnostics.AddRange(naming.Diagnostics);
            var proposed = naming.Entries.Single().ProposedName;
            expectedNames[sheet.PageViewId] = sheet.Name;
            newNames[sheet.PageViewId] = proposed;
            newBindings[sheet.PageViewId] = binding with { LastGeneratedName = proposed };
        }

        if (diagnostics.All(item => item.Severity != DiagnosticSeverity.Error))
        {
            var finalNames = snapshot.Sheets.Values
                .Select(sheet => (sheet.PageViewId,
                    Name: newNames.GetValueOrDefault(sheet.PageViewId, sheet.Name)))
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1);
            foreach (var duplicate in finalNames)
            {
                foreach (var item in duplicate)
                    diagnostics.Add(new Diagnostic(
                        "linked_name.duplicate",
                        DiagnosticSeverity.Error,
                        $"The linked naming rules would duplicate layout name '{item.Name}'.",
                        item.PageViewId));
            }
        }

        var changedNames = newNames
            .Where(pair => !string.Equals(expectedNames[pair.Key], pair.Value, StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var expectedChangedNames = expectedNames
            .Where(pair => changedNames.ContainsKey(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var bindingChanges = newBindings.Where(pair =>
        {
            var current = snapshot.Sheets[pair.Key].NamingBinding;
            return current != pair.Value;
        }).ToDictionary(pair => pair.Key, pair => pair.Value);

        var change = changedNames.Count == 0 && bindingChanges.Count == 0
            ? null
            : new UpdateLinkedSheetNamesChange(expectedChangedNames, changedNames, bindingChanges);
        return new LinkedSheetNamingPreview(change, diagnostics);
    }

    public static SheetNamingBinding Attach(
        string pattern,
        int index,
        string generatedName,
        SheetSnapshot sheet)
    {
        var namedViews = sheet.Details
            .Where(detail => !string.IsNullOrWhiteSpace(detail.Name))
            .Take(1)
            .ToDictionary(detail => detail.DetailViewportId, detail => detail.Name);
        return new SheetNamingBinding(pattern.Trim(), index, generatedName, namedViews);
    }

    private static string FirstAssignedView(SheetSnapshot sheet, SheetNamingBinding binding)
    {
        foreach (var detail in sheet.Details)
            if (binding.NamedViews.TryGetValue(detail.DetailViewportId, out var name) &&
                !string.IsNullOrWhiteSpace(name))
                return name;
        return string.Empty;
    }
}

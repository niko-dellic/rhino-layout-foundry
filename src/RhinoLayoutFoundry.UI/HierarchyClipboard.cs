using System.Text.Json;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

internal static class HierarchyClipboard
{
    private const string DataType = "application/x-rhino-layout-foundry-hierarchy+json";
    private const int CurrentVersion = 1;

    internal static bool IsCopyShortcut(KeyEventArgs eventArgs) =>
        IsPrimaryShortcut(eventArgs) && eventArgs.Key == Keys.C;

    internal static bool IsPasteShortcut(KeyEventArgs eventArgs) =>
        IsPrimaryShortcut(eventArgs) && eventArgs.Key == Keys.V;

    internal static HierarchyClipboardResult CopyCurrentSelection()
    {
        var snapshot = LayoutFoundryUiHost.CaptureSnapshot();
        if (snapshot is null)
            return HierarchyClipboardResult.Failure("The active Rhino document is unavailable.");
        var selection = LayoutFoundryUiHost.Selection.Selected.ToArray();
        if (selection.Any(key => key.Kind == OverviewNodeKind.Folder && key.Id == snapshot.RootFolderId))
            return HierarchyClipboardResult.Failure("The project root cannot be copied.");

        var resolved = HierarchySelectionResolver.Resolve(snapshot, selection);
        if (resolved.UnresolvedKeys.Count > 0)
            return HierarchyClipboardResult.Failure("One or more selected items no longer exist.");
        if (resolved.SelectedItemCount == 0)
            return HierarchyClipboardResult.Failure("Select at least one folder, layout, or detail to copy.");

        var payload = new HierarchyClipboardPayload(
            CurrentVersion,
            snapshot.DocumentRuntimeSerialNumber,
            resolved.FolderRootIds,
            resolved.StandaloneSheetPageViewIds);
        try
        {
            Clipboard.Instance.Clear();
            Clipboard.Instance.SetString(JsonSerializer.Serialize(payload), DataType);
            return HierarchyClipboardResult.Success(
                $"Copied {resolved.SelectedItemCount} item{(resolved.SelectedItemCount == 1 ? string.Empty : "s")}.");
        }
        catch (Exception exception)
        {
            return HierarchyClipboardResult.Failure($"Could not copy the selection: {exception.Message}");
        }
    }

    internal static bool CanPasteCurrentDocument()
    {
        var context = LayoutFoundryUiHost.CaptureDocumentContext();
        return context is { } current &&
               TryReadPayload(out var payload) &&
               payload.DocumentRuntimeSerialNumber == current.DocumentRuntimeSerialNumber;
    }

    internal static async Task<HierarchyClipboardResult> PasteAsync(
        Guid? destinationFolderId = null,
        ObserverPointRecord? canvasTargetOrigin = null)
    {
        if (!TryReadPayload(out var payload))
            return HierarchyClipboardResult.Failure("The clipboard does not contain Foundry layouts or folders.");
        var snapshot = LayoutFoundryUiHost.CaptureSnapshot();
        if (snapshot is null)
            return HierarchyClipboardResult.Failure("The active Rhino document is unavailable.");
        if (payload.DocumentRuntimeSerialNumber != snapshot.DocumentRuntimeSerialNumber)
            return HierarchyClipboardResult.Failure("Copied items can only be pasted into the same open 3DM.");

        var destination = destinationFolderId is { } explicitFolder
            ? snapshot.Folders.ContainsKey(explicitFolder)
                ? new HierarchyPasteDestination(true, explicitFolder, string.Empty)
                : new HierarchyPasteDestination(false, null, "The paste destination no longer exists.")
            : HierarchyPasteDestination.Resolve(
                snapshot,
                LayoutFoundryUiHost.Selection.Selected,
                LayoutFoundryUiHost.Selection.Anchor);
        if (!destination.Succeeded || destination.FolderId is not { } folderId)
            return HierarchyClipboardResult.Failure(destination.Message);

        var selection = payload.FolderIds
            .Select(id => new OverviewNodeKey(OverviewNodeKind.Folder, id))
            .Concat(payload.SheetIds.Select(id => new OverviewNodeKey(OverviewNodeKind.Sheet, id)))
            .ToArray();
        var result = await LayoutFoundryUiHost.PasteSelectionAsync(
            payload.DocumentRuntimeSerialNumber,
            selection,
            folderId,
            canvasTargetOrigin);
        return result.Succeeded
            ? HierarchyClipboardResult.Success(
                $"Pasted {selection.Length} item{(selection.Length == 1 ? string.Empty : "s")}.")
            : HierarchyClipboardResult.Failure(
                string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
    }

    private static bool IsPrimaryShortcut(KeyEventArgs eventArgs)
    {
        var modifiers = eventArgs.Modifiers;
        var primary = modifiers.HasFlag(Keys.Application) || modifiers.HasFlag(Keys.Control);
        return primary && !modifiers.HasFlag(Keys.Alt) && !modifiers.HasFlag(Keys.Shift);
    }

    private static bool TryReadPayload(out HierarchyClipboardPayload payload)
    {
        payload = null!;
        try
        {
            if (!Clipboard.Instance.Contains(DataType)) return false;
            var json = Clipboard.Instance.GetString(DataType);
            var parsed = string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<HierarchyClipboardPayload>(json);
            if (parsed is null || parsed.Version != CurrentVersion ||
                parsed.DocumentRuntimeSerialNumber == 0 ||
                parsed.FolderIds.Any(id => id == Guid.Empty) ||
                parsed.SheetIds.Any(id => id == Guid.Empty) ||
                parsed.FolderIds.Count + parsed.SheetIds.Count == 0)
                return false;
            payload = parsed;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private sealed record HierarchyClipboardPayload(
        int Version,
        uint DocumentRuntimeSerialNumber,
        IReadOnlyList<Guid> FolderIds,
        IReadOnlyList<Guid> SheetIds);
}

internal sealed record HierarchyClipboardResult(bool Succeeded, string Message)
{
    internal static HierarchyClipboardResult Success(string message) => new(true, message);
    internal static HierarchyClipboardResult Failure(string message) => new(false, message);
}

using RhinoLayoutFoundry.Core.Operations;
using System.Text.Json;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

public sealed partial class LayoutFoundryPanel
{
    private const string ConversationClipboardType = "application/x-foundry-conversations+json";
    private sealed record CopiedConversation(string Name, string Payload);
    private readonly ButtonMenuItem _renameConversationMenu = new() { Text = "Rename conversation…" };
    private readonly ButtonMenuItem _moveConversationMenu = new() { Text = "Move conversation to" };
    private sealed record PendingConversationDeletion(uint Serial, Guid[] Ids, string[] Payloads);
    private PendingConversationDeletion? _pendingConversationDeletion;

    private bool HasConversationClipboard() => FoundryConversationItems.Current is not null &&
        _overview.DocumentRuntimeSerialNumber is not null && Clipboard.Instance.Contains(ConversationClipboardType);

    private bool TryCopyConversations()
    {
        var keys = SelectedKeys();
        if (!keys.Any(key => key.Kind == OverviewNodeKind.Conversation)) return false;
        if (keys.Any(key => key.Kind != OverviewNodeKind.Conversation))
        {
            _statusLabel.Text = "Copy conversations separately from layouts and folders.";
            return true;
        }
        ConversationAction((provider, serial) =>
        {
            var copies = keys.Select(key => new CopiedConversation(
                _overview.Conversations.Single(item => item.Id == key.Id).Name, provider.Read(serial, key.Id))).ToArray();
            Clipboard.Instance.Clear();
            Clipboard.Instance.SetString(JsonSerializer.Serialize(copies), ConversationClipboardType);
            _statusLabel.Text = $"Copied {copies.Length} conversation(s).";
        });
        return true;
    }

    private bool PasteConversations()
    {
        if (!HasConversationClipboard()) return false;
        ConversationAction((provider, serial) =>
        {
            var json = Clipboard.Instance.GetString(ConversationClipboardType);
            if (json.Length > 32_000_000) throw new InvalidOperationException("Conversation clipboard is too large.");
            var copies = JsonSerializer.Deserialize<CopiedConversation[]>(json) ?? [];
            var folder = ResolveCreationDestinationFolderId() ?? _overview.RootFolderId ?? throw new InvalidOperationException("No destination folder.");
            foreach (var copy in copies)
                provider.Write(serial, Guid.Empty, folder, copy.Name.Length > 110 ? copy.Name[..110] + " copy" : copy.Name + " copy", copy.Payload);
            RefreshOverview();
            _statusLabel.Text = $"Pasted {copies.Length} conversation(s). Save the Rhino model to retain them.";
        });
        return true;
    }

    private void ConversationAction(Action<FoundryConversationProvider, uint> action)
    {
        try
        {
            if (FoundryConversationItems.Current is { } provider && _overview.DocumentRuntimeSerialNumber is { } serial)
                action(provider, serial);
        }
        catch (Exception error) { _statusLabel.Text = "Conversation: " + error.Message; }
    }

    private void RenameConversation()
    {
        if (SelectedItems() is [var item] && item.Node.Key.Kind == OverviewNodeKind.Conversation)
            BeginInlineNameRename(item);
    }

    private OperationResult CommitConversationRename(Guid id, string name)
    {
        try
        {
            var provider = FoundryConversationItems.Current ?? throw new InvalidOperationException("Conversation provider unavailable.");
            var serial = _overview.DocumentRuntimeSerialNumber ?? throw new InvalidOperationException("Document unavailable.");
            var entry = _overview.Conversations.Single(item => item.Id == id);
            provider.Write(serial, id, entry.FolderId, name, provider.Read(serial, id));
            return ToOperationResult(new OverviewNavigationResult(true, string.Empty));
        }
        catch (Exception error)
        {
            return ToOperationResult(new OverviewNavigationResult(false, error.Message));
        }
    }

    private void UpdateConversationMenu()
    {
        var keys = SelectedKeys();
        var onlyConversations = keys.Length > 0 && keys.All(key => key.Kind == OverviewNodeKind.Conversation);
        _renameConversationMenu.Visible = onlyConversations && keys.Length == 1;
        _moveConversationMenu.Visible = onlyConversations;
        _moveConversationMenu.Items.Clear();
        foreach (var folder in _overview.Folders)
        {
            var target = new ButtonMenuItem { Text = folder.Name };
            target.Click += (_, _) => ConversationAction((provider, serial) =>
            {
                foreach (var key in keys)
                {
                    var entry = _overview.Conversations.Single(item => item.Id == key.Id);
                    provider.Write(serial, entry.Id, folder.Id, entry.Name, provider.Read(serial, entry.Id));
                }
                RefreshOverview();
            });
            _moveConversationMenu.Items.Add(target);
        }
        if (onlyConversations)
        {
            _setCurrentMenuItem.Visible = _setCurrentMenuItem.Enabled = keys.Length == 1;
            _setCurrentMenuItem.Text = "Go to conversation";
        }
        else _setCurrentMenuItem.Text = "Set Current";
        _pasteSelectionMenuItem.Enabled |= HasConversationClipboard();
    }
}

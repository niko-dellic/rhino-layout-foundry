using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

public partial class LayoutFoundryPanel
{
    private readonly Panel _extensionWorkspace = new() { Visible = false };
    private Control? _extensionContent;
    private readonly Panel _workspaceHost = new();
    private Control _workspaceRoot = null!;
    private Control? _layoutsHost;

    private Control CreateWorkspaceHost(Control layouts)
    {
        _layoutsHost = layouts;
        _workspaceHost.Content = _layoutsHost;
        return _workspaceRoot = new StackLayout
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new StackLayoutItem(_workspaceHost, true),
                new Panel { Padding = new Eto.Drawing.Padding(FoundryTheme.Space4, 0, FoundryTheme.Space4, FoundryTheme.Space4), Content = CreateBottomBar() },
            },
        };
    }

    /// <summary>Hosts a companion-owned view; hiding it does not dispose its session.</summary>
    private void ShowExtensionWorkspace(string title, Control content)
        => ShowExtensionWorkspaceWithToolbar(title, content, new Panel());

    private void ShowExtensionWorkspaceWithToolbar(string title, Control content, Control toolbarActions)
    {
        if (ReferenceEquals(_extensionContent, content)) { SetWorkspaceVisible(true); return; }
        if (_extensionWorkspace.Content is StackLayout previous) previous.Items.Clear();
        _extensionContent = content;
        var back = new FoundryToolbarIconButton(LayoutBrandIcon.BackToLayouts(), "Back to layouts");
        back.Click += (_, _) => SetWorkspaceVisible(false);
        _extensionWorkspace.Content = new StackLayout
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Spacing = FoundryTheme.Space2,
            Items =
            {
                new StackLayout
                {
                    Padding = new Eto.Drawing.Padding(FoundryTheme.Space4, FoundryTheme.Space4, FoundryTheme.Space4, 0),
                    Spacing = FoundryTheme.Space3,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Items =
                    {
                        CreateHeader(),
                        new StackLayout
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = FoundryTheme.Space2,
                            VerticalContentAlignment = VerticalAlignment.Center,
                            Items = { back,
                                new Panel { Width = 1, Height = 20, BackgroundColor = FoundryTheme.CanvasBorder },
                                toolbarActions, new StackLayoutItem(null, true) },
                        },
                    },
                },
                new StackLayoutItem(content, true),
            },
        };
        SetWorkspaceVisible(true);
    }

    private void SetWorkspaceVisible(bool visible)
    {
        _extensionWorkspace.Visible = true;
        _workspaceHost.Content = visible ? _extensionWorkspace : _layoutsHost;
        _statusLabel.Visible = _summaryLabel.Visible = !visible;
        if (!visible) RefreshOverview();
    }
}

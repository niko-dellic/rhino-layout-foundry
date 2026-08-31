using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

/// <summary>
/// Hosts the same capability editor used by Canvas when hierarchy or thumbnail
/// users request properties. Operations commit immediately and preserve the
/// shared stable-ID selection.
/// </summary>
internal sealed class SelectionInspectorDialog : Dialog
{
    private readonly SelectionInspectorPanel _inspector;
    private readonly IReadOnlyList<OverviewNodeKey> _selection;
    private readonly Label _status = FoundryTheme.MutedLabel();

    internal SelectionInspectorDialog(
        DocumentSnapshot snapshot,
        IReadOnlyList<OverviewNodeKey> selection,
        SelectionInspectorContent contentMode = SelectionInspectorContent.All)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _selection = selection.Distinct().ToArray();
        _inspector = new SelectionInspectorPanel(contentMode);
        Title = contentMode switch
        {
            SelectionInspectorContent.Layers => "Layout Foundry — Layer settings",
            SelectionInspectorContent.ObjectModes => "Layout Foundry — Object display modes",
            _ => "Layout Foundry — Properties",
        };
        MinimumSize = contentMode switch
        {
            SelectionInspectorContent.Layers => new Size(720, 520),
            SelectionInspectorContent.ObjectModes => new Size(760, 600),
            _ => new Size(420, 620),
        };
        Size = contentMode switch
        {
            SelectionInspectorContent.Layers => new Size(820, 660),
            SelectionInspectorContent.ObjectModes => new Size(880, 720),
            _ => new Size(460, 820),
        };
        Padding = new Padding(FoundryTheme.Space4);
        BackgroundColor = FoundryTheme.PanelBackground;

        var close = new FoundryDialogButton("Close", FoundryDialogButtonStyle.Secondary, 92);
        close.Click += (_, _) => Close();
        _inspector.SetContext(snapshot, _selection);
        _inspector.OperationCompleted += (_, eventArgs) =>
        {
            Changed |= eventArgs.Result.Succeeded;
            _status.Text = eventArgs.Result.Succeeded
                ? eventArgs.SuccessMessage
                : string.Join(" ", eventArgs.Result.Diagnostics.Select(item => item.Message));
            var refreshed = LayoutFoundryUiHost.CaptureSnapshot();
            if (refreshed is not null) _inspector.SetContext(refreshed, _selection);
        };
        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space3,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new StackLayoutItem(_inspector, true),
                _status,
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalContentAlignment = HorizontalAlignment.Right,
                    Items = { close },
                },
            },
        };
    }

    internal bool Changed { get; private set; }
}

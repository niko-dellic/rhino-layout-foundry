using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

internal sealed class LayoutTemplateOverlay : PixelLayout
{
    private readonly Panel _card;
    private readonly FoundryCheckBox _registration = new("Use as layout template");
    private IReadOnlyDictionary<OverviewNodeKey, bool> _initial = new Dictionary<OverviewNodeKey, bool>();
    private bool _changed;
    private bool _configuring;
    internal LayoutTemplateOverlay()
    {
        Visible = false;
        BackgroundColor = Colors.Transparent;
        _card = new Panel
        {
            Size = new Size(240, 44),
            Padding = FoundryTheme.Space2,
            BackgroundColor = FoundryTheme.PanelBackground,
            Content = _registration,
        };
        Add(_card, 0, 0);
        _registration.CheckedChanged += (_, _) =>
        {
            if (!_configuring)
                _changed = true;
        };
        MouseDown += (_, e) =>
        {
            if (e.Buttons.HasFlag(MouseButtons.Primary))
            {
                Dismiss(true);
                e.Handled = true;
            }
        };
        _registration.KeyDown += (_, e) =>
        {
            if (e.Key == Keys.Escape)
            {
                Dismiss(false);
                e.Handled = true;
            }
            else if (e.Key == Keys.Enter)
            {
                Dismiss(true);
                e.Handled = true;
            }
        };
    }

    internal event EventHandler<LayoutTemplateCommitEventArgs>? CommitRequested;
    internal void ShowPicker(IReadOnlyDictionary<OverviewNodeKey, bool> initial, Point anchor)
    {
        _initial = new Dictionary<OverviewNodeKey, bool>(initial);
        _configuring = true;
        var values = initial.Values.Distinct().ToArray();
        _registration.Checked = values.Length == 1 ? values[0] : null;
        _configuring = false;
        _changed = false;
        const int margin = 8;
        Move(_card, Math.Clamp(anchor.X, margin, Math.Max(margin, ClientSize.Width - _card.Width - margin)), Math.Clamp(anchor.Y + FoundryTheme.Space2, margin, Math.Max(margin, ClientSize.Height - _card.Height - margin)));
        Visible = true;
        Application.Instance.AsyncInvoke(() =>
        {
            if (Visible)
                _registration.Focus();
        });
    }

    internal void Dismiss(bool commit)
    {
        if (!Visible)
            return;
        Visible = false;
        if (commit && _changed)
            CommitRequested?.Invoke(this, new LayoutTemplateCommitEventArgs(_initial.Keys.ToDictionary(key => key, _ => _registration.Checked == true)));
    }
}

internal sealed class LayoutTemplateCommitEventArgs(IReadOnlyDictionary<OverviewNodeKey, bool> values) : EventArgs
{
    internal IReadOnlyDictionary<OverviewNodeKey, bool> Values { get; } = values;
}

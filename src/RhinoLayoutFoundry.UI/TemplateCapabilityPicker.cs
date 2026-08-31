using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.UI;

internal sealed class TemplateCapabilityPicker : Panel
{
    private static readonly (TemplateCapability Capability, string Label)[] Options =
    [
        (TemplateCapability.Layout, "Layout"),
        (TemplateCapability.TitleBlock, "Title block"),
    ];

    private readonly FoundryDialogButton _trigger;
    private readonly Dictionary<TemplateCapability, FoundryCheckBox> _checks = [];
    private Form? _popup;
    private TemplateCapability _value;
    private TemplateCapability _allowed = TemplateCapability.Layout |
        TemplateCapability.TitleBlock;
    private bool _updating;

    internal TemplateCapabilityPicker()
    {
        _trigger = new FoundryDialogButton("No template roles", FoundryDialogButtonStyle.Secondary, 260);
        _trigger.Click += (_, _) => TogglePopup();
        Content = _trigger;
        UnLoad += (_, _) => ClosePopup();
    }

    internal event EventHandler? ValueChanged;

    internal TemplateCapability Value
    {
        get => _value;
        set
        {
            _value = value & _allowed;
            UpdateControls();
        }
    }

    internal TemplateCapability Allowed
    {
        get => _allowed;
        set
        {
            _allowed = value;
            _value &= value;
            UpdateControls();
        }
    }

    private void TogglePopup()
    {
        if (_popup?.Visible == true)
        {
            _popup.Visible = false;
            return;
        }
        var popup = EnsurePopup();
        PositionPopup();
        popup.Show();
        popup.BringToFront();
    }

    private Form EnsurePopup()
    {
        if (_popup is not null) return _popup;
        var stack = new StackLayout
        {
            Padding = new Padding(FoundryTheme.Space3),
            Spacing = FoundryTheme.Space2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        foreach (var option in Options)
        {
            var check = new FoundryCheckBox(option.Label);
            var captured = option.Capability;
            check.CheckedChanged += (_, _) =>
            {
                if (_updating || check.Checked is not { } isChecked) return;
                _value = isChecked ? _value | captured : _value & ~captured;
                UpdateSummary();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            };
            _checks[captured] = check;
            stack.Items.Add(check);
        }
        var popup = new Form
        {
            Owner = ParentWindow,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Resizable = false,
            Maximizable = false,
            Minimizable = false,
            Closeable = false,
            AutoSize = false,
            BackgroundColor = FoundryTheme.CanvasBorder,
            Padding = new Padding(1),
            Content = new Panel
            {
                BackgroundColor = FoundryTheme.CanvasOverlayBackground,
                Content = stack,
            },
        };
        popup.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key != Keys.Escape) return;
            popup.Visible = false;
            _trigger.Focus();
            eventArgs.Handled = true;
        };
        popup.LostFocus += (_, _) => Application.Instance.AsyncInvoke(() =>
        {
            if (popup.Visible && !_checks.Values.Any(check => check.HasFocus)) popup.Visible = false;
        });
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_popup, popup)) _popup = null;
        };
        _popup = popup;
        UpdateControls();
        return popup;
    }

    private void UpdateControls()
    {
        _updating = true;
        foreach (var option in Options)
        {
            if (!_checks.TryGetValue(option.Capability, out var check)) continue;
            check.Enabled = _allowed.HasFlag(option.Capability);
            check.Checked = _value.HasFlag(option.Capability);
        }
        _updating = false;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var labels = Options.Where(option => _value.HasFlag(option.Capability))
            .Select(option => option.Label).ToArray();
        _trigger.Text = labels.Length switch
        {
            0 => "No template roles  ▾",
            1 => $"{labels[0]}  ▾",
            _ => $"{labels[0]} +{labels.Length - 1}  ▾",
        };
        _trigger.ToolTip = labels.Length == 0
            ? "Choose reusable template roles"
            : string.Join(", ", labels);
    }

    private void PositionPopup()
    {
        if (_popup is null) return;
        var anchor = PointToScreen(new PointF(0, Height));
        var screen = Screen.Screens.FirstOrDefault(candidate => candidate.Bounds.Contains(anchor)) ??
                     Screen.PrimaryScreen;
        var height = FoundryTheme.Space3 * 2 + Options.Length * 36;
        var width = Math.Max(260, Width);
        var work = screen.WorkingArea;
        var x = Math.Clamp((int)Math.Round(anchor.X), (int)work.Left + FoundryTheme.Space2,
            (int)work.Right - width - FoundryTheme.Space2);
        var y = (int)Math.Round(anchor.Y + FoundryTheme.Space1);
        if (y + height > work.Bottom - FoundryTheme.Space2)
            y = (int)Math.Round(PointToScreen(PointF.Empty).Y - height - FoundryTheme.Space1);
        _popup.Size = new Size(width, height);
        _popup.Location = new Point(x, Math.Max((int)work.Top + FoundryTheme.Space2, y));
    }

    private void ClosePopup()
    {
        if (_popup is null) return;
        var popup = _popup;
        _popup = null;
        popup.Close();
    }
}

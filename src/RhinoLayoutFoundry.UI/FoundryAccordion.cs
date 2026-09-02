using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

/// <summary>
/// Restrained, full-row accordion for dense Foundry configuration panes.
/// Sections expand independently so related form areas can remain visible together.
/// </summary>
internal sealed class FoundryAccordion : Panel
{
    internal FoundryAccordion(params FoundryAccordionItem[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var stack = new StackLayout
        {
            Spacing = 0,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        foreach (var item in items) stack.Items.Add(item);
        Content = stack;
    }
}

internal sealed class FoundryAccordionItem : Panel
{
    private readonly Panel _contentHost;
    private readonly FoundryAccordionTrigger _trigger;
    private bool _isExpanded;

    internal FoundryAccordionItem(string title, Control content, bool isExpanded = false)
    {
        ArgumentNullException.ThrowIfNull(content);
        _isExpanded = isExpanded;
        _trigger = new FoundryAccordionTrigger(title, isExpanded);
        _trigger.Activated += (_, _) => IsExpanded = !IsExpanded;
        _contentHost = new Panel
        {
            Padding = new Padding(FoundryTheme.Space2, 0, FoundryTheme.Space2, FoundryTheme.Space3),
            Content = content,
            Visible = isExpanded,
        };
        Content = new StackLayout
        {
            Spacing = 0,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                _trigger,
                _contentHost,
                new Panel { Height = 1, BackgroundColor = FoundryTheme.CanvasBorder },
            },
        };
    }

    internal bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            _contentHost.Visible = value;
            _trigger.IsExpanded = value;
            Parent?.Invalidate();
        }
    }
}

internal sealed class FoundryAccordionTrigger : Drawable
{
    private readonly Font _font = SystemFonts.Bold(13);
    private readonly string _title;
    private bool _isExpanded;
    private bool _hovered;
    private bool _pressed;
    private bool _showFocusRing;

    internal FoundryAccordionTrigger(string title, bool isExpanded) : base(true)
    {
        _title = title ?? string.Empty;
        _isExpanded = isExpanded;
        Height = 44;
        MinimumSize = new Size(120, 44);
        BackgroundColor = Colors.Transparent;
        CanFocus = true;
        ToolTip = $"{(isExpanded ? "Collapse" : "Expand")} {_title}";

        Paint += OnPaint;
        MouseEnter += (_, _) =>
        {
            if (!Enabled) return;
            _hovered = true;
            Invalidate();
        };
        MouseLeave += (_, _) =>
        {
            _hovered = false;
            _pressed = false;
            Invalidate();
        };
        MouseDown += (_, eventArgs) =>
        {
            if (!Enabled || !eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
            _pressed = true;
            _showFocusRing = false;
            Focus();
            eventArgs.Handled = true;
            Invalidate();
        };
        MouseUp += (_, eventArgs) =>
        {
            if (!Enabled || !_pressed) return;
            _pressed = false;
            Activate();
            eventArgs.Handled = true;
        };
        KeyDown += (_, eventArgs) =>
        {
            if (!Enabled || eventArgs.Key is not (Keys.Enter or Keys.Space)) return;
            Activate();
            eventArgs.Handled = true;
        };
        GotFocus += (_, _) =>
        {
            if (!_pressed) _showFocusRing = true;
            Invalidate();
        };
        LostFocus += (_, _) =>
        {
            _pressed = false;
            _showFocusRing = false;
            Invalidate();
        };
        EnabledChanged += (_, _) =>
        {
            if (!Enabled)
            {
                _hovered = false;
                _pressed = false;
                _showFocusRing = false;
            }
            Invalidate();
        };
    }

    internal event EventHandler? Activated;

    internal bool IsExpanded
    {
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            ToolTip = $"{(value ? "Collapse" : "Expand")} {_title}";
            Invalidate();
        }
    }

    private void Activate()
    {
        Activated?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void OnPaint(object? sender, PaintEventArgs eventArgs)
    {
        if (Enabled && (_hovered || _pressed))
            eventArgs.Graphics.FillRectangle(
                FoundryTheme.WithAlpha(FoundryTheme.CanvasSubtleSurface, _pressed ? 165 : 105),
                0,
                0,
                Width,
                Height);

        var textColor = Enabled ? FoundryTheme.PrimaryText : FoundryTheme.MutedText;
        var titleSize = eventArgs.Graphics.MeasureString(_font, _title);
        eventArgs.Graphics.DrawText(
            _font,
            textColor,
            FoundryTheme.Space2,
            (Height - titleSize.Height) / 2f,
            _title);

        var centerX = Width - FoundryTheme.Space3 - 6;
        var centerY = Height / 2f;
        var chevron = new Pen(FoundryTheme.WithAlpha(textColor, Enabled ? 220 : 90), 1.1f);
        if (_isExpanded)
        {
            eventArgs.Graphics.DrawLine(chevron, centerX - 4, centerY - 2, centerX, centerY + 2);
            eventArgs.Graphics.DrawLine(chevron, centerX, centerY + 2, centerX + 4, centerY - 2);
        }
        else
        {
            eventArgs.Graphics.DrawLine(chevron, centerX - 2, centerY - 4, centerX + 2, centerY);
            eventArgs.Graphics.DrawLine(chevron, centerX + 2, centerY, centerX - 2, centerY + 4);
        }

        if (_showFocusRing && Enabled)
        {
            using var focus = GraphicsPath.GetRoundRect(
                new RectangleF(1.5f, 3.5f, Math.Max(0, Width - 3), Height - 7),
                5);
            eventArgs.Graphics.DrawPath(
                new Pen(FoundryTheme.WithAlpha(FoundryTheme.PrimaryText, 100), 1),
                focus);
        }
    }
}

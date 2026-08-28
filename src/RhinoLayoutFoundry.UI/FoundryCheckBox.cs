using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

internal sealed class FoundryCheckBox : Drawable
{
    private readonly Font _font = SystemFonts.Default();
    private string _text;
    private bool? _checked;
    private bool _hovered;
    private bool _showFocusRing;

    internal FoundryCheckBox(string text, bool? isChecked = false) : base(true)
    {
        _text = text ?? string.Empty;
        _checked = isChecked;
        Height = 24;
        UpdateWidth();
        BackgroundColor = Colors.Transparent;
        CanFocus = true;
        Paint += OnPaint;
        MouseEnter += (_, _) => { _hovered = true; Invalidate(); };
        MouseLeave += (_, _) => { _hovered = false; Invalidate(); };
        MouseDown += (_, eventArgs) =>
        {
            if (!Enabled || !eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
            Focus();
            Toggle();
            eventArgs.Handled = true;
        };
        KeyDown += (_, eventArgs) =>
        {
            if (!Enabled || eventArgs.Key != Keys.Space) return;
            Toggle();
            eventArgs.Handled = true;
        };
        GotFocus += (_, _) => { _showFocusRing = true; Invalidate(); };
        LostFocus += (_, _) => { _showFocusRing = false; Invalidate(); };
        EnabledChanged += (_, _) => Invalidate();
    }

    internal event EventHandler? CheckedChanged;

    internal string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            UpdateWidth();
            Invalidate();
        }
    }

    internal bool? Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            CheckedChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    private void Toggle() => Checked = Checked != true;

    private void UpdateWidth()
    {
        Width = Math.Max(24, (int)Math.Ceiling((_text.Length * 7.2) + 30));
        MinimumSize = new Size(Width, 24);
    }

    private void OnPaint(object? sender, PaintEventArgs eventArgs)
    {
        var box = new RectangleF(1.5f, 4.5f, 15, 15);
        using var outline = GraphicsPath.GetRoundRect(box, 4);
        var active = Checked == true;
        if (active)
            eventArgs.Graphics.FillPath(FoundryTheme.WithAlpha(FoundryTheme.PrimaryText, Enabled ? 230 : 90), outline);
        else if (_hovered && Enabled)
            eventArgs.Graphics.FillPath(FoundryTheme.WithAlpha(FoundryTheme.CanvasSubtleSurface, 145), outline);
        eventArgs.Graphics.DrawPath(
            new Pen(FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, Enabled ? 210 : 75), 1),
            outline);

        if (active)
        {
            var checkColor = FoundryTheme.IsDarkMode
                ? Color.FromArgb(24, 24, 27, 255)
                : Color.FromArgb(250, 250, 250, 255);
            var pen = new Pen(checkColor, 1.5f);
            eventArgs.Graphics.DrawLine(pen, 5, 12, 8, 15);
            eventArgs.Graphics.DrawLine(pen, 8, 15, 13, 9);
        }

        if (_showFocusRing && Enabled)
        {
            using var focus = GraphicsPath.GetRoundRect(new RectangleF(0.5f, 3.5f, 17, 17), 5);
            eventArgs.Graphics.DrawPath(
                new Pen(FoundryTheme.WithAlpha(FoundryTheme.PrimaryText, 100), 1),
                focus);
        }

        var color = Enabled ? FoundryTheme.PrimaryText : FoundryTheme.MutedText;
        var textSize = eventArgs.Graphics.MeasureString(_font, _text);
        eventArgs.Graphics.DrawText(_font, color, 24, (Height - textSize.Height) / 2f, _text);
    }
}

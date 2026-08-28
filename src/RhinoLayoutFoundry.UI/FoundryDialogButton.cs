using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

internal enum FoundryDialogButtonStyle
{
    Secondary,
    Primary,
    Destructive,
}

/// <summary>
/// Quiet-outline dialog and inline action with shared mouse, keyboard, focus,
/// disabled, and destructive states.
/// </summary>
internal sealed class FoundryDialogButton : Drawable
{
    private readonly FoundryDialogButtonStyle _style;
    private readonly Font _font = SystemFonts.Bold(9);
    private string _text;
    private bool _hovered;
    private bool _pressed;
    private bool _showFocusRing;

    internal FoundryDialogButton(
        string text,
        FoundryDialogButtonStyle style,
        int width = 92)
        : base(true)
    {
        _text = text;
        _style = style;
        Size = new Size(width, 32);
        BackgroundColor = Colors.Transparent;
        CanFocus = true;

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
            Click?.Invoke(this, EventArgs.Empty);
            eventArgs.Handled = true;
            Invalidate();
        };
        KeyDown += (_, eventArgs) =>
        {
            if (!Enabled || eventArgs.Key is not (Keys.Enter or Keys.Space)) return;
            Click?.Invoke(this, EventArgs.Empty);
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

    internal event EventHandler? Click;

    internal void PerformClick()
    {
        if (!Enabled) return;
        Click?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    internal string Text
    {
        get => _text;
        set
        {
            _text = value;
            Invalidate();
        }
    }

    private void OnPaint(object? sender, PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        var bounds = new RectangleF(0.5f, 0.5f, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        using var outline = GraphicsPath.GetRoundRect(bounds, 6);
        var destructive = _style == FoundryDialogButtonStyle.Destructive;
        if (Enabled && (_hovered || _pressed))
        {
            var fill = destructive
                ? FoundryTheme.WithAlpha(FoundryTheme.DangerAccent, _pressed ? 42 : 24)
                : FoundryTheme.WithAlpha(FoundryTheme.CanvasSubtleSurface, _pressed ? 205 : 135);
            graphics.FillPath(fill, outline);
        }

        var border = destructive
            ? FoundryTheme.WithAlpha(FoundryTheme.DangerAccent, Enabled ? 175 : 72)
            : FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, Enabled ? 205 : 80);
        graphics.DrawPath(new Pen(border, 1), outline);

        var textColor = !Enabled
            ? FoundryTheme.MutedText
            : destructive
                ? FoundryTheme.DangerAccent
                : FoundryTheme.PrimaryText;
        var textSize = graphics.MeasureString(_font, _text);
        graphics.DrawText(
            _font,
            textColor,
            (Width - textSize.Width) / 2f,
            (Height - textSize.Height) / 2f,
            _text);

        if (Enabled && HasFocus && _showFocusRing)
        {
            using var focus = GraphicsPath.GetRoundRect(
                new RectangleF(2.5f, 2.5f, Math.Max(0, Width - 5), Math.Max(0, Height - 5)),
                4);
            graphics.DrawPath(
                new Pen(FoundryTheme.WithAlpha(destructive ? FoundryTheme.DangerAccent : FoundryTheme.PrimaryText, 165), 1),
                focus);
        }
    }
}

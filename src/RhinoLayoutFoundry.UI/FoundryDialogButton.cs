using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

internal enum FoundryDialogButtonStyle
{
    Secondary,
    Destructive,
}

/// <summary>
/// Flat dialog action used where native raised buttons would conflict with the
/// lightweight Foundry chrome. It retains mouse, keyboard and focus behavior.
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
        if (_style == FoundryDialogButtonStyle.Destructive)
        {
            graphics.FillRectangle(
                Enabled
                    ? FoundryTheme.DangerAccent
                    : FoundryTheme.WithAlpha(FoundryTheme.DangerAccent, 80),
                bounds);
            if (Enabled && (_hovered || _pressed))
            {
                graphics.FillRectangle(
                    FoundryTheme.WithAlpha(_pressed ? Colors.Black : Colors.White, 28),
                    bounds);
            }
        }
        else
        {
            if (Enabled && (_hovered || _pressed))
            {
                graphics.FillRectangle(
                    FoundryTheme.WithAlpha(
                        FoundryTheme.CanvasSubtleSurface,
                        _pressed ? 190 : 120),
                    bounds);
            }

            graphics.DrawRectangle(
                new Pen(FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, Enabled ? 190 : 85), 1),
                bounds);
        }

        var textColor = !Enabled
            ? FoundryTheme.MutedText
            : _style == FoundryDialogButtonStyle.Destructive
                ? Colors.White
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
            graphics.DrawRectangle(
                new Pen(
                    _style == FoundryDialogButtonStyle.Destructive
                        ? FoundryTheme.WithAlpha(Colors.White, 190)
                        : FoundryTheme.SelectionAccent,
                    1),
                2.5f,
                2.5f,
                Math.Max(0, Width - 5),
                Math.Max(0, Height - 5));
        }
    }
}

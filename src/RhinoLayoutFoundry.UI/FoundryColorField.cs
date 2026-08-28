using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

/// <summary>
/// Shadcn-style color input. Foundry owns the compact trigger chrome while the
/// platform color dialog remains native.
/// </summary>
internal sealed class FoundryColorField : Drawable
{
    private readonly ColorDialog _dialog;
    private readonly Font _font = SystemFonts.Default(9);
    private Color _value;
    private bool _hovered;
    private bool _pressed;
    private bool _showFocusRing;

    internal FoundryColorField(Color value, int width = 170)
        : base(true)
    {
        _value = Opaque(value);
        Size = new Size(width, 32);
        BackgroundColor = Colors.Transparent;
        CanFocus = true;
        ToolTip = "Choose the canvas grid color";

        _dialog = new ColorDialog
        {
            AllowAlpha = false,
            Color = _value,
        };
        _dialog.ColorChanged += (_, _) => Value = _dialog.Color;

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
            ShowPicker();
            eventArgs.Handled = true;
            Invalidate();
        };
        KeyDown += (_, eventArgs) =>
        {
            if (!Enabled || eventArgs.Key is not (Keys.Enter or Keys.Space)) return;
            ShowPicker();
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

    internal event EventHandler? ValueChanged;

    internal Color Value
    {
        get => _value;
        set
        {
            var opaque = Opaque(value);
            if (SameRgb(_value, opaque)) return;
            _value = opaque;
            ValueChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    private void ShowPicker()
    {
        _dialog.Color = _value;
        _dialog.ShowDialog(this);
    }

    private void OnPaint(object? sender, PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        var bounds = new RectangleF(0.5f, 0.5f, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        using var outline = GraphicsPath.GetRoundRect(bounds, 6);
        graphics.FillPath(
            Enabled
                ? FoundryTheme.InputBackground
                : FoundryTheme.WithAlpha(FoundryTheme.CanvasSubtleSurface, 85),
            outline);
        if (Enabled && (_hovered || _pressed))
        {
            graphics.FillPath(
                FoundryTheme.WithAlpha(FoundryTheme.CanvasSubtleSurface, _pressed ? 170 : 90),
                outline);
        }

        var border = !Enabled
            ? FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 75)
            : HasFocus
                ? FoundryTheme.WithAlpha(FoundryTheme.PrimaryText, 185)
                : _hovered
                    ? FoundryTheme.WithAlpha(FoundryTheme.SecondaryText, 190)
                    : FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 190);
        graphics.DrawPath(new Pen(border, HasFocus ? 1.5f : 1), outline);

        var swatchBounds = new RectangleF(7.5f, 6.5f, 19, 19);
        using (var swatch = GraphicsPath.GetRoundRect(swatchBounds, 4))
        {
            graphics.FillPath(_value, swatch);
            graphics.DrawPath(new Pen(FoundryTheme.WithAlpha(FoundryTheme.PrimaryText, 110), 1), swatch);
        }

        var valueText = $"#{_value.Rb:X2}{_value.Gb:X2}{_value.Bb:X2}";
        var textSize = graphics.MeasureString(_font, valueText);
        graphics.DrawText(
            _font,
            Enabled ? FoundryTheme.PrimaryText : FoundryTheme.MutedText,
            35,
            (Height - textSize.Height) / 2f,
            valueText);

        using var chevronPen = new Pen(
            Enabled ? FoundryTheme.SecondaryText : FoundryTheme.MutedText,
            1.25f);
        var chevronX = Width - 15f;
        var chevronY = Height / 2f - 1;
        graphics.DrawLine(chevronPen, chevronX - 3, chevronY, chevronX, chevronY + 3);
        graphics.DrawLine(chevronPen, chevronX, chevronY + 3, chevronX + 3, chevronY);

        if (!Enabled || !HasFocus || !_showFocusRing) return;
        using var focus = GraphicsPath.GetRoundRect(
            new RectangleF(2.5f, 2.5f, Math.Max(0, Width - 5), Math.Max(0, Height - 5)),
            4);
        graphics.DrawPath(
            new Pen(FoundryTheme.WithAlpha(FoundryTheme.PrimaryText, 70), 1),
            focus);
    }

    private static bool SameRgb(Color left, Color right) =>
        left.Rb == right.Rb && left.Gb == right.Gb && left.Bb == right.Bb;

    private static Color Opaque(Color color) => Color.FromArgb(color.Rb, color.Gb, color.Bb, 255);
}

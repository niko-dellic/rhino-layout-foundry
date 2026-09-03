using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

/// <summary>
/// Compact removable value used by Foundry multi-select fields.
/// </summary>
internal sealed class FoundryRemovableBadge : Drawable
{
    private const int BadgeHeight = 24;
    private const int MaximumWidth = 280;
    private readonly Font _font = SystemFonts.Default(10);
    private readonly string _displayText;
    private bool _hovered;
    private bool _pressed;
    private bool _showFocusRing;

    internal FoundryRemovableBadge(string text)
        : base(true)
    {
        Text = text;
        _displayText = FitText(text, MaximumWidth - 34);
        var width = Math.Clamp((int)Math.Ceiling(_font.MeasureString(_displayText).Width) + 34, 48, MaximumWidth);
        Size = new Size(width, BadgeHeight);
        MinimumSize = Size;
        BackgroundColor = Colors.Transparent;
        CanFocus = true;
        ToolTip = $"Remove {text}";

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

    internal string Text { get; }

    private string FitText(string text, float maximumTextWidth)
    {
        if (_font.MeasureString(text).Width <= maximumTextWidth) return text;
        var candidate = text;
        while (candidate.Length > 1 && _font.MeasureString(candidate + "…").Width > maximumTextWidth)
            candidate = candidate[..^1];
        return candidate + "…";
    }

    private void OnPaint(object? sender, PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        var bounds = new RectangleF(0.5f, 0.5f, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        using var outline = GraphicsPath.GetRoundRect(bounds, 6);
        graphics.FillPath(
            FoundryTheme.WithAlpha(
                _pressed ? FoundryTheme.CanvasSubtleSurface : FoundryTheme.ToolbarGroupBackground,
                Enabled ? (_hovered ? 235 : 205) : 105),
            outline);
        graphics.DrawPath(
            new Pen(FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, Enabled ? 175 : 70), 1),
            outline);

        var textColor = Enabled ? FoundryTheme.PrimaryText : FoundryTheme.MutedText;
        var textSize = graphics.MeasureString(_font, _displayText);
        graphics.DrawText(_font, textColor, 8, (Height - textSize.Height) / 2f, _displayText);

        var closeColor = Enabled
            ? FoundryTheme.WithAlpha(FoundryTheme.SecondaryText, _hovered ? 255 : 205)
            : FoundryTheme.WithAlpha(FoundryTheme.MutedText, 115);
        using var closePen = new Pen(closeColor, 1.4f);
        var closeX = Width - 12f;
        var closeY = Height / 2f;
        graphics.DrawLine(closePen, closeX - 3, closeY - 3, closeX + 3, closeY + 3);
        graphics.DrawLine(closePen, closeX + 3, closeY - 3, closeX - 3, closeY + 3);

        if (Enabled && HasFocus && _showFocusRing)
        {
            using var focus = GraphicsPath.GetRoundRect(
                new RectangleF(2.5f, 2.5f, Math.Max(0, Width - 5), Math.Max(0, Height - 5)),
                4);
            graphics.DrawPath(new Pen(FoundryTheme.WithAlpha(FoundryTheme.PrimaryText, 155), 1), focus);
        }
    }
}

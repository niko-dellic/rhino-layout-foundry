using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

/// <summary>
/// Compact neutral slider for Foundry-owned surfaces. It preserves pointer and
/// keyboard input without inheriting platform-specific tick and fill styling.
/// </summary>
internal sealed class FoundrySlider : Drawable
{
    private const float TrackInset = 8;
    private readonly int _minimum;
    private readonly int _maximum;
    private readonly Func<int, string> _toolTipFormatter;
    private readonly bool _drawFocusRing;
    private int _value;
    private bool _dragging;
    private bool _hovered;
    private bool _showFocusRing;

    internal FoundrySlider(
        int minimum,
        int maximum,
        int value,
        int width = 170,
        Func<int, string>? toolTipFormatter = null,
        bool drawFocusRing = true)
        : base(true)
    {
        if (maximum <= minimum)
            throw new ArgumentOutOfRangeException(nameof(maximum), "Maximum must exceed minimum.");

        _minimum = minimum;
        _maximum = maximum;
        _toolTipFormatter = toolTipFormatter ?? (current => $"{current}% opacity");
        _drawFocusRing = drawFocusRing;
        _value = Math.Clamp(value, minimum, maximum);
        Size = new Size(width, 32);
        BackgroundColor = Colors.Transparent;
        CanFocus = true;
        UpdateToolTip();

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
            Invalidate();
        };
        MouseDown += (_, eventArgs) =>
        {
            if (!Enabled || !eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
            _dragging = true;
            _showFocusRing = false;
            Focus();
            SetValueFromPointer(eventArgs.Location.X);
            eventArgs.Handled = true;
        };
        MouseMove += (_, eventArgs) =>
        {
            if (!_dragging || !Enabled) return;
            SetValueFromPointer(eventArgs.Location.X);
            eventArgs.Handled = true;
        };
        MouseUp += (_, eventArgs) =>
        {
            if (!_dragging) return;
            _dragging = false;
            SetValueFromPointer(eventArgs.Location.X);
            eventArgs.Handled = true;
        };
        KeyDown += OnKeyDown;
        GotFocus += (_, _) =>
        {
            if (!_dragging) _showFocusRing = true;
            Invalidate();
        };
        LostFocus += (_, _) =>
        {
            _dragging = false;
            _showFocusRing = false;
            Invalidate();
        };
        EnabledChanged += (_, _) =>
        {
            if (!Enabled)
            {
                _dragging = false;
                _hovered = false;
                _showFocusRing = false;
            }

            Invalidate();
        };
    }

    internal event EventHandler? ValueChanged;

    internal int Value
    {
        get => _value;
        set => SetValue(value);
    }

    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (!Enabled) return;
        var next = eventArgs.Key switch
        {
            Keys.Left or Keys.Down => _value - 1,
            Keys.Right or Keys.Up => _value + 1,
            Keys.PageDown => _value - 10,
            Keys.PageUp => _value + 10,
            Keys.Home => _minimum,
            Keys.End => _maximum,
            _ => _value,
        };
        if (next == _value && eventArgs.Key is not (Keys.Home or Keys.End)) return;
        SetValue(next);
        eventArgs.Handled = true;
    }

    private void SetValueFromPointer(float x)
    {
        var trackWidth = Math.Max(1, Width - (TrackInset * 2));
        var ratio = Math.Clamp((x - TrackInset) / trackWidth, 0, 1);
        SetValue(_minimum + (int)Math.Round(ratio * (_maximum - _minimum)));
    }

    private void SetValue(int value)
    {
        var clamped = Math.Clamp(value, _minimum, _maximum);
        if (_value == clamped) return;
        _value = clamped;
        UpdateToolTip();
        ValueChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void UpdateToolTip() => ToolTip = _toolTipFormatter(_value);

    private void OnPaint(object? sender, PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        var trackWidth = Math.Max(1, Width - (TrackInset * 2));
        var track = new RectangleF(TrackInset, Height / 2f - 2, trackWidth, 4);
        using (var trackPath = GraphicsPath.GetRoundRect(track, 2))
        {
            graphics.FillPath(
                Enabled
                    ? FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 135)
                    : FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 65),
                trackPath);
        }

        var ratio = (_value - _minimum) / (double)(_maximum - _minimum);
        var thumbX = TrackInset + (float)(ratio * trackWidth);
        if (thumbX > TrackInset)
        {
            var activeTrack = new RectangleF(TrackInset, Height / 2f - 2, thumbX - TrackInset, 4);
            using var activePath = GraphicsPath.GetRoundRect(activeTrack, 2);
            graphics.FillPath(
                Enabled ? FoundryTheme.SecondaryText : FoundryTheme.MutedText,
                activePath);
        }

        var thumb = new RectangleF(thumbX - 7, Height / 2f - 7, 14, 14);
        graphics.FillEllipse(
            Enabled ? FoundryTheme.InputBackground : FoundryTheme.CanvasSubtleSurface,
            thumb);
        graphics.DrawEllipse(
            new Pen(
                Enabled
                    ? (_dragging || _hovered ? FoundryTheme.PrimaryText : FoundryTheme.SecondaryText)
                    : FoundryTheme.MutedText,
                _dragging ? 1.5f : 1),
            thumb);

        if (!_drawFocusRing || !Enabled || !HasFocus || !_showFocusRing) return;
        using var focus = GraphicsPath.GetRoundRect(
            new RectangleF(1.5f, 3.5f, Math.Max(0, Width - 3), Math.Max(0, Height - 7)),
            6);
        graphics.DrawPath(
            new Pen(FoundryTheme.WithAlpha(FoundryTheme.PrimaryText, 70), 1),
            focus);
    }
}

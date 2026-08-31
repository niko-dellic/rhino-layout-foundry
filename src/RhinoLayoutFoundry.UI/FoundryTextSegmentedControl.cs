using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

/// <summary>Compact text segments for mutually exclusive Foundry modes.</summary>
internal sealed class FoundryTextSegmentedControl : Drawable
{
    private const int PaddingSize = 3;
    private const int SegmentHeight = 26;
    private readonly string[] _labels;
    private readonly int _segmentWidth;
    private readonly string? _leadingLabel;
    private readonly int _leadingLabelWidth;
    private readonly Font _font = SystemFonts.Bold(9);
    private int _selectedIndex;
    private int _hoveredIndex = -1;
    private int _pressedIndex = -1;
    private bool _showFocusRing;

    internal FoundryTextSegmentedControl(
        IReadOnlyList<string> labels,
        int selectedIndex = 0,
        int segmentWidth = 72,
        string? leadingLabel = null,
        int leadingLabelWidth = 0)
        : base(true)
    {
        ArgumentNullException.ThrowIfNull(labels);
        if (labels.Count == 0) throw new ArgumentException("At least one segment is required.", nameof(labels));
        _labels = labels.ToArray();
        _segmentWidth = Math.Max(52, segmentWidth);
        _leadingLabel = string.IsNullOrWhiteSpace(leadingLabel) ? null : leadingLabel.Trim();
        _leadingLabelWidth = _leadingLabel is null ? 0 : Math.Max(52, leadingLabelWidth);
        _selectedIndex = Math.Clamp(selectedIndex, 0, _labels.Length - 1);
        Size = new Size(
            PaddingSize * 2 + _leadingLabelWidth + _segmentWidth * _labels.Length,
            SegmentHeight + PaddingSize * 2);
        MinimumSize = Size;
        BackgroundColor = Colors.Transparent;
        CanFocus = true;
        Paint += OnPaint;
        MouseMove += (_, eventArgs) =>
        {
            var index = HitTest(eventArgs.Location);
            if (_hoveredIndex == index) return;
            _hoveredIndex = index;
            Invalidate();
        };
        MouseLeave += (_, _) =>
        {
            _hoveredIndex = -1;
            _pressedIndex = -1;
            Invalidate();
        };
        MouseDown += (_, eventArgs) =>
        {
            if (!Enabled || !eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
            _pressedIndex = HitTest(eventArgs.Location);
            _showFocusRing = false;
            Focus();
            eventArgs.Handled = _pressedIndex >= 0;
            Invalidate();
        };
        MouseUp += (_, eventArgs) =>
        {
            if (!Enabled || _pressedIndex < 0) return;
            var index = HitTest(eventArgs.Location);
            _pressedIndex = -1;
            if (index >= 0) SelectedIndex = index;
            eventArgs.Handled = true;
            Invalidate();
        };
        KeyDown += (_, eventArgs) =>
        {
            if (!Enabled) return;
            var target = eventArgs.Key switch
            {
                Keys.Left => (_selectedIndex - 1 + _labels.Length) % _labels.Length,
                Keys.Right => (_selectedIndex + 1) % _labels.Length,
                Keys.Home => 0,
                Keys.End => _labels.Length - 1,
                _ => -1,
            };
            if (target < 0) return;
            SelectedIndex = target;
            eventArgs.Handled = true;
        };
        GotFocus += (_, _) => { _showFocusRing = true; Invalidate(); };
        LostFocus += (_, _) => { _showFocusRing = false; _pressedIndex = -1; Invalidate(); };
        EnabledChanged += (_, _) => Invalidate();
    }

    internal event EventHandler? SelectedIndexChanged;

    internal int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var next = Math.Clamp(value, 0, _labels.Length - 1);
            if (_selectedIndex == next) return;
            _selectedIndex = next;
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    private int HitTest(PointF point)
    {
        if (point.Y < PaddingSize || point.Y > PaddingSize + SegmentHeight) return -1;
        var segmentStart = PaddingSize + _leadingLabelWidth;
        var index = (int)((point.X - segmentStart) / _segmentWidth);
        return point.X >= segmentStart && index >= 0 && index < _labels.Length ? index : -1;
    }

    private void OnPaint(object? sender, PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        using var capsule = GraphicsPath.GetRoundRect(
            new RectangleF(0.5f, 0.5f, Math.Max(0, Width - 1), Math.Max(0, Height - 1)), 8);
        graphics.FillPath(FoundryTheme.ToolbarGroupBackground, capsule);
        graphics.DrawPath(new Pen(FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 75), 1), capsule);
        if (_leadingLabel is not null)
        {
            var labelBounds = new RectangleF(PaddingSize, PaddingSize, _leadingLabelWidth, SegmentHeight);
            var labelSize = graphics.MeasureString(_font, _leadingLabel);
            graphics.DrawText(
                _font,
                Enabled ? FoundryTheme.MutedText : FoundryTheme.WithAlpha(FoundryTheme.MutedText, 145),
                labelBounds.X + (labelBounds.Width - labelSize.Width) / 2,
                labelBounds.Y + (labelBounds.Height - labelSize.Height) / 2,
                _leadingLabel);
        }
        for (var index = 0; index < _labels.Length; index++)
        {
            var bounds = new RectangleF(PaddingSize + _leadingLabelWidth + index * _segmentWidth + 0.5f,
                PaddingSize + 0.5f,
                _segmentWidth - 1, SegmentHeight - 1);
            using var segment = GraphicsPath.GetRoundRect(bounds, 6);
            if (index == _selectedIndex)
            {
                graphics.FillPath(FoundryTheme.ToolbarActiveBackground, segment);
                graphics.DrawPath(new Pen(FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 220), 1), segment);
            }
            else if (Enabled && (index == _hoveredIndex || index == _pressedIndex))
            {
                graphics.FillPath(FoundryTheme.WithAlpha(FoundryTheme.CanvasSubtleSurface,
                    index == _pressedIndex ? 190 : 120), segment);
            }
            var color = Enabled ? FoundryTheme.PrimaryText : FoundryTheme.MutedText;
            var size = graphics.MeasureString(_font, _labels[index]);
            graphics.DrawText(_font, color,
                bounds.X + (bounds.Width - size.Width) / 2,
                bounds.Y + (bounds.Height - size.Height) / 2,
                _labels[index]);
        }
        if (Enabled && HasFocus && _showFocusRing)
        {
            var bounds = new RectangleF(PaddingSize + _leadingLabelWidth + _selectedIndex * _segmentWidth + 2.5f,
                PaddingSize + 2.5f, _segmentWidth - 5, SegmentHeight - 5);
            using var focus = GraphicsPath.GetRoundRect(bounds, 4);
            graphics.DrawPath(new Pen(FoundryTheme.WithAlpha(FoundryTheme.PrimaryText, 150), 1), focus);
        }
    }
}

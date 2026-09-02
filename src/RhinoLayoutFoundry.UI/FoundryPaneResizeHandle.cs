using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

/// <summary>
/// Lightweight Foundry-owned pane divider. It reports bounded drag deltas to
/// its owner instead of participating in native splitter constraint solving.
/// </summary>
internal sealed class FoundryPaneResizeHandle : Drawable
{
    private const int HandleThickness = 12;
    private const int KeyboardStep = 16;
    private readonly FoundryPaneResizeAxis _axis;
    private bool _dragging;
    private bool _hovered;
    private bool _showFocusRing;
    private PointF _lastScreenPoint;
    private bool _isCollapsed;

    internal FoundryPaneResizeHandle(
        FoundryPaneResizeAxis axis,
        string leadingPaneName) : base(true)
    {
        _axis = axis;
        LeadingPaneName = leadingPaneName;
        BackgroundColor = Colors.Transparent;
        CanFocus = true;
        Cursor = axis == FoundryPaneResizeAxis.Horizontal
            ? Cursors.VerticalSplit
            : Cursors.HorizontalSplit;

        if (axis == FoundryPaneResizeAxis.Horizontal)
        {
            Width = HandleThickness;
            MinimumSize = new Size(HandleThickness, 44);
        }
        else
        {
            Height = HandleThickness;
            MinimumSize = new Size(44, HandleThickness);
        }

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
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseDoubleClick += (_, eventArgs) =>
        {
            if (!Enabled || !eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
            ToggleCollapse();
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

    internal event EventHandler<FoundryPaneResizeEventArgs>? ResizeRequested;

    internal event EventHandler? CollapseToggleRequested;

    internal string LeadingPaneName { get; }

    internal bool IsCollapsed
    {
        get => _isCollapsed;
        set
        {
            if (_isCollapsed == value) return;
            _isCollapsed = value;
            UpdateToolTip();
            Invalidate();
        }
    }

    private void OnMouseDown(object? sender, MouseEventArgs eventArgs)
    {
        if (!Enabled || !eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
        _dragging = true;
        _showFocusRing = false;
        _lastScreenPoint = PointToScreen(eventArgs.Location);
        Focus();
        eventArgs.Handled = true;
        Invalidate();
    }

    private void OnMouseMove(object? sender, MouseEventArgs eventArgs)
    {
        if (!_dragging || !Enabled || !eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
        var screenPoint = PointToScreen(eventArgs.Location);
        var delta = _axis == FoundryPaneResizeAxis.Horizontal
            ? screenPoint.X - _lastScreenPoint.X
            : screenPoint.Y - _lastScreenPoint.Y;
        var roundedDelta = (int)Math.Round(delta);
        if (roundedDelta == 0) return;
        _lastScreenPoint = screenPoint;
        ResizeRequested?.Invoke(this, new FoundryPaneResizeEventArgs(roundedDelta));
        eventArgs.Handled = true;
    }

    private void OnMouseUp(object? sender, MouseEventArgs eventArgs)
    {
        if (!_dragging) return;
        _dragging = false;
        eventArgs.Handled = true;
        Invalidate();
    }

    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (!Enabled) return;
        var delta = (_axis, eventArgs.Key) switch
        {
            (FoundryPaneResizeAxis.Horizontal, Keys.Left) => -KeyboardStep,
            (FoundryPaneResizeAxis.Horizontal, Keys.Right) => KeyboardStep,
            (FoundryPaneResizeAxis.Vertical, Keys.Up) => -KeyboardStep,
            (FoundryPaneResizeAxis.Vertical, Keys.Down) => KeyboardStep,
            _ => 0,
        };
        if (delta != 0)
        {
            ResizeRequested?.Invoke(this, new FoundryPaneResizeEventArgs(delta));
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.Key is not (Keys.Enter or Keys.Space or Keys.Home)) return;
        ToggleCollapse();
        eventArgs.Handled = true;
    }

    private void ToggleCollapse()
    {
        CollapseToggleRequested?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void UpdateToolTip()
    {
        var direction = _axis == FoundryPaneResizeAxis.Horizontal
            ? "left or right"
            : "up or down";
        ToolTip = $"Drag {direction} to resize. Double-click or press Enter to " +
                  $"{(_isCollapsed ? "expand" : "collapse")} {LeadingPaneName}.";
    }

    private void OnPaint(object? sender, PaintEventArgs eventArgs)
    {
        var color = Enabled
            ? FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, _hovered || _dragging ? 255 : 170)
            : FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 80);
        var focusColor = FoundryTheme.WithAlpha(FoundryTheme.PrimaryText, 110);
        var centerX = Width / 2f;
        var centerY = Height / 2f;

        if (_axis == FoundryPaneResizeAxis.Horizontal)
        {
            eventArgs.Graphics.DrawLine(new Pen(color, _dragging ? 2 : 1), centerX, 0, centerX, Height);
            DrawGrip(eventArgs.Graphics, centerX, centerY, vertical: true, color);
        }
        else
        {
            eventArgs.Graphics.DrawLine(new Pen(color, _dragging ? 2 : 1), 0, centerY, Width, centerY);
            DrawGrip(eventArgs.Graphics, centerX, centerY, vertical: false, color);
        }

        if (_showFocusRing && Enabled)
        {
            using var focus = GraphicsPath.GetRoundRect(
                new RectangleF(1.5f, 1.5f, Math.Max(0, Width - 3), Math.Max(0, Height - 3)),
                4);
            eventArgs.Graphics.DrawPath(new Pen(focusColor, 1), focus);
        }
    }

    private static void DrawGrip(Graphics graphics, float x, float y, bool vertical, Color color)
    {
        const float radius = 1.25f;
        const float spacing = 4f;
        var background = FoundryTheme.CanvasSurface;
        if (vertical)
            graphics.FillRectangle(background, x - 3, y - 10, 6, 20);
        else
            graphics.FillRectangle(background, x - 10, y - 3, 20, 6);

        for (var index = -1; index <= 1; index++)
        {
            var dotX = vertical ? x : x + index * spacing;
            var dotY = vertical ? y + index * spacing : y;
            graphics.FillEllipse(color, dotX - radius, dotY - radius, radius * 2, radius * 2);
        }
    }
}

internal enum FoundryPaneResizeAxis
{
    Horizontal,
    Vertical,
}

internal sealed class FoundryPaneResizeEventArgs(int delta) : EventArgs
{
    internal int Delta { get; } = delta;
}

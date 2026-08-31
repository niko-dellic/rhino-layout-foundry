using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

/// <summary>
/// Responsive named-view preview surface used by the selection inspector. The
/// requested tile width controls density; the available width determines the
/// actual column count and distributes the remaining space evenly.
/// </summary>
internal sealed class NamedViewThumbnailGrid : Drawable
{
    private const int PaddingSize = 8;
    private const int Gap = 8;
    private const int LabelHeight = 28;
    private const double PreviewAspectRatio = 192d / 120d;
    private readonly Font _labelFont = SystemFonts.Bold(9);
    private NamedViewThumbnailItem[] _items = [];
    private string? _selectedName;
    private string? _pressedName;
    private PointF _pressPoint;
    private int? _hoveredIndex;
    private int _columns = 1;
    private float _cellWidth = 1;
    private float _rowHeight = 1;
    private float _contentHeight = 120;

    internal NamedViewThumbnailGrid()
        : base(true)
    {
        BackgroundColor = FoundryTheme.CanvasOverlayBackground;
        CanFocus = true;
        Paint += OnPaint;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += (_, _) => _pressedName = null;
        MouseLeave += (_, _) =>
        {
            _hoveredIndex = null;
            _pressedName = null;
            Invalidate();
        };
        KeyDown += OnKeyDown;
        GotFocus += (_, _) => Invalidate();
        LostFocus += (_, _) => Invalidate();
        EnabledChanged += (_, _) => Invalidate();
    }

    internal event EventHandler<NamedViewThumbnailSelectionEventArgs>? SelectionChanged;

    internal float ContentHeight => _contentHeight;

    internal void SetItems(IEnumerable<NamedViewThumbnailItem> items)
    {
        _items = items?.ToArray() ?? [];
        if (_selectedName is not null &&
            !_items.Any(item => string.Equals(item.Name, _selectedName, StringComparison.OrdinalIgnoreCase)))
            _selectedName = null;
        Invalidate();
    }

    internal void SetSelectedName(string? name)
    {
        if (string.Equals(_selectedName, name, StringComparison.OrdinalIgnoreCase)) return;
        _selectedName = name;
        Invalidate();
    }

    internal void SetLayout(double availableWidth, int requestedTileWidth, int minimumHeight = 120)
    {
        var width = Math.Max(1, (float)availableWidth);
        var usableWidth = Math.Max(1, width - PaddingSize * 2);
        requestedTileWidth = Math.Max(48, requestedTileWidth);
        _columns = Math.Max(1, (int)Math.Floor((usableWidth + Gap) / (requestedTileWidth + Gap)));
        _columns = Math.Min(Math.Max(1, _items.Length), _columns);
        _cellWidth = Math.Max(1, (usableWidth - Math.Max(0, _columns - 1) * Gap) / _columns);
        var previewHeight = (float)Math.Round(_cellWidth / PreviewAspectRatio);
        _rowHeight = previewHeight + LabelHeight + Gap;
        var rows = _items.Length == 0 ? 0 : (int)Math.Ceiling(_items.Length / (double)_columns);
        _contentHeight = rows == 0
            ? minimumHeight
            : Math.Max(minimumHeight, PaddingSize * 2 + rows * _rowHeight - Gap);
        Size = new Size((int)Math.Ceiling(width), (int)Math.Ceiling(_contentHeight));
        Invalidate();
    }

    private void OnPaint(object? sender, PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        graphics.AntiAlias = true;
        graphics.FillRectangle(FoundryTheme.CanvasOverlayBackground, eventArgs.ClipRectangle);
        if (_items.Length == 0)
        {
            graphics.DrawText(
                SystemFonts.Default(),
                FoundryTheme.MutedText,
                PaddingSize,
                PaddingSize,
                "No named views");
            return;
        }

        for (var index = 0; index < _items.Length; index++)
        {
            var bounds = CellBounds(index);
            if (!bounds.Intersects(eventArgs.ClipRectangle)) continue;
            DrawItem(graphics, index, bounds, _items[index]);
        }
    }

    private void DrawItem(Graphics graphics, int index, RectangleF bounds, NamedViewThumbnailItem item)
    {
        var selected = string.Equals(item.Name, _selectedName, StringComparison.OrdinalIgnoreCase);
        var hovered = Enabled && _hoveredIndex == index;
        using (var card = GraphicsPath.GetRoundRect(bounds, 6))
        {
            graphics.FillPath(
                selected
                    ? FoundryTheme.CanvasSubtleSurface
                    : hovered
                        ? FoundryTheme.CanvasSurface
                        : FoundryTheme.CanvasOverlayBackground,
                card);
            graphics.DrawPath(
                new Pen(
                    selected
                        ? FoundryTheme.SecondaryText
                        : FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, hovered ? 210 : 120),
                    selected ? 1.5f : 1),
                card);
        }

        var imageBounds = new RectangleF(
            bounds.Left + 5,
            bounds.Top + 5,
            Math.Max(1, bounds.Width - 10),
            Math.Max(1, bounds.Height - LabelHeight - 8));
        using (var imageShell = GraphicsPath.GetRoundRect(imageBounds, 4))
        {
            graphics.FillPath(FoundryTheme.ToolbarActiveBackground, imageShell);
            graphics.DrawPath(new Pen(FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 170), 1), imageShell);
        }
        if (item.Image is not null)
        {
            var destination = Fit(item.Image.Size, imageBounds);
            graphics.DrawImage(item.Image, destination);
        }

        graphics.DrawText(
            _labelFont,
            Enabled ? FoundryTheme.PrimaryText : FoundryTheme.MutedText,
            bounds.Left + 7,
            bounds.Bottom - LabelHeight + 7,
            CompactLabel(item.Name, bounds.Width - 14));

        if (!selected || !HasFocus || !Enabled) return;
        using var focus = GraphicsPath.GetRoundRect(
            new RectangleF(bounds.X + 2, bounds.Y + 2, bounds.Width - 4, bounds.Height - 4),
            5);
        graphics.DrawPath(new Pen(FoundryTheme.WithAlpha(FoundryTheme.PrimaryText, 80), 1), focus);
    }

    private RectangleF CellBounds(int index)
    {
        var row = index / _columns;
        var column = index % _columns;
        return new RectangleF(
            PaddingSize + column * (_cellWidth + Gap),
            PaddingSize + row * _rowHeight,
            _cellWidth,
            _rowHeight - Gap);
    }

    private int? HitIndex(PointF point)
    {
        for (var index = 0; index < _items.Length; index++)
            if (CellBounds(index).Contains(point))
                return index;
        return null;
    }

    private void OnMouseDown(object? sender, MouseEventArgs eventArgs)
    {
        if (!Enabled || !eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
        Focus();
        var index = HitIndex(eventArgs.Location);
        if (index is null)
        {
            Select(null);
            eventArgs.Handled = true;
            return;
        }

        _pressPoint = eventArgs.Location;
        _pressedName = _items[index.Value].Name;
        Select(_pressedName);
        eventArgs.Handled = true;
    }

    private void OnMouseMove(object? sender, MouseEventArgs eventArgs)
    {
        var hovered = HitIndex(eventArgs.Location);
        if (_hoveredIndex != hovered)
        {
            _hoveredIndex = hovered;
            ToolTip = hovered is { } index ? _items[index].Name : null;
            Invalidate();
        }

        if (!Enabled || _pressedName is null ||
            !eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
        var distance = Math.Sqrt(
            Math.Pow(eventArgs.Location.X - _pressPoint.X, 2) +
            Math.Pow(eventArgs.Location.Y - _pressPoint.Y, 2));
        if (distance <= 6) return;
        var data = new DataObject();
        data.SetString(_pressedName, ObserverCanvasDrawable.NamedViewDragType);
        var draggedName = _pressedName;
        _pressedName = null;
        DoDragDrop(data, DragEffects.Copy);
        ToolTip = draggedName;
        eventArgs.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (!Enabled || _items.Length == 0) return;
        var index = Array.FindIndex(_items,
            item => string.Equals(item.Name, _selectedName, StringComparison.OrdinalIgnoreCase));
        if (index < 0) index = 0;
        var next = eventArgs.Key switch
        {
            Keys.Left => index - 1,
            Keys.Right => index + 1,
            Keys.Up => index - _columns,
            Keys.Down => index + _columns,
            Keys.Home => 0,
            Keys.End => _items.Length - 1,
            _ => index,
        };
        if (next == index && eventArgs.Key is not
            (Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End)) return;
        Select(_items[Math.Clamp(next, 0, _items.Length - 1)].Name);
        eventArgs.Handled = true;
    }

    private void Select(string? name)
    {
        if (string.Equals(_selectedName, name, StringComparison.OrdinalIgnoreCase)) return;
        _selectedName = name;
        SelectionChanged?.Invoke(this, new NamedViewThumbnailSelectionEventArgs(name));
        Invalidate();
    }

    private static RectangleF Fit(Size imageSize, RectangleF bounds)
    {
        if (imageSize.Width <= 0 || imageSize.Height <= 0) return bounds;
        var scale = Math.Min(bounds.Width / imageSize.Width, bounds.Height / imageSize.Height);
        var width = imageSize.Width * scale;
        var height = imageSize.Height * scale;
        return new RectangleF(
            bounds.X + (bounds.Width - width) / 2,
            bounds.Y + (bounds.Height - height) / 2,
            width,
            height);
    }

    private static string CompactLabel(string text, float width)
    {
        var maximumCharacters = Math.Max(4, (int)Math.Floor(width / 7));
        return text.Length <= maximumCharacters
            ? text
            : $"{text[..Math.Max(1, maximumCharacters - 1)]}…";
    }
}

internal sealed record NamedViewThumbnailItem(string Name, Image? Image);

internal sealed class NamedViewThumbnailSelectionEventArgs(string? name) : EventArgs
{
    internal string? Name { get; } = name;
}

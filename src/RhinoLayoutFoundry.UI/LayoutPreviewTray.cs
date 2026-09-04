using System.Linq.Expressions;
using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Naming;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

internal sealed class LayoutPreviewTray : Drawable
{
    private const int ColumnCount = 3;
    private const int TileWidth = 136;
    private const int TileHeight = 112;
    private const int Gap = 8;
    private const int TrayPadding = 4;
    private readonly LayoutChoice[] _choices;
    private readonly Font _titleFont = SystemFonts.Bold(8);
    private readonly Font _subtitleFont = SystemFonts.Default(8);
    private int _selectedIndex;
    private PaperRecipe _paper = new(594, 420, "Millimeters");

    internal LayoutPreviewTray(LayoutChoice[] choices, int selectedIndex)
        : base(true)
    {
        _choices = choices;
        _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, choices.Length - 1));
        CanFocus = true;
        BackgroundColor = FoundryTheme.ContentBackground;
        var columns = Math.Min(ColumnCount, Math.Max(1, choices.Length));
        var rows = Math.Max(1, (choices.Length + ColumnCount - 1) / ColumnCount);
        Size = new Size(
            TrayPadding * 2 + columns * TileWidth + Math.Max(0, columns - 1) * Gap,
            TrayPadding * 2 + rows * TileHeight + Math.Max(0, rows - 1) * Gap);
        Paint += OnPaint;
        MouseDown += OnMouseDown;
        KeyDown += OnKeyDown;
    }

    internal event EventHandler? SelectedIndexChanged;
    internal event EventHandler? SelectionCommitted;

    internal int ContentWidth => Size.Width;
    internal int ContentHeight => Size.Height;
    internal int SelectedCenterY => (int)Math.Round(TileBounds(_selectedIndex).Center.Y);

    internal int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var next = Math.Clamp(value, 0, Math.Max(0, _choices.Length - 1));
            if (_selectedIndex == next) return;
            _selectedIndex = next;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void SetPaper(PaperRecipe paper)
    {
        _paper = paper;
        Invalidate();
    }

    private void OnPaint(object? sender, PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        graphics.AntiAlias = true;
        graphics.FillRectangle(FoundryTheme.ContentBackground, eventArgs.ClipRectangle);
        for (var index = 0; index < _choices.Length; index++) DrawTile(graphics, index);
    }

    private void DrawTile(Graphics graphics, int index)
    {
        var tile = TileBounds(index);
        var selected = index == _selectedIndex;
        graphics.FillRectangle(
            selected ? FoundryTheme.CanvasSubtleSurface : FoundryTheme.CanvasSurface,
            tile);
        graphics.DrawRectangle(
            new Pen(selected ? FoundryTheme.PrimaryText : FoundryTheme.CanvasBorder, selected ? 2 : 1),
            tile);

        var page = PageBounds(tile);
        graphics.FillRectangle(FoundryTheme.CanvasSubtleSurface, page);
        graphics.DrawRectangle(new Pen(FoundryTheme.CanvasBorder, 1), page);
        foreach (var detail in DetailBounds(_choices[index], page))
        {
            graphics.FillRectangle(FoundryTheme.ToolbarButtonBackground, detail);
            graphics.DrawRectangle(new Pen(FoundryTheme.CanvasBorder, 1), detail);
        }

        var parts = _choices[index].Label.Split([" — "], 2, StringSplitOptions.None);
        DrawCentered(graphics, _titleFont, FoundryTheme.PrimaryText, parts[0], tile, tile.Bottom - 31);
        if (parts.Length > 1)
            DrawCentered(graphics, _subtitleFont, FoundryTheme.MutedText, parts[1], tile, tile.Bottom - 17);
    }

    private RectangleF PageBounds(RectangleF tile)
    {
        const float availableWidth = 94;
        const float availableHeight = 54;
        var paperWidth = Math.Max(0.001, _paper.Width);
        var paperHeight = Math.Max(0.001, _paper.Height);
        var scale = Math.Min(availableWidth / paperWidth, availableHeight / paperHeight);
        var width = (float)Math.Max(12, paperWidth * scale);
        var height = (float)Math.Max(12, paperHeight * scale);
        return new RectangleF(
            tile.X + (tile.Width - width) / 2,
            tile.Y + 9 + (availableHeight - height) / 2,
            width,
            height);
    }

    internal static IReadOnlyList<RectangleF> DetailBounds(LayoutChoice choice, RectangleF page)
    {
        if (choice.Template is { } template)
        {
            var width = Math.Max(0.001, template.Paper.Width);
            var height = Math.Max(0.001, template.Paper.Height);
            return template.DetailSlots.Select(slot => FromNormalized(
                page,
                slot.Left / width,
                slot.Bottom / height,
                slot.Right / width,
                slot.Top / height)).ToArray();
        }

        var shortSide = Math.Min(page.Width, page.Height);
        var margin = shortSide * 0.025f;
        var halfGap = shortSide * 0.01f;
        var left = page.Left + margin;
        var right = page.Right - margin;
        var top = page.Top + margin;
        var bottom = page.Bottom - margin;
        var midX = page.Center.X;
        var midY = page.Center.Y;
        return choice.BuiltInLayout switch
        {
            BuiltInLayoutKind.Blank => [],
            BuiltInLayoutKind.SingleDetail => [new RectangleF(left, top, right - left, bottom - top)],
            BuiltInLayoutKind.TwoDetailsHorizontal =>
            [
                new RectangleF(left, top, right - left, midY - halfGap - top),
                new RectangleF(left, midY + halfGap, right - left, bottom - midY - halfGap),
            ],
            BuiltInLayoutKind.TwoDetailsVertical =>
            [
                new RectangleF(left, top, midX - halfGap - left, bottom - top),
                new RectangleF(midX + halfGap, top, right - midX - halfGap, bottom - top),
            ],
            BuiltInLayoutKind.FourDetailsGrid =>
            [
                new RectangleF(left, top, midX - halfGap - left, midY - halfGap - top),
                new RectangleF(midX + halfGap, top, right - midX - halfGap, midY - halfGap - top),
                new RectangleF(left, midY + halfGap, midX - halfGap - left, bottom - midY - halfGap),
                new RectangleF(midX + halfGap, midY + halfGap,
                    right - midX - halfGap, bottom - midY - halfGap),
            ],
            _ => [],
        };
    }

    internal static RectangleF FromNormalized(
        RectangleF page,
        double left,
        double bottom,
        double right,
        double top) => new(
            page.X + (float)(left * page.Width),
            page.Bottom - (float)(top * page.Height),
            (float)((right - left) * page.Width),
            (float)((top - bottom) * page.Height));

    private static void DrawCentered(
        Graphics graphics,
        Font font,
        Color color,
        string text,
        RectangleF bounds,
        float y)
    {
        var fitted = FitText(graphics, font, text, bounds.Width - 8);
        var size = graphics.MeasureString(font, fitted);
        graphics.DrawText(font, color, bounds.X + Math.Max(4, (bounds.Width - size.Width) / 2), y, fitted);
    }

    internal static string FitText(Graphics graphics, Font font, string text, float maximumWidth)
    {
        if (graphics.MeasureString(font, text).Width <= maximumWidth) return text;
        const string ellipsis = "…";
        for (var length = text.Length - 1; length > 0; length--)
        {
            var candidate = text[..length].TrimEnd() + ellipsis;
            if (graphics.MeasureString(font, candidate).Width <= maximumWidth) return candidate;
        }
        return ellipsis;
    }

    private RectangleF TileBounds(int index) => new(
        TrayPadding + index % ColumnCount * (TileWidth + Gap),
        TrayPadding + index / ColumnCount * (TileHeight + Gap),
        TileWidth,
        TileHeight);

    private void OnMouseDown(object? sender, MouseEventArgs eventArgs)
    {
        if (!eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
        Focus();
        var column = (int)Math.Floor((eventArgs.Location.X - TrayPadding) / (TileWidth + Gap));
        var row = (int)Math.Floor((eventArgs.Location.Y - TrayPadding) / (TileHeight + Gap));
        var index = row * ColumnCount + column;
        if (index < 0 || index >= _choices.Length || !TileBounds(index).Contains(eventArgs.Location)) return;
        SelectedIndex = index;
        SelectionCommitted?.Invoke(this, EventArgs.Empty);
        eventArgs.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        var next = eventArgs.Key switch
        {
            Keys.Left => _selectedIndex - 1,
            Keys.Right => _selectedIndex + 1,
            Keys.Up => _selectedIndex - ColumnCount,
            Keys.Down => _selectedIndex + ColumnCount,
            Keys.Home => 0,
            Keys.End => _choices.Length - 1,
            _ => _selectedIndex,
        };
        if (eventArgs.Key is Keys.Enter or Keys.Space)
        {
            SelectionCommitted?.Invoke(this, EventArgs.Empty);
            eventArgs.Handled = true;
            return;
        }
        next = Math.Clamp(next, 0, Math.Max(0, _choices.Length - 1));
        if (next == _selectedIndex) return;
        SelectedIndex = next;
        eventArgs.Handled = true;
    }
}
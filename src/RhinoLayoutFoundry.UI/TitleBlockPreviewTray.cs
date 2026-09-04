using System.Linq.Expressions;
using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Naming;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

internal sealed class TitleBlockPreviewTray : Drawable
{
    private const int ColumnCount = 3;
    private const int TileWidth = 156;
    private const int TileHeight = 112;
    private const int Gap = 8;
    private const int TrayPadding = 4;
    private readonly TitleBlockChoice[] _choices;
    private readonly Font _titleFont = SystemFonts.Bold(8);
    private readonly Font _subtitleFont = SystemFonts.Default(8);
    private int _selectedIndex;
    private PaperRecipe _paper = new(594, 420, "Millimeters");

    internal TitleBlockPreviewTray(TitleBlockChoice[] choices, int selectedIndex)
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
        for (var index = 0; index < _choices.Length; index++)
        {
            var tile = TileBounds(index);
            var selected = index == _selectedIndex;
            graphics.FillRectangle(selected ? FoundryTheme.CanvasSubtleSurface : FoundryTheme.CanvasSurface, tile);
            graphics.DrawRectangle(
                new Pen(selected ? FoundryTheme.PrimaryText : FoundryTheme.CanvasBorder, selected ? 2 : 1),
                tile);
            var page = PageBounds(_paper, new RectangleF(tile.X + 25, tile.Y + 8, tile.Width - 50, 56));
            DrawTitleBlock(graphics, _choices[index], _paper, page);
            var parts = _choices[index].Label.Split([" — "], 2, StringSplitOptions.None);
            DrawCentered(graphics, _titleFont, FoundryTheme.PrimaryText, parts[0], tile, tile.Bottom - 31);
            if (parts.Length > 1)
                DrawCentered(graphics, _subtitleFont, FoundryTheme.MutedText, parts[1], tile, tile.Bottom - 17);
        }
    }

    internal static RectangleF PageBounds(PaperRecipe paper, RectangleF available)
    {
        var paperWidth = Math.Max(0.001, paper.Width);
        var paperHeight = Math.Max(0.001, paper.Height);
        var scale = Math.Min(available.Width / paperWidth, available.Height / paperHeight);
        var width = (float)(paperWidth * scale);
        var height = (float)(paperHeight * scale);
        return new RectangleF(
            available.X + (available.Width - width) / 2,
            available.Y + (available.Height - height) / 2,
            width,
            height);
    }

    internal static void DrawTitleBlock(
        Graphics graphics,
        TitleBlockChoice choice,
        PaperRecipe paper,
        RectangleF page,
        bool showEmptyMarker = true)
    {
        graphics.FillRectangle(FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 55),
            page.X + 2, page.Y + 3, page.Width, page.Height);
        graphics.FillRectangle(FoundryTheme.CanvasSubtleSurface, page);
        graphics.DrawRectangle(new Pen(FoundryTheme.CanvasBorder, 1), page);
        if (choice.BuiltInKind is null)
        {
            if (showEmptyMarker)
                graphics.DrawLine(new Pen(FoundryTheme.WithAlpha(FoundryTheme.MutedText, 145), 2),
                    page.X + page.Width * 0.28f, page.Y + page.Height * 0.72f,
                    page.X + page.Width * 0.72f, page.Y + page.Height * 0.28f);
            return;
        }

        if (choice.BuiltInKind is { } kind)
        {
            try
            {
                var layout = AdaptiveTitleBlockLayoutSolver.Solve(kind, paper);
                float X(double value) => page.X + (float)(value / paper.Width * page.Width);
                float Y(double value) => page.Bottom - (float)(value / paper.Height * page.Height);
                float W(double value) => (float)(value / paper.Width * page.Width);
                float H(double value) => (float)(value / paper.Height * page.Height);
                var managedBlock = new RectangleF(
                    X(layout.Block.Left),
                    Y(layout.Block.Top),
                    W(layout.Block.Width),
                    H(layout.Block.Height));
                var pen = new Pen(FoundryTheme.WithAlpha(FoundryTheme.PrimaryText, 155), 1);
                graphics.DrawRectangle(pen, managedBlock);
                graphics.DrawLine(pen, managedBlock.X, managedBlock.Y + managedBlock.Height * 0.32f,
                    managedBlock.Right, managedBlock.Y + managedBlock.Height * 0.32f);
                graphics.DrawLine(pen, managedBlock.X + managedBlock.Width * 0.62f, managedBlock.Y,
                    managedBlock.X + managedBlock.Width * 0.62f, managedBlock.Bottom);
                graphics.DrawLine(pen, managedBlock.X, managedBlock.Y + managedBlock.Height * 0.68f,
                    managedBlock.Right, managedBlock.Y + managedBlock.Height * 0.68f);
                return;
            }
            catch (Exception)
            {
                return;
            }
        }
    }

    private static void DrawCentered(
        Graphics graphics,
        Font font,
        Color color,
        string text,
        RectangleF bounds,
        float y)
    {
        var fitted = LayoutPreviewTray.FitText(graphics, font, text, bounds.Width - 8);
        var size = graphics.MeasureString(font, fitted);
        graphics.DrawText(font, color, bounds.X + Math.Max(4, (bounds.Width - size.Width) / 2), y, fitted);
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
        if (eventArgs.Key is Keys.Enter or Keys.Space)
        {
            SelectionCommitted?.Invoke(this, EventArgs.Empty);
            eventArgs.Handled = true;
            return;
        }
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
        next = Math.Clamp(next, 0, Math.Max(0, _choices.Length - 1));
        if (next == _selectedIndex) return;
        SelectedIndex = next;
        eventArgs.Handled = true;
    }
}

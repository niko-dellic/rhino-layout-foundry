using static RhinoLayoutFoundry.UI.BatchLayoutLabels;
using System.Linq.Expressions;
using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Naming;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

internal sealed class DetailNamedViewTray : Drawable
{
    private const int ColumnCount = 3;
    private const int TileWidth = 172;
    private const int TileHeight = 112;
    private const int Gap = 8;
    private const int TrayPadding = 4;
    private readonly NamedViewChoice[] _choices;
    private readonly NamedViewPreviewTray _previewSource;
    private readonly bool _hasMixedOption;
    private readonly Font _titleFont = SystemFonts.Bold(8);
    private Guid? _displayModeId;
    private PreviewAppearance? _previewAppearance;
    private int _selectedIndex;

    internal DetailNamedViewTray(
        NamedViewChoice[] choices,
        NamedViewPreviewTray previewSource,
        bool hasMixedOption,
        int selectedIndex,
        Guid? displayModeId,
        PreviewAppearance? previewAppearance)
        : base(true)
    {
        _choices = choices;
        _previewSource = previewSource;
        _hasMixedOption = hasMixedOption;
        _displayModeId = displayModeId;
        _previewAppearance = previewAppearance;
        var choiceCount = choices.Length + (hasMixedOption ? 1 : 0);
        _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, choiceCount - 1));
        var columns = Math.Min(ColumnCount, Math.Max(1, choiceCount));
        var rows = Math.Max(1, (choiceCount + ColumnCount - 1) / ColumnCount);
        Size = new Size(
            TrayPadding * 2 + columns * TileWidth + Math.Max(0, columns - 1) * Gap,
            TrayPadding * 2 + rows * TileHeight + Math.Max(0, rows - 1) * Gap);
        CanFocus = true;
        BackgroundColor = FoundryTheme.ContentBackground;
        Paint += OnPaint;
        MouseDown += OnMouseDown;
        KeyDown += OnKeyDown;
    }

    internal int SelectedIndex
    {
        get => _selectedIndex;
        private set
        {
            var choiceCount = _choices.Length + (_hasMixedOption ? 1 : 0);
            var next = Math.Clamp(value, 0, Math.Max(0, choiceCount - 1));
            if (_selectedIndex == next) return;
            _selectedIndex = next;
            Invalidate();
        }
    }

    internal int ContentHeight => Size.Height;
    internal int SelectedCenterY => (int)Math.Round(TileBounds(_selectedIndex).Center.Y);

    internal void SetPreviewContext(Guid? displayModeId, PreviewAppearance? previewAppearance)
    {
        if (_displayModeId == displayModeId && _previewAppearance == previewAppearance) return;
        _displayModeId = displayModeId;
        _previewAppearance = previewAppearance;
        Invalidate();
    }

    private void OnPaint(object? sender, PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        graphics.AntiAlias = true;
        graphics.FillRectangle(FoundryTheme.ContentBackground, eventArgs.ClipRectangle);
        var choiceCount = _choices.Length + (_hasMixedOption ? 1 : 0);
        for (var index = 0; index < choiceCount; index++)
        {
            var tile = TileBounds(index);
            var selected = index == _selectedIndex;
            graphics.FillRectangle(
                selected ? FoundryTheme.CanvasSubtleSurface : FoundryTheme.CanvasSurface,
                tile);
            graphics.DrawRectangle(
                new Pen(selected ? FoundryTheme.PrimaryText : FoundryTheme.CanvasBorder, selected ? 2 : 1),
                tile);
            if (_hasMixedOption && index == 0)
            {
                var previewBounds = new RectangleF(tile.X + 8, tile.Y + 7, tile.Width - 16, 72);
                graphics.FillRectangle(FoundryTheme.CanvasSubtleSurface, previewBounds);
                DrawCentered(graphics, SystemFonts.Bold(10), FoundryTheme.MutedText,
                    MixedDisplayMode, previewBounds, previewBounds.Y + 27);
                graphics.DrawRectangle(new Pen(FoundryTheme.CanvasBorder, 1), previewBounds);
                DrawCentered(graphics, _titleFont, FoundryTheme.PrimaryText,
                    MixedDisplayMode, tile, tile.Bottom - 22);
                continue;
            }

            var choiceIndex = index - (_hasMixedOption ? 1 : 0);
            NamedViewPreviewTray.DrawPreview(
                graphics,
                _choices[choiceIndex],
                _previewSource.PreviewFor(
                    _choices[choiceIndex].Name,
                    _displayModeId,
                    _previewAppearance),
                new RectangleF(tile.X + 8, tile.Y + 7, tile.Width - 16, 72));
            DrawCentered(graphics, _titleFont, FoundryTheme.PrimaryText,
                _choices[choiceIndex].Label, tile, tile.Bottom - 22);
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
        var fitted = LayoutPreviewTray.FitText(graphics, font, text, bounds.Width - 10);
        var size = graphics.MeasureString(font, fitted);
        graphics.DrawText(font, color, bounds.X + Math.Max(5, (bounds.Width - size.Width) / 2), y, fitted);
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
        var choiceCount = _choices.Length + (_hasMixedOption ? 1 : 0);
        if (index < 0 || index >= choiceCount || !TileBounds(index).Contains(eventArgs.Location)) return;
        SelectedIndex = index;
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
            Keys.End => _choices.Length + (_hasMixedOption ? 1 : 0) - 1,
            _ => _selectedIndex,
        };
        if (next == _selectedIndex) return;
        SelectedIndex = next;
        eventArgs.Handled = true;
    }
}


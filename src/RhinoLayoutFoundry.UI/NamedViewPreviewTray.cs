using System.Linq.Expressions;
using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Naming;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

internal sealed class NamedViewPreviewTray : Drawable
{
    internal const int TrayHeight = 140;
    private const int TileWidth = 172;
    private const int TileHeight = 112;
    private const int ContentHeight = 120;
    private const int Gap = 8;
    private const int TrayPadding = 4;
    private readonly NamedViewChoice[] _choices;
    private readonly Font _titleFont = SystemFonts.Bold(8);
    private readonly Dictionary<PreviewKey, Bitmap> _previews = [];
    private readonly Dictionary<PreviewKey, string> _previewFailures = [];
    private int _selectedIndex;

    internal NamedViewPreviewTray(NamedViewChoice[] choices, int selectedIndex)
        : base(true)
    {
        _choices = choices;
        _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, choices.Length - 1));
        CanFocus = true;
        BackgroundColor = FoundryTheme.ContentBackground;
        Size = new Size(
            Math.Max(1, TrayPadding * 2 + choices.Length * TileWidth + Math.Max(0, choices.Length - 1) * Gap),
            ContentHeight);
        Paint += OnPaint;
        MouseDown += OnMouseDown;
        KeyDown += OnKeyDown;
    }

    internal event EventHandler? SelectedIndexChanged;
    internal event EventHandler? SelectionCommitted;
    internal event EventHandler? PreviewsChanged;
    internal int ContentWidth => Size.Width;
    internal int SelectedCenter => TrayPadding + _selectedIndex * (TileWidth + Gap) + TileWidth / 2;

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

    internal Bitmap? PreviewAt(int index)
    {
        if (index < 0 || index >= _choices.Length || _choices[index].Name is not { } name) return null;
        return PreviewFor(name, null);
    }

    internal Bitmap? PreviewFor(
        string? name,
        Guid? displayModeId = null,
        PreviewAppearance? appearance = null) => string.IsNullOrWhiteSpace(name)
        ? null
        : _previews.GetValueOrDefault(PreviewKey.For(name, displayModeId, appearance));

    internal bool HasPreview(
        string name,
        Guid? displayModeId,
        PreviewAppearance? appearance = null) =>
        _previews.ContainsKey(PreviewKey.For(name, displayModeId, appearance));

    internal bool HasPreviewFailure(
        string name,
        Guid? displayModeId,
        PreviewAppearance? appearance = null) =>
        _previewFailures.ContainsKey(PreviewKey.For(name, displayModeId, appearance));

    internal void SetPreview(NamedViewThumbnailKey thumbnailKey, Bitmap bitmap)
    {
        var key = PreviewKey.For(thumbnailKey);
        if (_previews.Remove(key, out var previous)) previous.Dispose();
        _previewFailures.Remove(key);
        _previews[key] = bitmap;
        Invalidate();
        PreviewsChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void SetPreviewFailure(NamedViewThumbnailKey thumbnailKey, string error)
    {
        var key = PreviewKey.For(thumbnailKey);
        _previewFailures[key] = error;
        Invalidate();
        PreviewsChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void DisposePreviews()
    {
        foreach (var bitmap in _previews.Values) bitmap.Dispose();
        _previews.Clear();
        _previewFailures.Clear();
    }

    private readonly record struct PreviewKey(
        string Name,
        Guid? DisplayModeId,
        Guid? AppearanceStateId,
        Guid? AppearanceScopeId,
        Guid? DetailSlotId)
    {
        internal static PreviewKey For(
            string name,
            Guid? displayModeId,
            PreviewAppearance? appearance) => new(
            name.ToUpperInvariant(),
            displayModeId,
            appearance?.AppearanceStateId,
            appearance?.FolderId,
            appearance?.DetailSlotId);

        internal static PreviewKey For(NamedViewThumbnailKey key) => new(
            key.NamedViewName.ToUpperInvariant(),
            key.DisplayModeId,
            key.AppearanceStateId,
            key.AppearanceScopeId,
            key.DetailSlotId);
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
            graphics.FillRectangle(
                selected ? FoundryTheme.CanvasSubtleSurface : FoundryTheme.CanvasSurface,
                tile);
            graphics.DrawRectangle(
                new Pen(selected ? FoundryTheme.PrimaryText : FoundryTheme.CanvasBorder, selected ? 2 : 1),
                tile);
            DrawPreview(
                graphics,
                _choices[index],
                PreviewAt(index),
                new RectangleF(tile.X + 8, tile.Y + 7, tile.Width - 16, 72));
            DrawCentered(graphics, _titleFont, FoundryTheme.PrimaryText,
                _choices[index].Label, tile, tile.Bottom - 22);
        }
    }

    internal static void DrawPreview(
        Graphics graphics,
        NamedViewChoice choice,
        Bitmap? preview,
        RectangleF bounds)
    {
        graphics.FillRectangle(FoundryTheme.CanvasSurface, bounds);
        if (preview is not null)
        {
            graphics.DrawImage(preview, bounds);
        }
        else if (choice.Name is null)
        {
            graphics.FillRectangle(FoundryTheme.CanvasSubtleSurface, bounds);
            var pen = new Pen(FoundryTheme.WithAlpha(FoundryTheme.PrimaryText, 125), 1);
            var frame = new RectangleF(
                bounds.X + bounds.Width * 0.2f,
                bounds.Y + bounds.Height * 0.2f,
                bounds.Width * 0.6f,
                bounds.Height * 0.6f);
            graphics.DrawRectangle(pen, frame);
            graphics.DrawLine(pen, frame.Left, frame.Bottom,
                frame.Left + frame.Width * 0.45f, frame.Top + frame.Height * 0.48f);
            graphics.DrawLine(pen, frame.Left + frame.Width * 0.45f,
                frame.Top + frame.Height * 0.48f, frame.Right, frame.Bottom - frame.Height * 0.2f);
        }
        else
        {
            graphics.FillRectangle(FoundryTheme.CanvasSubtleSurface, bounds);
            var pen = new Pen(FoundryTheme.WithAlpha(FoundryTheme.MutedText, 110), 1);
            graphics.DrawLine(pen, bounds.Left + 10, bounds.Bottom - 12,
                bounds.Left + bounds.Width * 0.45f, bounds.Top + bounds.Height * 0.48f);
            graphics.DrawLine(pen, bounds.Left + bounds.Width * 0.45f,
                bounds.Top + bounds.Height * 0.48f, bounds.Right - 10, bounds.Bottom - 18);
        }
        graphics.DrawRectangle(new Pen(FoundryTheme.CanvasBorder, 1), bounds);
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
        TrayPadding + index * (TileWidth + Gap),
        TrayPadding,
        TileWidth,
        TileHeight);

    private void OnMouseDown(object? sender, MouseEventArgs eventArgs)
    {
        if (!eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
        Focus();
        var index = (int)Math.Floor((eventArgs.Location.X - TrayPadding) / (TileWidth + Gap));
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


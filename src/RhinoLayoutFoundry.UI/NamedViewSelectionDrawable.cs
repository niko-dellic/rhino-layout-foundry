using System.Linq.Expressions;
using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Naming;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

internal sealed class NamedViewSelectionDrawable : Drawable
{
    private readonly NamedViewChoice[] _choices;
    private readonly NamedViewPreviewTray _previews;
    private readonly string _detailLabel;
    private readonly Font _titleFont = SystemFonts.Bold(9);
    private readonly Font _subtitleFont = SystemFonts.Default(8);
    private int _selectedIndex;
    private bool _mixed;
    private bool _expanded;

    internal NamedViewSelectionDrawable(
        NamedViewChoice[] choices,
        NamedViewPreviewTray previews,
        string detailLabel,
        int selectedIndex,
        bool mixed)
        : base(true)
    {
        _choices = choices;
        _previews = previews;
        _detailLabel = detailLabel;
        _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, choices.Length - 1));
        _mixed = mixed;
        CanFocus = true;
        Height = 78;
        MinimumSize = new Size(220, 78);
        BackgroundColor = FoundryTheme.CanvasSurface;
        Paint += OnPaint;
        MouseDown += OnMouseDown;
        KeyDown += OnKeyDown;
    }

    internal event EventHandler? Activated;
    internal string DetailLabel => _detailLabel;

    internal void SetSelection(int selectedIndex, bool expanded, bool mixed)
    {
        _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, _choices.Length - 1));
        _expanded = expanded;
        _mixed = mixed;
        Invalidate();
    }

    internal void SetExpanded(bool expanded)
    {
        _expanded = expanded;
        Invalidate();
    }

    internal void RefreshPreview() => Invalidate();

    private void OnPaint(object? sender, PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        graphics.AntiAlias = true;
        var bounds = new RectangleF(0.5f, 0.5f, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        graphics.FillRectangle(FoundryTheme.CanvasSurface, bounds);
        graphics.DrawRectangle(new Pen(FoundryTheme.CanvasBorder, HasFocus ? 2 : 1), bounds);

        var previewBounds = new RectangleF(12, 10, 92, 56);
        if (_mixed)
        {
            graphics.FillRectangle(FoundryTheme.CanvasSubtleSurface, previewBounds);
            graphics.DrawRectangle(new Pen(FoundryTheme.CanvasBorder, 1), previewBounds);
            var mixedSize = graphics.MeasureString(_titleFont, "Mixed");
            graphics.DrawText(_titleFont, FoundryTheme.MutedText,
                previewBounds.X + (previewBounds.Width - mixedSize.Width) / 2,
                previewBounds.Y + (previewBounds.Height - mixedSize.Height) / 2,
                "Mixed");
        }
        else
        {
            NamedViewPreviewTray.DrawPreview(
                graphics,
                _choices[_selectedIndex],
                _previews.PreviewAt(_selectedIndex),
                previewBounds);
        }
        var choice = _choices[_selectedIndex];
        var textWidth = Math.Max(20, Width - 142);
        graphics.DrawText(_titleFont, FoundryTheme.PrimaryText, 118, 23,
            LayoutPreviewTray.FitText(graphics, _titleFont, _detailLabel, textWidth));
        graphics.DrawText(_subtitleFont, FoundryTheme.MutedText, 118, 43,
            LayoutPreviewTray.FitText(graphics, _subtitleFont,
                _mixed ? "Mixed" : choice.Label, textWidth));
        graphics.DrawText(SystemFonts.Default(10), FoundryTheme.MutedText,
            Math.Max(122, Width - 25), 30, _expanded ? "▴" : "▾");
    }

    private void OnMouseDown(object? sender, MouseEventArgs eventArgs)
    {
        if (!eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
        Focus();
        Activated?.Invoke(this, EventArgs.Empty);
        eventArgs.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key is not (Keys.Enter or Keys.Space)) return;
        Activated?.Invoke(this, EventArgs.Empty);
        eventArgs.Handled = true;
    }
}


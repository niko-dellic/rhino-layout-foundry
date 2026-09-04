using System.Linq.Expressions;
using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Naming;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

internal sealed class TitleBlockSelectionDrawable : Drawable
{
    private readonly TitleBlockChoice[] _choices;
    private readonly Font _titleFont = SystemFonts.Bold(9);
    private readonly Font _subtitleFont = SystemFonts.Default(8);
    private int _selectedIndex;
    private bool _expanded;
    private PaperRecipe _paper = new(594, 420, "Millimeters");

    internal TitleBlockSelectionDrawable(TitleBlockChoice[] choices, int selectedIndex)
        : base(true)
    {
        _choices = choices;
        _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, choices.Length - 1));
        CanFocus = true;
        Height = 78;
        Size = new Size(220, 78);
        BackgroundColor = FoundryTheme.CanvasSurface;
        Paint += OnPaint;
        MouseDown += OnMouseDown;
        KeyDown += OnKeyDown;
    }

    internal event EventHandler? Activated;

    internal void SetSelection(int selectedIndex, bool expanded)
    {
        _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, _choices.Length - 1));
        _expanded = expanded;
        Invalidate();
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
        var bounds = new RectangleF(0.5f, 0.5f, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        graphics.FillRectangle(FoundryTheme.CanvasSurface, bounds);
        graphics.DrawRectangle(new Pen(FoundryTheme.CanvasBorder, HasFocus ? 2 : 1), bounds);
        var page = TitleBlockPreviewTray.PageBounds(_paper, new RectangleF(12, 10, 78, 56));
        TitleBlockPreviewTray.DrawTitleBlock(graphics, _choices[_selectedIndex], _paper, page);
        var parts = _choices[_selectedIndex].Label.Split([" — "], 2, StringSplitOptions.None);
        var textWidth = Math.Max(20, Width - 132);
        graphics.DrawText(_titleFont, FoundryTheme.PrimaryText, 106, 23,
            LayoutPreviewTray.FitText(graphics, _titleFont, parts[0], textWidth));
        if (parts.Length > 1)
            graphics.DrawText(_subtitleFont, FoundryTheme.MutedText, 106, 43,
                LayoutPreviewTray.FitText(graphics, _subtitleFont, parts[1], textWidth));
        graphics.DrawText(SystemFonts.Default(10), FoundryTheme.MutedText,
            Math.Max(110, Width - 25), 30, _expanded ? "▴" : "▾");
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


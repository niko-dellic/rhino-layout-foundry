using System.Linq.Expressions;
using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Naming;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

internal sealed class LayoutPickerDrawable : Drawable
{
    private readonly LayoutChoice[] _choices;
    private readonly Font _titleFont = SystemFonts.Bold();
    private readonly Font _subtitleFont = SystemFonts.Default();
    private int _selectedIndex;
    private bool _expanded;
    private bool _hovered;

    internal LayoutPickerDrawable(LayoutChoice[] choices, int selectedIndex)
        : base(true)
    {
        _choices = choices;
        _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, choices.Length - 1));
        CanFocus = true;
        Size = new Size(280, 32);
        MinimumSize = Size;
        BackgroundColor = Colors.Transparent;
        UpdateToolTip();
        Paint += OnPaint;
        MouseEnter += (_, _) => { _hovered = true; Invalidate(); };
        MouseLeave += (_, _) => { _hovered = false; Invalidate(); };
        MouseDown += (_, eventArgs) =>
        {
            if (!Enabled || !eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
            Focus();
            Activated?.Invoke(this, EventArgs.Empty);
            eventArgs.Handled = true;
        };
        KeyDown += (_, eventArgs) =>
        {
            if (!Enabled || eventArgs.Key is not (Keys.Enter or Keys.Space)) return;
            Activated?.Invoke(this, EventArgs.Empty);
            eventArgs.Handled = true;
        };
    }

    internal event EventHandler? Activated;

    internal void SetSelection(int selectedIndex, bool expanded)
    {
        _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, _choices.Length - 1));
        _expanded = expanded;
        UpdateToolTip();
        Invalidate();
    }

    private void OnPaint(object? sender, PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        graphics.AntiAlias = true;
        var bounds = new RectangleF(0.5f, 0.5f, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var surface = GraphicsPath.GetRoundRect(bounds, 6);
        graphics.FillPath(
            _hovered && Enabled ? FoundryTheme.CanvasSubtleSurface : FoundryTheme.ToolbarButtonBackground,
            surface);
        graphics.DrawPath(new Pen(
            HasFocus ? FoundryTheme.PrimaryText : FoundryTheme.CanvasBorder,
            HasFocus ? 2 : 1), surface);

        var (name, description) = PickerSummary();
        var textColor = Enabled ? FoundryTheme.PrimaryText : FoundryTheme.MutedText;
        var availableWidth = Math.Max(20, Width - 40);
        var title = LayoutPreviewTray.FitText(graphics, _titleFont, $"{name}:", availableWidth);
        var titleSize = graphics.MeasureString(_titleFont, title);
        graphics.DrawText(_titleFont, textColor, 10, (Height - titleSize.Height) / 2f, title);

        var descriptionX = 10 + titleSize.Width + FoundryTheme.Space1;
        var descriptionWidth = Math.Max(0, availableWidth - titleSize.Width - FoundryTheme.Space1);
        if (descriptionWidth > 8)
        {
            var fittedDescription = LayoutPreviewTray.FitText(
                graphics,
                _subtitleFont,
                description,
                descriptionWidth);
            var descriptionSize = graphics.MeasureString(_subtitleFont, fittedDescription);
            graphics.DrawText(
                _subtitleFont,
                Enabled ? FoundryTheme.SecondaryText : FoundryTheme.MutedText,
                descriptionX,
                (Height - descriptionSize.Height) / 2f,
                fittedDescription);
        }

        var arrow = _expanded ? "▴" : "▾";
        var arrowFont = SystemFonts.Default(10);
        var arrowSize = graphics.MeasureString(arrowFont, arrow);
        graphics.DrawText(
            arrowFont,
            FoundryTheme.MutedText,
            Math.Max(16, Width - 22),
            (Height - arrowSize.Height) / 2f,
            arrow);
    }

    private (string Name, string Description) PickerSummary()
    {
        var parts = _choices[_selectedIndex].Label.Split([" — "], 2, StringSplitOptions.None);
        if (parts.Length == 1) return (parts[0], string.Empty);
        return _choices[_selectedIndex].TemplateId is not null ||
               _choices[_selectedIndex].BuiltInLayout == BuiltInLayoutKind.Blank
            ? (parts[0], parts[1])
            : (parts[1], parts[0]);
    }

    private void UpdateToolTip()
    {
        var (name, description) = PickerSummary();
        ToolTip = string.IsNullOrWhiteSpace(description) ? name : $"{name}: {description}";
    }
}


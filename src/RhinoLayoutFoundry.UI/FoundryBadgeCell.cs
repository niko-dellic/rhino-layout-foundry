using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

internal enum FoundryBadgeTone
{
    Neutral,
    Warning,
    Error,
}

/// <summary>
/// Compact shadcn-inspired badge renderer for read-only grid status cells.
/// It leaves the native row background and selection treatment intact while
/// adding a semantic outlined surface around non-empty values.
/// </summary>
internal sealed class FoundryBadgeCell<T> : DrawableCell
{
    private const float BadgeHeight = 20;
    private const float HorizontalPadding = 8;
    private readonly Font _font = FoundryTheme.HierarchyTableBadgeFont;
    private readonly Func<T, string> _textSelector;
    private readonly Func<T, FoundryBadgeTone> _toneSelector;

    internal FoundryBadgeCell(
        Func<T, string> textSelector,
        Func<T, FoundryBadgeTone> toneSelector)
    {
        _textSelector = textSelector ?? throw new ArgumentNullException(nameof(textSelector));
        _toneSelector = toneSelector ?? throw new ArgumentNullException(nameof(toneSelector));
        Paint += OnPaint;
    }

    private void OnPaint(object? sender, CellPaintEventArgs eventArgs)
    {
        if (eventArgs.Item is not T item) return;
        var text = _textSelector(item);
        if (string.IsNullOrWhiteSpace(text)) return;

        var graphics = eventArgs.Graphics;
        graphics.AntiAlias = true;
        var tone = _toneSelector(item);
        var accent = tone switch
        {
            FoundryBadgeTone.Warning => FoundryTheme.WarningAccent,
            FoundryBadgeTone.Error => FoundryTheme.DangerAccent,
            _ => FoundryTheme.SecondaryText,
        };
        var textSize = graphics.MeasureString(_font, text);
        var availableWidth = Math.Max(1, eventArgs.ClipRectangle.Width - FoundryTheme.Space2);
        var badgeWidth = Math.Min(
            availableWidth,
            Math.Max(36, (float)Math.Ceiling(textSize.Width + HorizontalPadding * 2)));
        var badgeBounds = new RectangleF(
            eventArgs.ClipRectangle.Left + FoundryTheme.Space1,
            eventArgs.ClipRectangle.Top + Math.Max(1, (eventArgs.ClipRectangle.Height - BadgeHeight) / 2),
            badgeWidth,
            BadgeHeight);
        using var badge = GraphicsPath.GetRoundRect(badgeBounds, 6);
        graphics.FillPath(
            FoundryTheme.WithAlpha(accent, FoundryTheme.IsDarkMode ? 28 : 18),
            badge);
        graphics.DrawPath(
            new Pen(FoundryTheme.WithAlpha(accent, 150), 1),
            badge);
        graphics.DrawText(
            _font,
            accent,
            badgeBounds.Left + HorizontalPadding,
            badgeBounds.Top + Math.Max(0, (badgeBounds.Height - textSize.Height) / 2),
            text);
    }
}

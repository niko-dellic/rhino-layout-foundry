using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

internal enum FoundryBadgeTone
{
    Neutral,
    Warning,
    Error,
}

internal static class FoundryBadgeRenderer
{
    internal const float StandardHeight = 20;
    internal const float HorizontalPadding = 8;
    internal const float Radius = 6;

    internal static float MeasureWidth(
        Graphics graphics,
        Font font,
        string text,
        float minimumWidth,
        float maximumWidth)
    {
        var textSize = graphics.MeasureString(font, text);
        return Math.Min(
            Math.Max(1, maximumWidth),
            Math.Max(minimumWidth, (float)Math.Ceiling(textSize.Width + HorizontalPadding * 2)));
    }

    internal static void Draw(
        Graphics graphics,
        RectangleF bounds,
        string text,
        Font font,
        Color background,
        Color border,
        Color foreground)
    {
        using var badge = GraphicsPath.GetRoundRect(bounds, Math.Min(Radius, bounds.Height / 2));
        graphics.FillPath(background, badge);
        graphics.DrawPath(new Pen(border, 1), badge);
        var textSize = graphics.MeasureString(font, text);
        graphics.DrawText(
            font,
            foreground,
            bounds.Left + HorizontalPadding,
            bounds.Top + Math.Max(0, (bounds.Height - textSize.Height) / 2),
            text);
    }
}

/// <summary>
/// Compact shadcn-inspired badge renderer for read-only grid status cells.
/// It leaves the native row background and selection treatment intact while
/// adding a semantic outlined surface around non-empty values.
/// </summary>
internal sealed class FoundryBadgeCell<T> : DrawableCell
{
    private readonly Font _font = SystemFonts.Bold(8);
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
        var availableWidth = Math.Max(1, eventArgs.ClipRectangle.Width - FoundryTheme.Space2);
        var badgeWidth = FoundryBadgeRenderer.MeasureWidth(
            graphics,
            _font,
            text,
            minimumWidth: 36,
            maximumWidth: availableWidth);
        var badgeBounds = new RectangleF(
            eventArgs.ClipRectangle.Left + FoundryTheme.Space1,
            eventArgs.ClipRectangle.Top + Math.Max(
                1,
                (eventArgs.ClipRectangle.Height - FoundryBadgeRenderer.StandardHeight) / 2),
            badgeWidth,
            FoundryBadgeRenderer.StandardHeight);
        FoundryBadgeRenderer.Draw(
            graphics,
            badgeBounds,
            text,
            _font,
            FoundryTheme.WithAlpha(accent, FoundryTheme.IsDarkMode ? 28 : 18),
            FoundryTheme.WithAlpha(accent, 150),
            accent);
    }
}

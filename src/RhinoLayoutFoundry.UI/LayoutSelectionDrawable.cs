using System.Linq.Expressions;
using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Naming;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

internal sealed class LayoutSelectionDrawable : Drawable
{
    private readonly LayoutChoice[] _choices;
    private readonly Font _detailFont = SystemFonts.Bold(7);
    private readonly Font _detailMetaFont = SystemFonts.Default(6);
    private DetailPreviewState[] _detailStates = [];
    private int _selectedIndex;
    private int _hoveredDetailIndex = -1;
    private int _keyboardDetailIndex = -1;
    private PaperRecipe _paper = new(594, 420, "Millimeters");
    private TitleBlockChoice? _titleBlock;
    private Bitmap? _pagePreview;
    private string? _pagePreviewMessage;
    private bool _overlayPagePreviewDetails = true;

    internal LayoutSelectionDrawable(LayoutChoice[] choices, int selectedIndex)
        : base(true)
    {
        _choices = choices;
        _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, choices.Length - 1));
        CanFocus = true;
        Size = new Size(428, 320);
        MinimumSize = new Size(180, 140);
        BackgroundColor = FoundryTheme.CanvasSurface;
        Paint += OnPaint;
        SizeChanged += (_, _) => Invalidate();
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;
        KeyDown += OnKeyDown;
    }

    internal event EventHandler<DetailActivatedEventArgs>? DetailActivated;
    internal bool HasPagePreview => _pagePreview is not null;

    internal void SetSelection(int selectedIndex)
    {
        if (_selectedIndex != selectedIndex)
        {
            _hoveredDetailIndex = -1;
            _keyboardDetailIndex = -1;
        }
        _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, _choices.Length - 1));
        Invalidate();
    }

    internal void SetDetailStates(IReadOnlyList<DetailPreviewState> states)
    {
        _detailStates = states.ToArray();
        if (_keyboardDetailIndex >= _detailStates.Length) _keyboardDetailIndex = -1;
        if (_hoveredDetailIndex >= _detailStates.Length) _hoveredDetailIndex = -1;
        Invalidate();
    }

    internal void SetPaper(PaperRecipe paper)
    {
        _paper = paper;
        Invalidate();
    }

    internal void SetTitleBlock(TitleBlockChoice titleBlock)
    {
        _titleBlock = titleBlock;
        Invalidate();
    }

    internal void SetPagePreview(
        Bitmap? preview,
        string? message,
        bool overlayDetails = true)
    {
        if (!ReferenceEquals(_pagePreview, preview))
            _pagePreview?.Dispose();
        _pagePreview = preview;
        _pagePreviewMessage = message;
        _overlayPagePreviewDetails = overlayDetails;
        Invalidate();
    }

    internal void DisposePagePreview()
    {
        _pagePreview?.Dispose();
        _pagePreview = null;
        _pagePreviewMessage = null;
        _overlayPagePreviewDetails = true;
    }

    private void OnPaint(object? sender, PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        graphics.AntiAlias = true;
        var page = PageBounds();
        if (_pagePreview is { } pagePreview)
        {
            graphics.DrawImage(pagePreview, page);
        }
        else
        {
            TitleBlockPreviewTray.DrawTitleBlock(
                graphics,
                _titleBlock ?? new TitleBlockChoice(false, null, null, "None", null),
                _paper,
                page,
                showEmptyMarker: false);
            if (!string.IsNullOrWhiteSpace(_pagePreviewMessage))
            {
                DrawCentered(
                    graphics,
                    _detailMetaFont,
                    FoundryTheme.MutedText,
                    _pagePreviewMessage,
                    page,
                    page.Y + Math.Max(10, (page.Height - 12) / 2));
            }
        }
        if (_pagePreview is not null && !_overlayPagePreviewDetails)
        {
            var previewDetails = PreviewDetailBounds(page);
            for (var detailIndex = 0;
                 detailIndex < previewDetails.Count && detailIndex < _detailStates.Length;
                 detailIndex++)
            {
                var highlighted = detailIndex == _hoveredDetailIndex ||
                                  detailIndex == _keyboardDetailIndex;
                var changed = _detailStates[detailIndex].Changed;
                if (!highlighted && !changed) continue;
                graphics.DrawRectangle(new Pen(
                    changed ? FoundryTheme.WarningAccent : FoundryTheme.PrimaryText,
                    2), previewDetails[detailIndex]);
            }
            if (HasFocus)
                graphics.DrawRectangle(new Pen(FoundryTheme.PrimaryText, 2), page);
            return;
        }
        var details = PreviewDetailBounds(page);
        for (var detailIndex = 0; detailIndex < details.Count; detailIndex++)
        {
            var detail = details[detailIndex];
            var interactive = detailIndex < _detailStates.Length;
            var highlighted = interactive &&
                              (detailIndex == _hoveredDetailIndex || detailIndex == _keyboardDetailIndex);
            var state = interactive ? _detailStates[detailIndex] : null;
            if (_pagePreview is not null)
            {
                if (interactive)
                {
                    graphics.FillRectangle(
                        FoundryTheme.WithAlpha(FoundryTheme.CanvasSurface, 175),
                        detail.X, detail.Y, detail.Width, Math.Min(24, detail.Height));
                    if (detail.Height >= 30)
                        graphics.FillRectangle(
                            FoundryTheme.WithAlpha(FoundryTheme.CanvasSurface, 165),
                            detail.X, detail.Bottom - 17, detail.Width, 17);
                }
            }
            else if (!string.IsNullOrWhiteSpace(_pagePreviewMessage))
            {
                // Keep the sheet-level render status visible instead of
                // covering it with the schematic detail surfaces.
            }
            else if (state?.NamedViewPreview is { } namedViewPreview)
            {
                graphics.DrawImage(namedViewPreview, detail);
                graphics.FillRectangle(
                    FoundryTheme.WithAlpha(FoundryTheme.CanvasSurface, 175),
                    detail.X, detail.Y, detail.Width, Math.Min(24, detail.Height));
                if (detail.Height >= 30)
                    graphics.FillRectangle(
                        FoundryTheme.WithAlpha(FoundryTheme.CanvasSurface, 165),
                        detail.X, detail.Bottom - 17, detail.Width, 17);
            }
            else
            {
                graphics.FillRectangle(FoundryTheme.ToolbarButtonBackground, detail);
                if (!string.IsNullOrWhiteSpace(state?.PreviewMessage) && detail.Height >= 46)
                {
                    var messageY = detail.Y + Math.Max(22, (detail.Height - 10) / 2);
                    DrawCentered(
                        graphics,
                        _detailMetaFont,
                        FoundryTheme.MutedText,
                        state.PreviewMessage,
                        detail,
                        messageY);
                }
            }
            graphics.DrawRectangle(new Pen(
                state?.Changed == true
                    ? FoundryTheme.WarningAccent
                    : highlighted
                        ? FoundryTheme.PrimaryText
                        : FoundryTheme.CanvasBorder,
                state?.Changed == true || highlighted ? 2 : 1), detail);
            if (!interactive) continue;
            var previewState = _detailStates[detailIndex];
            if (detail.Width < 28 || detail.Height < 20)
            {
                DrawCentered(graphics, _detailFont, FoundryTheme.PrimaryText,
                    (detailIndex + 1).ToString(), detail, detail.Y + (detail.Height - 10) / 2);
                continue;
            }

            var badge = new RectangleF(detail.X + 4, detail.Y + 4, 14, 14);
            graphics.FillEllipse(FoundryTheme.CanvasSubtleSurface, badge);
            graphics.DrawEllipse(new Pen(FoundryTheme.CanvasBorder, 1), badge);
            DrawCentered(graphics, _detailFont, FoundryTheme.PrimaryText,
                (detailIndex + 1).ToString(), badge, badge.Y + 2);

            DrawFittedText(
                graphics,
                _detailFont,
                previewState.HasNamedView || previewState.NamedViewIsMixed
                    ? FoundryTheme.PrimaryText
                    : FoundryTheme.MutedText,
                previewState.NamedViewLabel,
                detail.X + 22,
                detail.Y + 5,
                Math.Max(8, detail.Width - 31));
            if (detail.Height >= 30)
                DrawFittedText(
                    graphics,
                    _detailMetaFont,
                    previewState.HasDisplayMode || previewState.DisplayModeIsMixed
                        ? FoundryTheme.PrimaryText
                        : FoundryTheme.MutedText,
                    previewState.DisplayModeLabel,
                    detail.X + 5,
                    detail.Bottom - 12,
                    Math.Max(8, detail.Width - 15));
            graphics.DrawText(_detailMetaFont, FoundryTheme.MutedText,
                detail.Right - 9, detail.Bottom - 12, "›");
        }
        if (HasFocus)
            graphics.DrawRectangle(new Pen(FoundryTheme.PrimaryText, 2), page);
    }

    private void OnMouseDown(object? sender, MouseEventArgs eventArgs)
    {
        if (!eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
        Focus();
        var detailIndex = HitTestDetail(eventArgs.Location);
        if (detailIndex >= 0)
        {
            _keyboardDetailIndex = detailIndex;
            DetailActivated?.Invoke(this, new DetailActivatedEventArgs(detailIndex));
            eventArgs.Handled = true;
            Invalidate();
            return;
        }
    }

    private void OnMouseMove(object? sender, MouseEventArgs eventArgs)
    {
        var next = HitTestDetail(eventArgs.Location);
        if (_hoveredDetailIndex == next) return;
        _hoveredDetailIndex = next;
        ToolTip = next >= 0
            ? $"{_detailStates[next].Label}\nNamed view: {_detailStates[next].NamedViewLabel}\nDisplay mode: {_detailStates[next].DisplayModeLabel}\nSet named view and display mode."
            : "Sheet preview. Unconfigured details are labeled Set detail.";
        Invalidate();
    }

    private void OnMouseLeave(object? sender, MouseEventArgs eventArgs)
    {
        if (_hoveredDetailIndex < 0) return;
        _hoveredDetailIndex = -1;
        ToolTip = "Sheet preview. Unconfigured details are labeled Set detail.";
        Invalidate();
    }

    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key is Keys.Left or Keys.Up or Keys.Right or Keys.Down)
        {
            if (_detailStates.Length == 0) return;
            var delta = eventArgs.Key is Keys.Left or Keys.Up ? -1 : 1;
            _keyboardDetailIndex = _keyboardDetailIndex < 0
                ? 0
                : Math.Clamp(_keyboardDetailIndex + delta, 0, _detailStates.Length - 1);
            Invalidate();
            eventArgs.Handled = true;
            return;
        }
        if (eventArgs.Key == Keys.Enter && _keyboardDetailIndex >= 0)
        {
            DetailActivated?.Invoke(this, new DetailActivatedEventArgs(_keyboardDetailIndex));
            eventArgs.Handled = true;
            return;
        }
        if (eventArgs.Key == Keys.Escape && _keyboardDetailIndex >= 0)
        {
            _keyboardDetailIndex = -1;
            Invalidate();
            eventArgs.Handled = true;
            return;
        }
    }

    private RectangleF PageBounds()
    {
        return TitleBlockPreviewTray.PageBounds(
            _paper,
            new RectangleF(0, 0, Math.Max(1, Width), Math.Max(1, Height)));
    }

    private RectangleF DetailContentBounds(RectangleF page)
    {
        if (_titleBlock?.BuiltInKind is not { } kind) return page;
        try
        {
            var layout = AdaptiveTitleBlockLayoutSolver.Solve(kind, _paper);
            return new RectangleF(
                page.X + (float)(layout.Content.Left / _paper.Width * page.Width),
                page.Bottom - (float)(layout.Content.Top / _paper.Height * page.Height),
                (float)(layout.Content.Width / _paper.Width * page.Width),
                (float)(layout.Content.Height / _paper.Height * page.Height));
        }
        catch (Exception)
        {
            return page;
        }
    }

    private IReadOnlyList<RectangleF> PreviewDetailBounds(RectangleF page)
    {
        var choice = _choices[_selectedIndex];
        var targetContent = DetailContentBounds(page);
        if (_titleBlock?.BuiltInKind is null)
            return LayoutPreviewTray.DetailBounds(choice, targetContent);

        var sourceDetails = LayoutPreviewTray.DetailBounds(choice, page);
        if (sourceDetails.Count == 0) return sourceDetails;
        var sourceLeft = sourceDetails.Min(detail => detail.Left);
        var sourceTop = sourceDetails.Min(detail => detail.Top);
        var sourceRight = sourceDetails.Max(detail => detail.Right);
        var sourceBottom = sourceDetails.Max(detail => detail.Bottom);
        var sourceWidth = Math.Max(0.001f, sourceRight - sourceLeft);
        var sourceHeight = Math.Max(0.001f, sourceBottom - sourceTop);
        return sourceDetails.Select(detail => new RectangleF(
            targetContent.Left + (detail.Left - sourceLeft) / sourceWidth * targetContent.Width,
            targetContent.Top + (detail.Top - sourceTop) / sourceHeight * targetContent.Height,
            detail.Width / sourceWidth * targetContent.Width,
            detail.Height / sourceHeight * targetContent.Height)).ToArray();
    }

    private static void DrawCentered(
        Graphics graphics,
        Font font,
        Color color,
        string text,
        RectangleF bounds,
        float y)
    {
        var fitted = LayoutPreviewTray.FitText(graphics, font, text, Math.Max(4, bounds.Width - 4));
        var size = graphics.MeasureString(font, fitted);
        graphics.DrawText(font, color, bounds.X + Math.Max(2, (bounds.Width - size.Width) / 2), y, fitted);
    }

    private static void DrawFittedText(
        Graphics graphics,
        Font font,
        Color color,
        string text,
        float x,
        float y,
        float width) => graphics.DrawText(
        font,
        color,
        x,
        y,
        LayoutPreviewTray.FitText(graphics, font, text, width));

    private int HitTestDetail(PointF location)
    {
        if (_detailStates.Length == 0) return -1;
        var details = PreviewDetailBounds(PageBounds());
        for (var index = 0; index < Math.Min(details.Count, _detailStates.Length); index++)
            if (details[index].Contains(location)) return index;
        return -1;
    }
}


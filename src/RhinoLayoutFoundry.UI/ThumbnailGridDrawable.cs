using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Observer;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

internal sealed class ThumbnailGridDrawable : Drawable
{
    private readonly Font _nameFont = SystemFonts.Bold(10);
    private readonly Font _metaFont = SystemFonts.Default(8);
    private readonly Dictionary<Guid, PreviewEntry> _previews = [];
    private ObserverSnapshot _snapshot = ObserverSnapshot.NoDocument;
    private ThumbnailGridLayout _layout = ThumbnailGridLayout.Create(0, 1, 190);
    private HashSet<OverviewNodeKey> _selection = [];
    private Guid? _selectionAnchor;

    internal ThumbnailGridDrawable()
        : base(true)
    {
        BackgroundColor = FoundryTheme.PanelBackground;
        CanFocus = true;
        Paint += OnPaint;
        MouseDown += OnMouseDown;
        MouseDoubleClick += OnMouseDoubleClick;
        KeyDown += OnKeyDown;
    }

    internal event EventHandler<ThumbnailGridSelectionEventArgs>? SelectionRequested;
    internal event EventHandler<ThumbnailGridNavigationEventArgs>? NavigationRequested;

    internal ThumbnailGridLayout GridLayout => _layout;

    internal void SetSnapshot(ObserverSnapshot snapshot, double availableWidth, double requestedCardWidth)
    {
        _snapshot = snapshot ?? ObserverSnapshot.NoDocument;
        SetGridSize(availableWidth, requestedCardWidth);
        var currentIds = _snapshot.Sheets.Select(sheet => sheet.PageViewId).ToHashSet();
        foreach (var stale in _previews.Keys.Where(id => !currentIds.Contains(id)).ToArray())
        {
            _previews[stale].Bitmap.Dispose();
            _previews.Remove(stale);
        }
        Invalidate();
    }

    internal void SetGridSize(double availableWidth, double requestedCardWidth)
    {
        _layout = ThumbnailGridLayout.Create(_snapshot.Sheets.Count, availableWidth, requestedCardWidth);
        Size = new Size(
            Math.Max(1, (int)Math.Ceiling(availableWidth)),
            Math.Max(1, (int)Math.Ceiling(_layout.ContentHeight)));
        Invalidate();
    }

    internal void SetSelection(IReadOnlyCollection<OverviewNodeKey> selection)
    {
        _selection = selection.ToHashSet();
        Invalidate();
    }

    internal IReadOnlyList<ObserverSheetSnapshot> VisibleSheets(Rectangle visibleRect, int overscanRows = 1) =>
        _layout.VisibleIndices(visibleRect.Top, visibleRect.Bottom, overscanRows)
            .Select(index => _snapshot.Sheets[index])
            .ToArray();

    internal void SetPreview(OverviewThumbnailKey key, Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (_previews.Remove(key.SheetPageViewId, out var previous)) previous.Bitmap.Dispose();
        _previews[key.SheetPageViewId] = new PreviewEntry(key, bitmap);
        Invalidate();
    }

    internal bool HasCurrentPreview(Guid sheetId, long contentVersion, int bucket) =>
        _previews.TryGetValue(sheetId, out var preview) &&
        preview.Key.ContentVersion == contentVersion &&
        preview.Key.ResolutionBucket == bucket;

    internal int CurrentPreviewBucket(Guid sheetId) =>
        _previews.TryGetValue(sheetId, out var preview) ? preview.Key.ResolutionBucket : 0;

    internal void PrunePreviews(IReadOnlySet<Guid> retainedSheetIds)
    {
        foreach (var pair in _previews.Where(pair => !retainedSheetIds.Contains(pair.Key)).ToArray())
        {
            pair.Value.Bitmap.Dispose();
            _previews.Remove(pair.Key);
        }
    }

    internal void InvalidatePreviews(IReadOnlySet<Guid>? sheetIds = null)
    {
        foreach (var pair in _previews
                     .Where(pair => sheetIds is null || sheetIds.Count == 0 || sheetIds.Contains(pair.Key))
                     .ToArray())
        {
            pair.Value.Bitmap.Dispose();
            _previews.Remove(pair.Key);
        }
        Invalidate();
    }

    internal void ReleasePreviews() => InvalidatePreviews();

    private void OnPaint(object? sender, PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        graphics.AntiAlias = true;
        graphics.FillRectangle(FoundryTheme.PanelBackground, eventArgs.ClipRectangle);
        if (!_snapshot.HasDocument)
        {
            DrawEmpty(graphics, "Open a Rhino document to browse layout thumbnails.");
            return;
        }
        if (_snapshot.Sheets.Count == 0)
        {
            DrawEmpty(graphics, "Create a layout to populate thumbnail view.");
            return;
        }

        foreach (var index in _layout.VisibleIndices(
                     eventArgs.ClipRectangle.Top,
                     eventArgs.ClipRectangle.Bottom,
                     overscanRows: 0))
        {
            DrawSheet(graphics, index, _snapshot.Sheets[index]);
        }
    }

    private void DrawSheet(Graphics graphics, int index, ObserverSheetSnapshot sheet)
    {
        var cell = _layout.CellBounds(index);
        var image = PageBounds(cell, sheet);
        var selected = _selection.Contains(new OverviewNodeKey(OverviewNodeKind.Sheet, sheet.PageViewId));
        graphics.FillRectangle(Color.FromArgb(48, 0, 0, 0),
            image.X + 4, image.Y + 5, image.Width, image.Height);
        graphics.FillRectangle(Colors.White, image);
        if (_previews.TryGetValue(sheet.PageViewId, out var preview) &&
            preview.Key.ContentVersion == sheet.PreviewContentVersion)
        {
            graphics.DrawImage(preview.Bitmap, image);
        }
        else
        {
            graphics.FillRectangle(Color.FromArgb(255, 232, 232, 228), image);
            foreach (var detail in sheet.Details)
            {
                var bounds = new RectangleF(
                    image.X + (float)(detail.NormalizedBounds.X * image.Width),
                    image.Y + (float)(detail.NormalizedBounds.Y * image.Height),
                    (float)(detail.NormalizedBounds.Width * image.Width),
                    (float)(detail.NormalizedBounds.Height * image.Height));
                graphics.FillRectangle(Color.FromArgb(255, 214, 216, 218), bounds);
                graphics.DrawRectangle(Color.FromArgb(130, 100, 103, 106), bounds);
            }
        }

        graphics.DrawRectangle(
            new Pen(selected ? FoundryTheme.SelectionAccent : Color.FromArgb(125, 90, 90, 90),
                selected ? 3 : 1),
            image);
        graphics.FillEllipse(
            sheet.IncludeInPrintAll ? Color.FromArgb(255, 245, 188, 32) : Color.FromArgb(255, 95, 95, 95),
            image.Right - 15,
            image.Top + 7,
            8,
            8);
        graphics.DrawText(_nameFont, FoundryTheme.PrimaryText, (float)cell.X, image.Bottom + 8, sheet.Name);
        graphics.DrawText(
            _metaFont,
            FoundryTheme.MutedText,
            (float)cell.X,
            image.Bottom + 25,
            $"{sheet.Details.Count} detail{(sheet.Details.Count == 1 ? string.Empty : "s")}");
    }

    private RectangleF PageBounds(ThumbnailGridRect cell, ObserverSheetSnapshot sheet)
    {
        var availableWidth = Math.Max(1, cell.Width - 12);
        var availableHeight = Math.Max(1, _layout.ImageAreaHeight - 12);
        var paperWidth = sheet.PaperWidthMillimeters > 0 ? sheet.PaperWidthMillimeters : 1.414;
        var paperHeight = sheet.PaperHeightMillimeters > 0 ? sheet.PaperHeightMillimeters : 1;
        var scale = Math.Min(availableWidth / paperWidth, availableHeight / paperHeight);
        var width = paperWidth * scale;
        var height = paperHeight * scale;
        return new RectangleF(
            (float)(cell.X + (cell.Width - width) / 2),
            (float)(cell.Y + (_layout.ImageAreaHeight - height) / 2),
            (float)width,
            (float)height);
    }

    private int? HitIndex(PointF location)
    {
        if (_snapshot.Sheets.Count == 0 || location.Y < _layout.Padding) return null;
        var row = (int)Math.Floor((location.Y - _layout.Padding) / _layout.RowHeight);
        if (row < 0 || row >= _layout.Rows) return null;
        for (var column = 0; column < _layout.Columns; column++)
        {
            var index = row * _layout.Columns + column;
            if (index >= _snapshot.Sheets.Count) break;
            var cell = _layout.CellBounds(index);
            if (location.X >= cell.X && location.X <= cell.X + cell.Width &&
                location.Y >= cell.Y && location.Y <= cell.Bottom)
                return index;
        }
        return null;
    }

    private void OnMouseDown(object? sender, MouseEventArgs eventArgs)
    {
        if (!eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
        Focus();
        var index = HitIndex(eventArgs.Location);
        if (index is null)
        {
            SelectionRequested?.Invoke(this, new ThumbnailGridSelectionEventArgs([], null));
            return;
        }

        var sheet = _snapshot.Sheets[index.Value];
        var key = new OverviewNodeKey(OverviewNodeKind.Sheet, sheet.PageViewId);
        var selected = IsAdditive(eventArgs.Modifiers) ? _selection.ToHashSet() : [];
        if (eventArgs.Modifiers.HasFlag(Keys.Shift) && _selectionAnchor is { } anchor)
        {
            var anchorIndex = _snapshot.Sheets.ToList().FindIndex(candidate => candidate.PageViewId == anchor);
            if (anchorIndex >= 0)
            {
                foreach (var rangeIndex in Enumerable.Range(
                             Math.Min(anchorIndex, index.Value),
                             Math.Abs(anchorIndex - index.Value) + 1))
                    selected.Add(new OverviewNodeKey(OverviewNodeKind.Sheet, _snapshot.Sheets[rangeIndex].PageViewId));
            }
        }
        else if (IsAdditive(eventArgs.Modifiers) && !selected.Add(key))
        {
            selected.Remove(key);
        }
        else
        {
            selected.Add(key);
        }

        _selectionAnchor = sheet.PageViewId;
        SelectionRequested?.Invoke(this, new ThumbnailGridSelectionEventArgs(selected.ToArray(), key));
        eventArgs.Handled = true;
    }

    private void OnMouseDoubleClick(object? sender, MouseEventArgs eventArgs)
    {
        var index = HitIndex(eventArgs.Location);
        if (index is null) return;
        NavigationRequested?.Invoke(
            this,
            new ThumbnailGridNavigationEventArgs(
                new OverviewNavigationTarget(_snapshot.Sheets[index.Value].PageViewId)));
        eventArgs.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (_snapshot.Sheets.Count == 0) return;
        var selectedId = _selection.FirstOrDefault(key => key.Kind == OverviewNodeKind.Sheet).Id;
        var index = selectedId == Guid.Empty
            ? 0
            : Math.Max(0, _snapshot.Sheets.ToList().FindIndex(sheet => sheet.PageViewId == selectedId));
        var next = eventArgs.Key switch
        {
            Keys.Left => index - 1,
            Keys.Right => index + 1,
            Keys.Up => index - _layout.Columns,
            Keys.Down => index + _layout.Columns,
            _ => index,
        };
        if (eventArgs.Key == Keys.Enter)
        {
            NavigationRequested?.Invoke(
                this,
                new ThumbnailGridNavigationEventArgs(
                    new OverviewNavigationTarget(_snapshot.Sheets[index].PageViewId)));
            eventArgs.Handled = true;
            return;
        }
        if (next == index && eventArgs.Key is not (Keys.Left or Keys.Right or Keys.Up or Keys.Down)) return;
        next = Math.Clamp(next, 0, _snapshot.Sheets.Count - 1);
        var sheet = _snapshot.Sheets[next];
        var key = new OverviewNodeKey(OverviewNodeKind.Sheet, sheet.PageViewId);
        _selectionAnchor = sheet.PageViewId;
        SelectionRequested?.Invoke(this, new ThumbnailGridSelectionEventArgs([key], key));
        eventArgs.Handled = true;
    }

    private static bool IsAdditive(Keys modifiers) =>
        modifiers.HasFlag(Keys.Application) || modifiers.HasFlag(Keys.Control) || modifiers.HasFlag(Keys.Shift);

    private static void DrawEmpty(Graphics graphics, string message)
    {
        graphics.DrawText(SystemFonts.Bold(13), FoundryTheme.PrimaryText, 28, 28, "Thumbnail view");
        graphics.DrawText(SystemFonts.Default(), FoundryTheme.MutedText, 28, 56, message);
    }

    private sealed record PreviewEntry(OverviewThumbnailKey Key, Bitmap Bitmap);
}

internal sealed class ThumbnailGridSelectionEventArgs(
    IReadOnlyCollection<OverviewNodeKey> selection,
    OverviewNodeKey? anchor) : EventArgs
{
    internal IReadOnlyCollection<OverviewNodeKey> Selection { get; } = selection;
    internal OverviewNodeKey? Anchor { get; } = anchor;
}

internal sealed class ThumbnailGridNavigationEventArgs(OverviewNavigationTarget target) : EventArgs
{
    internal OverviewNavigationTarget Target { get; } = target;
}

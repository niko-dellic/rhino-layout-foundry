using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Observer;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

internal sealed class ObserverCanvasDrawable : Drawable
{
    internal const string NamedViewDragType = "application/x-layout-foundry-named-view";
    private readonly Font _folderFont = SystemFonts.Bold(11);
    private readonly Font _sheetFont = SystemFonts.Bold(10);
    private readonly Font _smallFont = SystemFonts.Default(8);
    private readonly Dictionary<Guid, PreviewEntry> _previews = [];
    private ObserverSnapshot _snapshot = ObserverSnapshot.NoDocument;
    private ObserverBoardLayout _layout = ObserverBoardLayout.Empty;
    private ObserverSpatialIndex _spatialIndex = new(ObserverBoardLayout.Empty);
    private ObserverCamera _camera = ObserverCamera.Default;
    private HashSet<OverviewNodeKey> _selection = [];
    private DragMode _dragMode;
    private ObserverPoint _pressScreen;
    private ObserverPoint _pressWorld;
    private ObserverPoint _lastScreen;
    private ObserverPoint _dragWorldDelta;
    private Guid? _dragFolderId;
    private Guid? _reorderSheetId;
    private ObserverRect? _lassoWorld;
    private ObserverPoint _contextWorld;
    private bool _spaceHeld;

    internal ObserverCanvasDrawable()
        : base(true)
    {
        BackgroundColor = FoundryTheme.CanvasBackground;
        CanFocus = true;
        AllowDrop = true;
        Paint += OnPaint;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseDoubleClick += OnMouseDoubleClick;
        MouseWheel += OnMouseWheel;
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        DragOver += OnDragOver;
        DragDrop += OnDragDrop;
    }

    internal event EventHandler? ViewChanged;
    internal event EventHandler<ObserverBoardStateRequestedEventArgs>? BoardStateRequested;
    internal event EventHandler<ObserverSelectionRequestedEventArgs>? SelectionRequested;
    internal event EventHandler<ObserverNavigationRequestedEventArgs>? NavigationRequested;
    internal event EventHandler<ObserverHierarchyMoveRequestedEventArgs>? HierarchyMoveRequested;
    internal event EventHandler<ObserverReorderRequestedEventArgs>? ReorderRequested;
    internal event EventHandler<ObserverReorderStepRequestedEventArgs>? ReorderStepRequested;
    internal event EventHandler<ObserverNamedViewRequestedEventArgs>? NamedViewRequested;
    internal event EventHandler<ObserverContextRequestedEventArgs>? ContextRequested;
    internal event EventHandler? DeleteRequested;
    internal event EventHandler? TidyRequested;
    internal event EventHandler? ExitWorkspaceRequested;

    internal ObserverCamera Camera => _camera;
    internal ObserverBoardLayout BoardLayout => _layout;
    internal ObserverSnapshot Snapshot => _snapshot;
    internal bool ExitWorkspaceOnEscape { get; set; }

    internal void SetSnapshot(ObserverSnapshot snapshot, bool fit)
    {
        _snapshot = snapshot ?? ObserverSnapshot.NoDocument;
        _layout = new ObserverPlacementPlanner().Arrange(_snapshot);
        _spatialIndex = new ObserverSpatialIndex(_layout);
        _selection.RemoveWhere(key => !ContainsKey(key));
        if (fit && !_layout.Bounds.IsEmpty)
        {
            FitAll();
        }
        else
        {
            Invalidate();
        }
    }

    internal void SetSelection(IEnumerable<OverviewNodeKey> selection)
    {
        _selection = selection.Where(ContainsKey).ToHashSet();
        Invalidate();
    }

    internal void FitAll()
    {
        _camera = ObserverCamera.Fit(_layout.Bounds, ViewportSize());
        NotifyViewChanged();
    }

    internal void FocusSelection()
    {
        var bounds = SelectedBounds();
        if (!bounds.IsEmpty)
        {
            _camera = ObserverCamera.Fit(bounds, ViewportSize(), 72);
            NotifyViewChanged();
        }
    }

    internal void ResetView()
    {
        _camera = ObserverCamera.Default;
        NotifyViewChanged();
    }

    internal void SetCamera(ObserverCamera camera)
    {
        _camera = camera ?? ObserverCamera.Default;
        NotifyViewChanged();
    }

    internal void Zoom(double factor)
    {
        var viewport = ViewportSize();
        _camera = _camera.ZoomAt(
            new ObserverPoint(viewport.Width / 2, viewport.Height / 2),
            factor,
            viewport);
        NotifyViewChanged();
    }

    internal IReadOnlyList<ObserverSheetCard> VisibleSheets(bool includeOverscan)
    {
        var visible = _camera.VisibleWorld(ViewportSize());
        var overscan = includeOverscan ? 160 / _camera.Zoom : 0;
        return _spatialIndex.QuerySheets(visible.Inflate(overscan));
    }

    internal int CurrentPreviewBucket(Guid sheetId) =>
        _previews.TryGetValue(sheetId, out var preview) ? preview.Key.ResolutionBucket : 0;

    internal bool HasCurrentPreview(Guid sheetId, long contentVersion, int minimumBucket) =>
        _previews.TryGetValue(sheetId, out var preview) &&
        preview.Key.ContentVersion == contentVersion &&
        preview.Key.ResolutionBucket >= minimumBucket;

    internal void SetPreview(OverviewThumbnailKey key, Bitmap bitmap)
    {
        if (_previews.Remove(key.SheetPageViewId, out var previous) &&
            !ReferenceEquals(previous.Bitmap, bitmap))
        {
            previous.Bitmap.Dispose();
        }

        _previews[key.SheetPageViewId] = new PreviewEntry(key, bitmap);
        Invalidate();
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

    internal void PrunePreviews(IReadOnlySet<Guid> retainedSheetIds)
    {
        foreach (var pair in _previews.Where(pair => !retainedSheetIds.Contains(pair.Key)).ToArray())
        {
            pair.Value.Bitmap.Dispose();
            _previews.Remove(pair.Key);
        }
    }

    internal void ReleasePreviews()
    {
        foreach (var preview in _previews.Values) preview.Bitmap.Dispose();
        _previews.Clear();
    }

    private void OnPaint(object? sender, PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        graphics.AntiAlias = true;
        graphics.FillRectangle(FoundryTheme.CanvasBackground, eventArgs.ClipRectangle);
        if (!_snapshot.HasDocument)
        {
            DrawEmpty(graphics, "Open a Rhino document to use the observer canvas.");
            return;
        }

        if (_snapshot.Sheets.Count == 0)
        {
            DrawEmpty(graphics, "Create a layout to begin arranging the board.");
            return;
        }

        var viewport = ViewportSize();
        var visibleWorld = _camera.VisibleWorld(viewport).Inflate(160 / _camera.Zoom);
        DrawGrid(graphics, viewport);
        foreach (var frame in _spatialIndex.QueryFolders(visibleWorld))
        {
            DrawFolder(graphics, frame, viewport);
        }

        foreach (var card in _spatialIndex.QuerySheets(visibleWorld))
        {
            DrawSheet(graphics, card, viewport);
        }

        if (_lassoWorld is { } lasso)
        {
            var screen = ScreenRect(lasso, viewport);
            var crossing = lasso.Width < 0;
            graphics.FillRectangle(FoundryTheme.SelectionWindowFill(crossing), screen);
            graphics.DrawRectangle(new Pen(FoundryTheme.SelectionWindowStroke(crossing), 1), screen);
        }
    }

    private void DrawGrid(Graphics graphics, ObserverSize viewport)
    {
        if (_camera.Zoom < 0.18) return;
        var spacing = 40d;
        var visible = _camera.VisibleWorld(viewport);
        var startX = Math.Floor(visible.Left / spacing) * spacing;
        var startY = Math.Floor(visible.Top / spacing) * spacing;
        var color = FoundryTheme.CanvasGrid;
        for (var x = startX; x <= visible.Right; x += spacing)
        for (var y = startY; y <= visible.Bottom; y += spacing)
        {
            var point = _camera.WorldToScreen(new ObserverPoint(x, y), viewport);
            graphics.FillRectangle(color, (float)point.X, (float)point.Y, 1, 1);
        }
    }

    private void DrawFolder(Graphics graphics, ObserverFolderFrame frame, ObserverSize viewport)
    {
        var bounds = ScreenRect(PreviewBounds(frame.Bounds, frame.Folder.Id), viewport);
        if (bounds.Width < 8 || bounds.Height < 8) return;
        var selected = _selection.Contains(new OverviewNodeKey(OverviewNodeKind.Folder, frame.Folder.Id));
        var outline = selected
            ? FoundryTheme.SelectionAccent
            : FoundryTheme.CanvasBorder;
        graphics.FillRectangle(FoundryTheme.WithAlpha(FoundryTheme.CanvasSurface, 118), bounds);
        graphics.DrawRectangle(new Pen(outline, selected ? 2 : 1), bounds);
        var headerHeight = Math.Max(22, ObserverPlacementPlanner.FolderHeaderHeight * _camera.Zoom);
        graphics.FillRectangle(FoundryTheme.WithAlpha(FoundryTheme.CanvasSubtleSurface, 220),
            bounds.X, bounds.Y, bounds.Width, (float)Math.Min(bounds.Height, headerHeight));
        graphics.DrawText(
            _folderFont,
            FoundryTheme.PrimaryText,
            bounds.X + 10,
            bounds.Y + 6,
            $"▾  {frame.Folder.Name}   ·   {frame.DirectSheetCount} layout{(frame.DirectSheetCount == 1 ? string.Empty : "s")}");
    }

    private void DrawSheet(Graphics graphics, ObserverSheetCard card, ObserverSize viewport)
    {
        var worldBounds = PreviewBounds(card.Bounds, card.Sheet.PageViewId);
        var bounds = ScreenRect(worldBounds, viewport);
        if (bounds.Width < 3 || bounds.Height < 3) return;
        var key = new OverviewNodeKey(OverviewNodeKind.Sheet, card.Sheet.PageViewId);
        var selected = _selection.Contains(key);
        var hasSelectedDetail = card.Sheet.Details.Any(detail =>
            _selection.Contains(new OverviewNodeKey(OverviewNodeKind.Detail, detail.DetailViewportId)));
        graphics.FillRectangle(Color.FromArgb(0, 0, 0, 45),
            bounds.X + 4, bounds.Y + 5, bounds.Width, bounds.Height);
        graphics.FillRectangle(Colors.White, bounds);
        if (_previews.TryGetValue(card.Sheet.PageViewId, out var preview) &&
            preview.Key.ContentVersion == card.Sheet.PreviewContentVersion)
        {
            graphics.DrawImage(preview.Bitmap, bounds);
        }
        else
        {
            DrawPlaceholder(graphics, card, bounds);
        }

        var border = selected || hasSelectedDetail
            ? FoundryTheme.SelectionAccent
            : FoundryTheme.CanvasBorder;
        graphics.DrawRectangle(new Pen(border, selected ? 3 : hasSelectedDetail ? 2 : 1), bounds);
        if (bounds.Width >= 70 && bounds.Height >= 50)
        {
            DrawDetailOverlays(graphics, card, bounds, selected);
            graphics.FillRectangle(
                card.Sheet.IncludeInPrintAll
                    ? FoundryTheme.PrimaryText
                    : FoundryTheme.MutedText,
                bounds.Right - 18,
                bounds.Top + 6,
                10,
                10);
            graphics.DrawRectangle(FoundryTheme.CanvasBorder,
                bounds.Right - 18, bounds.Top + 6, 10, 10);
            graphics.FillRectangle(FoundryTheme.WithAlpha(FoundryTheme.CanvasSubtleSurface, 230),
                bounds.Left + 6, bounds.Top + 6, 12, 12);
            graphics.DrawText(_smallFont, Colors.White, bounds.Left + 8, bounds.Top + 5, "↕");
        }

        graphics.DrawText(
            _sheetFont,
            FoundryTheme.PrimaryText,
            bounds.Left,
            bounds.Bottom + 5,
            card.Sheet.Name);
        if (selected && _dragMode == DragMode.Sheets && _dragWorldDelta != new ObserverPoint())
        {
            graphics.DrawRectangle(
                new Pen(FoundryTheme.WithAlpha(FoundryTheme.SelectionAccent, 180), 1),
                bounds);
        }
    }

    private static void DrawPlaceholder(Graphics graphics, ObserverSheetCard card, RectangleF bounds)
    {
        graphics.FillRectangle(Color.FromArgb(236, 236, 232, 255), bounds);
        foreach (var detail in card.Sheet.Details)
        {
            var rect = DetailScreenRect(detail.NormalizedBounds, bounds);
            graphics.FillRectangle(Color.FromArgb(218, 220, 222, 255), rect);
            graphics.DrawRectangle(Color.FromArgb(105, 108, 110, 160), rect);
        }

        graphics.DrawLine(Color.FromArgb(110, 110, 110, 100),
            bounds.Left + 8, bounds.Bottom - 12, bounds.Right - 8, bounds.Bottom - 12);
    }

    private void DrawDetailOverlays(
        Graphics graphics,
        ObserverSheetCard card,
        RectangleF bounds,
        bool sheetSelected)
    {
        foreach (var detail in card.Sheet.Details)
        {
            var detailSelected = _selection.Contains(
                new OverviewNodeKey(OverviewNodeKind.Detail, detail.DetailViewportId));
            if (!sheetSelected && !detailSelected) continue;
            var rect = DetailScreenRect(detail.NormalizedBounds, bounds);
            graphics.DrawRectangle(
                new Pen(
                    FoundryTheme.WithAlpha(
                        FoundryTheme.SelectionAccent,
                        detailSelected ? 255 : 180),
                    detailSelected ? 3 : 1),
                rect);
        }
    }

    private void DrawEmpty(Graphics graphics, string message)
    {
        graphics.DrawText(
            SystemFonts.Bold(13),
            FoundryTheme.PrimaryText,
            32,
            32,
            "Observer canvas");
        graphics.DrawText(SystemFonts.Default(), FoundryTheme.MutedText, 32, 60, message);
    }

    private void OnMouseDown(object? sender, MouseEventArgs eventArgs)
    {
        Focus();
        _pressScreen = Point(eventArgs.Location);
        _lastScreen = _pressScreen;
        _pressWorld = _camera.ScreenToWorld(_pressScreen, ViewportSize());
        _contextWorld = _pressWorld;
        _dragWorldDelta = new ObserverPoint();
        if (eventArgs.Buttons.HasFlag(MouseButtons.Alternate))
        {
            SelectAt(_pressWorld, eventArgs.Modifiers, preserveExistingIfHit: true);
            ContextRequested?.Invoke(this, new ObserverContextRequestedEventArgs(
                _pressWorld,
                eventArgs.Location));
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.Buttons.HasFlag(MouseButtons.Middle) ||
            (_spaceHeld && eventArgs.Buttons.HasFlag(MouseButtons.Primary)))
        {
            _dragMode = DragMode.Pan;
            eventArgs.Handled = true;
            return;
        }

        if (!eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
        var card = _spatialIndex.HitSheet(_pressWorld);
        if (card is not null)
        {
            var screenBounds = ScreenRect(card.Bounds, ViewportSize());
            var reorderHandle = eventArgs.Location.X <= screenBounds.Left + 24 &&
                                eventArgs.Location.Y <= screenBounds.Top + 24;
            var detail = reorderHandle ? null : HitDetail(card, _pressWorld);
            if (detail is not null)
                SelectKey(new OverviewNodeKey(OverviewNodeKind.Detail, detail.DetailViewportId), eventArgs.Modifiers);
            else
                SelectSheet(card.Sheet.PageViewId, eventArgs.Modifiers);
            _reorderSheetId = card.Sheet.PageViewId;
            _dragMode = reorderHandle
                ? DragMode.Reorder
                : DragMode.Sheets;
        }
        else
        {
            var folder = _spatialIndex.HitFolderHeader(
                _pressWorld,
                ObserverPlacementPlanner.FolderHeaderHeight);
            if (folder is not null)
            {
                SelectFolder(folder.Folder.Id, eventArgs.Modifiers);
                _dragFolderId = folder.Folder.Id;
                _dragMode = DragMode.Folder;
            }
            else
            {
                if (!IsAdditive(eventArgs.Modifiers))
                    SelectionRequested?.Invoke(this, new ObserverSelectionRequestedEventArgs([], null));
                _dragMode = DragMode.Lasso;
                _lassoWorld = new ObserverRect(_pressWorld.X, _pressWorld.Y, 0, 0);
            }
        }

        eventArgs.Handled = true;
    }

    private void OnMouseMove(object? sender, MouseEventArgs eventArgs)
    {
        var current = Point(eventArgs.Location);
        if (_dragMode == DragMode.None) return;
        if (_dragMode == DragMode.Pan)
        {
            _camera = _camera.PanScreen(current.X - _lastScreen.X, current.Y - _lastScreen.Y);
            _lastScreen = current;
            NotifyViewChanged();
        }
        else
        {
            var world = _camera.ScreenToWorld(current, ViewportSize());
            _dragWorldDelta = world - _pressWorld;
            if (_dragMode == DragMode.Lasso)
            {
                _lassoWorld = new ObserverRect(
                    _pressWorld.X,
                    _pressWorld.Y,
                    world.X - _pressWorld.X,
                    world.Y - _pressWorld.Y);
            }

            Invalidate();
        }

        eventArgs.Handled = true;
    }

    private void OnMouseUp(object? sender, MouseEventArgs eventArgs)
    {
        var releaseWorld = _camera.ScreenToWorld(Point(eventArgs.Location), ViewportSize());
        if (_dragMode == DragMode.Sheets && Distance(_pressScreen, Point(eventArgs.Location)) > 4)
        {
            var selectedSheetIds = SelectedSheetIds();
            var destination = HitFolderBody(releaseWorld);
            if (destination is not null && selectedSheetIds.Any(sheetId =>
                    _layout.Sheets.TryGetValue(sheetId, out var card) &&
                    card.Sheet.FolderId != destination.Folder.Id))
            {
                HierarchyMoveRequested?.Invoke(this, new ObserverHierarchyMoveRequestedEventArgs(
                    destination.Folder.Id,
                    selectedSheetIds,
                    []));
            }
            else
            {
                var state = new ObserverPlacementPlanner().MoveSheets(
                    _snapshot,
                    _layout,
                    selectedSheetIds,
                    _dragWorldDelta);
                BoardStateRequested?.Invoke(this, new ObserverBoardStateRequestedEventArgs(
                    state,
                    $"Move {selectedSheetIds.Length} observer layout{(selectedSheetIds.Length == 1 ? string.Empty : "s")}"));
            }
        }
        else if (_dragMode == DragMode.Folder && _dragFolderId is { } folderId &&
                 Distance(_pressScreen, Point(eventArgs.Location)) > 4)
        {
            var state = new ObserverPlacementPlanner().MoveFolder(
                _snapshot,
                folderId,
                _dragWorldDelta);
            BoardStateRequested?.Invoke(this, new ObserverBoardStateRequestedEventArgs(
                state,
                "Move observer folder"));
        }
        else if (_dragMode == DragMode.Reorder && _reorderSheetId is { } movingId &&
                 Distance(_pressScreen, Point(eventArgs.Location)) > 4)
        {
            var target = _spatialIndex.HitSheet(releaseWorld);
            if (target is not null && target.Sheet.PageViewId != movingId)
                ReorderRequested?.Invoke(this, new ObserverReorderRequestedEventArgs(
                    movingId,
                    target.Sheet.PageViewId));
        }
        else if (_dragMode == DragMode.Lasso && _lassoWorld is { } lasso)
        {
            var crossing = lasso.Width < 0;
            var keys = _spatialIndex.QuerySheets(lasso)
                .Where(card => crossing || lasso.Contains(card.Bounds))
                .Select(card => new OverviewNodeKey(OverviewNodeKind.Sheet, card.Sheet.PageViewId))
                .ToArray();
            SelectionRequested?.Invoke(this, new ObserverSelectionRequestedEventArgs(
                keys,
                keys.FirstOrDefault()));
        }

        ResetDrag();
        eventArgs.Handled = true;
    }

    private void OnMouseDoubleClick(object? sender, MouseEventArgs eventArgs)
    {
        var world = _camera.ScreenToWorld(Point(eventArgs.Location), ViewportSize());
        var card = _spatialIndex.HitSheet(world);
        if (card is null) return;
        var detail = HitDetail(card, world);
        var target = detail is null
            ? new OverviewNavigationTarget(card.Sheet.PageViewId)
            : new OverviewNavigationTarget(card.Sheet.PageViewId, detail.DetailViewportId);
        NavigationRequested?.Invoke(this, new ObserverNavigationRequestedEventArgs(target));
        eventArgs.Handled = true;
    }

    private void OnMouseWheel(object? sender, MouseEventArgs eventArgs)
    {
        var delta = eventArgs.Delta.Height;
        if (Math.Abs(delta) < float.Epsilon) return;
        _camera = _camera.ZoomAt(
            Point(eventArgs.Location),
            delta > 0 ? 1.12 : 1 / 1.12,
            ViewportSize());
        NotifyViewChanged();
        eventArgs.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Keys.Space)
        {
            _spaceHeld = true;
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key is Keys.Equal or Keys.Add)
        {
            Zoom(1.2);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key is Keys.Minus or Keys.Subtract)
        {
            Zoom(1 / 1.2);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Keys.F)
        {
            if (_selection.Count > 0) FocusSelection(); else FitAll();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Keys.T)
        {
            TidyRequested?.Invoke(this, EventArgs.Empty);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key is Keys.Delete or Keys.Backspace)
        {
            DeleteRequested?.Invoke(this, EventArgs.Empty);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Keys.Escape)
        {
            if (ExitWorkspaceOnEscape)
            {
                ExitWorkspaceRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                SelectionRequested?.Invoke(this, new ObserverSelectionRequestedEventArgs([], null));
                ResetDrag();
            }
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Keys.Enter)
        {
            NavigateFirstSelection();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key is Keys.PageUp or Keys.PageDown)
        {
            ReorderStepRequested?.Invoke(this,
                new ObserverReorderStepRequestedEventArgs(eventArgs.Key == Keys.PageUp ? -1 : 1));
            eventArgs.Handled = true;
        }
        else if (eventArgs.Modifiers.HasFlag(Keys.Alt) &&
                 eventArgs.Key is Keys.Left or Keys.Right or Keys.Up or Keys.Down)
        {
            var delta = eventArgs.Key switch
            {
                Keys.Left => new ObserverPoint(-10, 0),
                Keys.Right => new ObserverPoint(10, 0),
                Keys.Up => new ObserverPoint(0, -10),
                _ => new ObserverPoint(0, 10),
            };
            NudgeSelection(delta);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Modifiers.HasFlag(Keys.Application) || eventArgs.Modifiers.HasFlag(Keys.Control))
        {
            if (eventArgs.Key == Keys.A)
            {
                var keys = _snapshot.Sheets
                    .Select(sheet => new OverviewNodeKey(OverviewNodeKind.Sheet, sheet.PageViewId))
                    .ToArray();
                SelectionRequested?.Invoke(this, new ObserverSelectionRequestedEventArgs(keys, keys.FirstOrDefault()));
                eventArgs.Handled = true;
            }
        }
        else if (eventArgs.Key is Keys.Left or Keys.Right or Keys.Up or Keys.Down)
        {
            var dx = eventArgs.Key == Keys.Left ? 40 : eventArgs.Key == Keys.Right ? -40 : 0;
            var dy = eventArgs.Key == Keys.Up ? 40 : eventArgs.Key == Keys.Down ? -40 : 0;
            _camera = _camera.PanScreen(dx, dy);
            NotifyViewChanged();
            eventArgs.Handled = true;
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Keys.Space) _spaceHeld = false;
    }

    private void OnDragOver(object? sender, DragEventArgs eventArgs)
    {
        eventArgs.Effects = eventArgs.Data.Contains(NamedViewDragType) &&
                            HitDetailAtScreen(eventArgs.Location) is not null
            ? DragEffects.Copy
            : DragEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs eventArgs)
    {
        var name = eventArgs.Data.GetString(NamedViewDragType);
        var detail = HitDetailAtScreen(eventArgs.Location);
        if (!string.IsNullOrWhiteSpace(name) && detail is not null)
        {
            NamedViewRequested?.Invoke(this, new ObserverNamedViewRequestedEventArgs(
                name,
                [detail.DetailViewportId]));
            eventArgs.Effects = DragEffects.Copy;
        }
        else
        {
            eventArgs.Effects = DragEffects.None;
        }
    }

    private void SelectAt(ObserverPoint world, Keys modifiers, bool preserveExistingIfHit)
    {
        var sheet = _spatialIndex.HitSheet(world);
        if (sheet is not null)
        {
            var detail = HitDetail(sheet, world);
            if (detail is not null)
            {
                var detailKey = new OverviewNodeKey(OverviewNodeKind.Detail, detail.DetailViewportId);
                if (!preserveExistingIfHit || !_selection.Contains(detailKey))
                    SelectKey(detailKey, modifiers);
                return;
            }

            if (!preserveExistingIfHit || !_selection.Contains(
                    new OverviewNodeKey(OverviewNodeKind.Sheet, sheet.Sheet.PageViewId)))
                SelectSheet(sheet.Sheet.PageViewId, modifiers);
            return;
        }

        var folder = _spatialIndex.HitFolderHeader(world, ObserverPlacementPlanner.FolderHeaderHeight);
        if (folder is not null) SelectFolder(folder.Folder.Id, modifiers);
    }

    private void SelectSheet(Guid sheetId, Keys modifiers) =>
        SelectKey(new OverviewNodeKey(OverviewNodeKind.Sheet, sheetId), modifiers);

    private void SelectFolder(Guid folderId, Keys modifiers) =>
        SelectKey(new OverviewNodeKey(OverviewNodeKind.Folder, folderId), modifiers);

    private void SelectKey(OverviewNodeKey key, Keys modifiers)
    {
        var keys = IsAdditive(modifiers) ? _selection.ToHashSet() : [];
        if (IsAdditive(modifiers) && !keys.Add(key)) keys.Remove(key);
        else keys.Add(key);
        SelectionRequested?.Invoke(this, new ObserverSelectionRequestedEventArgs(keys.ToArray(), key));
    }

    private static bool IsAdditive(Keys modifiers) =>
        modifiers.HasFlag(Keys.Application) || modifiers.HasFlag(Keys.Control) || modifiers.HasFlag(Keys.Shift);

    private ObserverFolderFrame? HitFolderBody(ObserverPoint world) =>
        _layout.Folders.Values
            .Where(frame => frame.Bounds.Contains(world))
            .OrderByDescending(frame => frame.Depth)
            .FirstOrDefault();

    private ObserverDetailSnapshot? HitDetailAtScreen(PointF screen)
    {
        var world = _camera.ScreenToWorld(Point(screen), ViewportSize());
        var card = _spatialIndex.HitSheet(world);
        return card is null ? null : HitDetail(card, world);
    }

    private static ObserverDetailSnapshot? HitDetail(ObserverSheetCard card, ObserverPoint world)
    {
        var localX = (world.X - card.Bounds.Left) / card.Bounds.Width;
        var localY = (world.Y - card.Bounds.Top) / card.Bounds.Height;
        var point = new ObserverPoint(localX, localY);
        return card.Sheet.Details
            .Where(detail => detail.NormalizedBounds.Contains(point))
            .OrderBy(detail => detail.NormalizedBounds.Width * detail.NormalizedBounds.Height)
            .FirstOrDefault();
    }

    private void NavigateFirstSelection()
    {
        var first = _selection.FirstOrDefault();
        if (first == default) return;
        var target = NavigationTarget(first);
        if (target is { } navigation)
            NavigationRequested?.Invoke(this, new ObserverNavigationRequestedEventArgs(navigation));
    }

    private ObserverRect SelectedBounds()
    {
        var bounds = new ObserverRect();
        foreach (var key in _selection)
        {
            if (key.Kind == OverviewNodeKind.Sheet && _layout.Sheets.TryGetValue(key.Id, out var card))
                bounds = ObserverRect.Union(bounds, card.Bounds);
            else if (key.Kind == OverviewNodeKind.Folder && _layout.Folders.TryGetValue(key.Id, out var frame))
                bounds = ObserverRect.Union(bounds, frame.Bounds);
            else if (key.Kind == OverviewNodeKind.Detail)
            {
                var owner = _layout.Sheets.Values.FirstOrDefault(candidate =>
                    candidate.Sheet.Details.Any(detail => detail.DetailViewportId == key.Id));
                var detail = owner?.Sheet.Details.FirstOrDefault(candidate => candidate.DetailViewportId == key.Id);
                if (owner is not null && detail is not null)
                    bounds = ObserverRect.Union(bounds, DetailWorldRect(owner.Bounds, detail.NormalizedBounds));
            }
        }

        return bounds;
    }

    private void NudgeSelection(ObserverPoint delta)
    {
        var state = _snapshot.CanvasState;
        var selectedFolders = _selection
            .Where(key => key.Kind == OverviewNodeKind.Folder)
            .Select(key => key.Id)
            .Where(folderId => !_selection.Any(key => key.Kind == OverviewNodeKind.Folder &&
                                                       key.Id != folderId &&
                                                       IsFolderDescendant(folderId, key.Id)))
            .ToArray();
        foreach (var folderId in selectedFolders)
            state = new ObserverPlacementPlanner().MoveFolder(_snapshot with { CanvasState = state }, folderId, delta);

        var selectedSheets = SelectedSheetIds()
            .Where(sheetId => _layout.Sheets.TryGetValue(sheetId, out var card) &&
                              !selectedFolders.Any(folderId => IsFolderDescendant(card.Sheet.FolderId, folderId)))
            .ToArray();
        if (selectedSheets.Length > 0)
            state = new ObserverPlacementPlanner().MoveSheets(
                _snapshot with { CanvasState = state }, _layout, selectedSheets, delta);
        if (!ObserverCanvasStateComparer.ContentEquals(state, _snapshot.CanvasState))
            BoardStateRequested?.Invoke(this, new ObserverBoardStateRequestedEventArgs(
                state,
                "Nudge observer selection"));
    }

    private Guid[] SelectedSheetIds()
    {
        var result = _selection
            .Where(key => key.Kind == OverviewNodeKind.Sheet)
            .Select(key => key.Id)
            .ToHashSet();
        foreach (var detailId in _selection
                     .Where(key => key.Kind == OverviewNodeKind.Detail)
                     .Select(key => key.Id))
        {
            var owner = _snapshot.Sheets.FirstOrDefault(sheet =>
                sheet.Details.Any(detail => detail.DetailViewportId == detailId));
            if (owner is not null) result.Add(owner.PageViewId);
        }

        return result.ToArray();
    }

    private ObserverRect PreviewBounds(ObserverRect bounds, Guid id)
    {
        if (_dragMode == DragMode.Sheets &&
            _selection.Contains(new OverviewNodeKey(OverviewNodeKind.Sheet, id)))
            return bounds.Translate(_dragWorldDelta);
        if (_dragMode == DragMode.Folder && _dragFolderId is { } draggedFolderId)
        {
            if (_layout.Folders.ContainsKey(id) && IsFolderDescendant(id, draggedFolderId))
                return bounds.Translate(_dragWorldDelta);
            if (_layout.Sheets.TryGetValue(id, out var card) &&
                IsFolderDescendant(card.Sheet.FolderId, draggedFolderId))
                return bounds.Translate(_dragWorldDelta);
        }
        return bounds;
    }

    private bool IsFolderDescendant(Guid folderId, Guid ancestorId)
    {
        var folders = _snapshot.Folders.ToDictionary(folder => folder.Id);
        var visited = new HashSet<Guid>();
        Guid? current = folderId;
        while (current is { } id && visited.Add(id) && folders.TryGetValue(id, out var folder))
        {
            if (id == ancestorId) return true;
            current = folder.ParentId;
        }

        return false;
    }

    private bool ContainsKey(OverviewNodeKey key) => key.Kind switch
    {
        OverviewNodeKind.Folder => _snapshot.Folders.Any(folder => folder.Id == key.Id),
        OverviewNodeKind.Sheet => _snapshot.Sheets.Any(sheet => sheet.PageViewId == key.Id),
        OverviewNodeKind.Detail => _snapshot.Sheets.Any(sheet => sheet.Details.Any(detail => detail.DetailViewportId == key.Id)),
        _ => false,
    };

    private OverviewNavigationTarget? NavigationTarget(OverviewNodeKey key)
    {
        if (key.Kind == OverviewNodeKind.Sheet)
            return new OverviewNavigationTarget(key.Id);
        if (key.Kind == OverviewNodeKind.Detail)
        {
            var owner = _snapshot.Sheets.FirstOrDefault(sheet =>
                sheet.Details.Any(detail => detail.DetailViewportId == key.Id));
            return owner is null ? null : new OverviewNavigationTarget(owner.PageViewId, key.Id);
        }

        return null;
    }

    private void NotifyViewChanged()
    {
        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ResetDrag()
    {
        _dragMode = DragMode.None;
        _dragWorldDelta = new ObserverPoint();
        _dragFolderId = null;
        _reorderSheetId = null;
        _lassoWorld = null;
        Invalidate();
    }

    private ObserverSize ViewportSize() => new(Math.Max(1, Size.Width), Math.Max(1, Size.Height));

    private RectangleF ScreenRect(ObserverRect world, ObserverSize viewport)
    {
        var screen = _camera.WorldToScreen(world, viewport);
        return new RectangleF((float)screen.X, (float)screen.Y, (float)screen.Width, (float)screen.Height);
    }

    private static RectangleF DetailScreenRect(ObserverRect normalized, RectangleF card) => new(
        card.Left + (float)(normalized.Left * card.Width),
        card.Top + (float)(normalized.Top * card.Height),
        (float)(normalized.Width * card.Width),
        (float)(normalized.Height * card.Height));

    private static ObserverRect DetailWorldRect(ObserverRect card, ObserverRect normalized) => new(
        card.Left + normalized.Left * card.Width,
        card.Top + normalized.Top * card.Height,
        normalized.Width * card.Width,
        normalized.Height * card.Height);

    private static ObserverPoint Point(PointF point) => new(point.X, point.Y);
    private static double Distance(ObserverPoint first, ObserverPoint second) =>
        Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));

    private sealed record PreviewEntry(OverviewThumbnailKey Key, Bitmap Bitmap);

    private enum DragMode
    {
        None,
        Pan,
        Sheets,
        Folder,
        Reorder,
        Lasso,
    }
}

internal sealed class ObserverBoardStateRequestedEventArgs : EventArgs
{
    internal ObserverBoardStateRequestedEventArgs(ObserverCanvasState state, string undoDescription)
    {
        State = state;
        UndoDescription = undoDescription;
    }

    internal ObserverCanvasState State { get; }
    internal string UndoDescription { get; }
}

internal sealed class ObserverSelectionRequestedEventArgs : EventArgs
{
    internal ObserverSelectionRequestedEventArgs(
        IReadOnlyList<OverviewNodeKey> selection,
        OverviewNodeKey? anchor)
    {
        Selection = selection;
        Anchor = anchor;
    }

    internal IReadOnlyList<OverviewNodeKey> Selection { get; }
    internal OverviewNodeKey? Anchor { get; }
}

internal sealed class ObserverNavigationRequestedEventArgs : EventArgs
{
    internal ObserverNavigationRequestedEventArgs(OverviewNavigationTarget target) => Target = target;
    internal OverviewNavigationTarget Target { get; }
}

internal sealed class ObserverHierarchyMoveRequestedEventArgs : EventArgs
{
    internal ObserverHierarchyMoveRequestedEventArgs(
        Guid destinationFolderId,
        IReadOnlyList<Guid> sheetIds,
        IReadOnlyList<Guid> folderIds)
    {
        DestinationFolderId = destinationFolderId;
        SheetIds = sheetIds;
        FolderIds = folderIds;
    }

    internal Guid DestinationFolderId { get; }
    internal IReadOnlyList<Guid> SheetIds { get; }
    internal IReadOnlyList<Guid> FolderIds { get; }
}

internal sealed class ObserverReorderRequestedEventArgs : EventArgs
{
    internal ObserverReorderRequestedEventArgs(Guid movingSheetId, Guid beforeSheetId)
    {
        MovingSheetId = movingSheetId;
        BeforeSheetId = beforeSheetId;
    }

    internal Guid MovingSheetId { get; }
    internal Guid BeforeSheetId { get; }
}

internal sealed class ObserverReorderStepRequestedEventArgs : EventArgs
{
    internal ObserverReorderStepRequestedEventArgs(int direction) => Direction = Math.Sign(direction);
    internal int Direction { get; }
}

internal sealed class ObserverNamedViewRequestedEventArgs : EventArgs
{
    internal ObserverNamedViewRequestedEventArgs(string namedViewName, IReadOnlyList<Guid> detailViewportIds)
    {
        NamedViewName = namedViewName;
        DetailViewportIds = detailViewportIds;
    }

    internal string NamedViewName { get; }
    internal IReadOnlyList<Guid> DetailViewportIds { get; }
}

internal sealed class ObserverContextRequestedEventArgs : EventArgs
{
    internal ObserverContextRequestedEventArgs(ObserverPoint worldPoint, PointF controlPoint)
    {
        WorldPoint = worldPoint;
        ControlPoint = controlPoint;
    }

    internal ObserverPoint WorldPoint { get; }
    internal PointF ControlPoint { get; }
}

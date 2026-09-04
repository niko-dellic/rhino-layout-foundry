using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Observer;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

internal sealed class ThumbnailFoundryPanel : Panel
{
    private readonly ThumbnailGridDrawable _grid;
    private readonly Scrollable _scrollable;
    private readonly FoundrySlider _sizeSlider;
    private readonly FoundryToolbarField _densityControl;
    private readonly Label _status;
    private readonly UITimer _thumbnailTimer;
    private readonly UITimer _invalidationTimer;
    private readonly UITimer _resizeTimer;
    private readonly OverviewThumbnailCache _thumbnailCache = new(128, 64 * 1024 * 1024);
    private readonly OverviewThumbnailRequestQueue _thumbnailQueue = new();
    private readonly Dictionary<Guid, long> _previewContentVersions = [];
    private readonly object _invalidationSyncRoot = new();
    private ObserverSnapshot _snapshot = ObserverSnapshot.NoDocument;
    private OverviewFilterProjection _filter = new(false, new HashSet<OverviewNodeKey>(), new HashSet<Guid>());
    private OverviewInvalidation? _pendingInvalidation;
    private CancellationTokenSource _thumbnailCancellation = new();
    private bool _thumbnailCaptureInProgress;
    private bool _isLoaded;
    private long _previewContentSequence;

    internal event EventHandler<DeleteSelectionRequestedEventArgs>? DeleteSelectionRequested;

    internal ThumbnailFoundryPanel()
    {
        BackgroundColor = FoundryTheme.PanelBackground;
        _grid = new ThumbnailGridDrawable();
        _scrollable = new Scrollable
        {
            Border = BorderType.None,
            ExpandContentWidth = true,
            ExpandContentHeight = true,
            Content = _grid,
        };
        _sizeSlider = new FoundrySlider(
            0,
            100,
            50,
            150,
            value => $"Thumbnail size: {value}%",
            drawFocusRing: false);
        _densityControl = new FoundryToolbarField(_sizeSlider, 170);
        _status = FoundryTheme.MutedLabel();
        _status.Visible = false;
        _thumbnailTimer = new UITimer { Interval = 0.06 };
        _invalidationTimer = new UITimer { Interval = 0.12 };
        _resizeTimer = new UITimer { Interval = 0.12 };

        _sizeSlider.ValueChanged += (_, _) => ApplyGridSize();
        _scrollable.Scroll += (_, _) => QueueVisiblePreviews();
        _scrollable.SizeChanged += (_, _) =>
        {
            _grid.SetPresentationReady(false);
            _resizeTimer.Stop();
            _resizeTimer.Start();
        };
        _grid.SelectionRequested += (_, eventArgs) =>
            LayoutFoundryUiHost.Selection.Replace(
                _snapshot.HasDocument ? _snapshot.DocumentRuntimeSerialNumber : null,
                eventArgs.Selection,
                eventArgs.Anchor,
                this);
        _grid.NavigationRequested += (_, eventArgs) =>
        {
            var result = LayoutFoundryUiHost.Navigate(eventArgs.Target);
            SetStatus(result.Succeeded ? string.Empty : result.Message);
        };
        _grid.ContextRequested += (_, eventArgs) => ShowContextMenu(eventArgs.ControlPoint);
        _grid.CopyRequested += (_, _) => CopySelection();
        _grid.PasteRequested += async (_, _) => await PasteSelectionAsync();

        Content = new StackLayout
        {
            Padding = new Padding(0),
            Spacing = FoundryTheme.Space2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new StackLayoutItem(_scrollable, true),
                _status,
            },
        };

        _thumbnailTimer.Elapsed += async (_, _) => await CaptureNextThumbnailAsync();
        _invalidationTimer.Elapsed += OnInvalidationTimer;
        _resizeTimer.Elapsed += (_, _) =>
        {
            _resizeTimer.Stop();
            if (_scrollable.Size.Width <= 2 || _scrollable.Size.Height <= 2) return;
            ApplyGridSize();
            _grid.SetPresentationReady(true);
            QueueVisiblePreviews();
        };
        Load += OnLoaded;
        UnLoad += OnUnloaded;
        RefreshSnapshot();
    }

    internal void FocusContent() => _grid.Focus();

    internal Control DensityControl => _densityControl;

    internal void PrepareForDisplay()
    {
        _grid.SetPresentationReady(false);
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    internal void SetFilter(OverviewFilterProjection projection)
    {
        _filter = projection ?? throw new ArgumentNullException(nameof(projection));
        ApplyFilteredSnapshot();
        _scrollable.ScrollPosition = Point.Empty;
        UpdateStatus();
        QueueVisiblePreviews();
    }

    private void OnLoaded(object? sender, EventArgs eventArgs)
    {
        if (_isLoaded) return;
        _isLoaded = true;
        LayoutFoundryUiHost.OverviewChanged += OnOverviewChanged;
        LayoutFoundryUiHost.Selection.Changed += OnSharedSelectionChanged;
        PrepareForDisplay();
        RefreshSnapshot();
    }

    private void OnUnloaded(object? sender, EventArgs eventArgs)
    {
        if (!_isLoaded) return;
        _isLoaded = false;
        LayoutFoundryUiHost.OverviewChanged -= OnOverviewChanged;
        LayoutFoundryUiHost.Selection.Changed -= OnSharedSelectionChanged;
        _invalidationTimer.Stop();
        _resizeTimer.Stop();
        ResetThumbnailCapture();
        _grid.ReleasePreviews();
    }

    private void OnOverviewChanged(object? sender, OverviewInvalidationEventArgs eventArgs)
    {
        lock (_invalidationSyncRoot)
        {
            _pendingInvalidation = _pendingInvalidation is null
                ? eventArgs.Invalidation
                : _pendingInvalidation.Merge(eventArgs.Invalidation);
        }
        if (!_invalidationTimer.Started) _invalidationTimer.Start();
    }

    private void OnInvalidationTimer(object? sender, EventArgs eventArgs)
    {
        _invalidationTimer.Stop();
        OverviewInvalidation? invalidation;
        lock (_invalidationSyncRoot)
        {
            invalidation = _pendingInvalidation;
            _pendingInvalidation = null;
        }
        if (invalidation is null) return;
        if (invalidation.Kind.HasFlag(OverviewInvalidationKind.Thumbnails))
        {
            var affected = ResolveAffectedSheetIds(invalidation.AffectedEntityIds);
            AdvancePreviewVersions(affected.Count == 0 ? null : affected);
            _grid.InvalidatePreviews(affected.Count == 0 ? null : affected);
            if (_snapshot.HasDocument)
                _thumbnailCache.Invalidate(
                    _snapshot.DocumentRuntimeSerialNumber,
                    affected.Count == 0 ? null : affected);
        }
        RefreshSnapshot();
    }

    private void RefreshSnapshot()
    {
        var previousSerial = _snapshot.DocumentRuntimeSerialNumber;
        var next = LayoutFoundryUiHost.CaptureObserverSnapshot();
        if (previousSerial != next.DocumentRuntimeSerialNumber)
        {
            ResetThumbnailCapture();
            _grid.ReleasePreviews();
            if (previousSerial != 0) _thumbnailCache.Invalidate(previousSerial);
            _previewContentVersions.Clear();
            _previewContentSequence = 0;
            _scrollable.ScrollPosition = Point.Empty;
        }

        var currentSheetIds = next.Sheets.Select(sheet => sheet.PageViewId).ToHashSet();
        foreach (var stale in _previewContentVersions.Keys.Where(id => !currentSheetIds.Contains(id)).ToArray())
            _previewContentVersions.Remove(stale);
        foreach (var sheetId in currentSheetIds)
            if (!_previewContentVersions.ContainsKey(sheetId))
                _previewContentVersions[sheetId] = ++_previewContentSequence;
        _snapshot = next with
        {
            Sheets = next.Sheets.Select(sheet => sheet with
            {
                PreviewContentVersion = _previewContentVersions[sheet.PageViewId],
            }).ToArray(),
        };
        ApplyFilteredSnapshot();
        _grid.SetSelection(LayoutFoundryUiHost.Selection.DocumentRuntimeSerialNumber ==
                           (_snapshot.HasDocument ? _snapshot.DocumentRuntimeSerialNumber : null)
            ? LayoutFoundryUiHost.Selection.Selected
            : []);
        UpdateStatus();
        QueueVisiblePreviews();
    }

    private void OnSharedSelectionChanged(object? sender, DocumentSelectionChangedEventArgs eventArgs)
    {
        if (eventArgs.DocumentRuntimeSerialNumber !=
            (_snapshot.HasDocument ? _snapshot.DocumentRuntimeSerialNumber : null)) return;
        _grid.SetSelection(eventArgs.Selection);
    }

    private void ApplyGridSize()
    {
        var gridWidth = GridWidth();
        _grid.SetGridDensity(gridWidth, Density, GridMinimumHeight());
        _scrollable.UpdateScrollSizes();
        UpdateStatus();
        QueueVisiblePreviews();
    }

    private double GridWidth() => Math.Max(240, _scrollable.Size.Width - 2);

    private double GridMinimumHeight() => Math.Max(1, _scrollable.Size.Height - 2);

    private double Density => _sizeSlider.Value / 100d;

    private void ApplyFilteredSnapshot()
    {
        var visible = !_filter.IsActive
            ? _snapshot
            : _snapshot with
            {
                Sheets = _snapshot.Sheets
                    .Where(sheet => _filter.MatchesSheet(sheet.PageViewId))
                    .ToArray(),
            };
        _grid.SetSnapshot(visible, GridWidth(), Density, GridMinimumHeight());
        _scrollable.UpdateScrollSizes();
    }

    private void ShowContextMenu(PointF location)
    {
        var selection = SelectedSheets();
        if (selection.Length == 0)
        {
            var pasteOnly = new ButtonMenuItem
            {
                Text = "Paste",
                Enabled = HierarchyClipboard.CanPasteCurrentDocument(),
            };
            pasteOnly.Click += async (_, _) => await PasteSelectionAsync();
            new ContextMenu(pasteOnly).Show(_grid, location);
            return;
        }

        var open = new ButtonMenuItem { Text = "Open in Rhino", Enabled = selection.Length == 1 };
        var properties = new ButtonMenuItem
        {
            Text = selection.Length == 1 ? "Edit Properties…" : "Edit Properties for Selected…",
        };
        var duplicate = new ButtonMenuItem
        {
            Text = selection.Length == 1 ? "Duplicate Layout" : $"Duplicate {selection.Length} Layouts",
        };
        var delete = new ButtonMenuItem
        {
            Text = selection.Length == 1 ? "Delete Layout…" : $"Delete {selection.Length} Layouts…",
        };
        var include = new ButtonMenuItem { Text = "Enable for Printing" };
        var exclude = new ButtonMenuItem { Text = "Disable from Printing" };
        var copy = new ButtonMenuItem { Text = "Copy", Enabled = selection.Length > 0 };
        var paste = new ButtonMenuItem
        {
            Text = "Paste",
            Enabled = HierarchyClipboard.CanPasteCurrentDocument(),
        };

        open.Click += (_, _) => OpenSelectedSheet();
        properties.Click += (_, _) => OpenBatchProperties();
        duplicate.Click += async (_, _) => await DuplicateSelectionAsync();
        delete.Click += (_, _) => RequestDeleteSelection();
        include.Click += async (_, _) => await SetPrintInclusionAsync(true);
        exclude.Click += async (_, _) => await SetPrintInclusionAsync(false);
        copy.Click += (_, _) => CopySelection();
        paste.Click += async (_, _) => await PasteSelectionAsync();

        new ContextMenu(
            open,
            properties,
            new SeparatorMenuItem(),
            copy,
            paste,
            new SeparatorMenuItem(),
            duplicate,
            delete,
            new SeparatorMenuItem(),
            include,
            exclude).Show(_grid, location);
    }

    private OverviewNodeKey[] SelectedSheets() =>
        LayoutFoundryUiHost.Selection.Selected
            .Where(key => key.Kind == OverviewNodeKind.Sheet)
            .ToArray();

    private void OpenSelectedSheet()
    {
        var selection = SelectedSheets();
        if (selection.Length != 1) return;
        var result = LayoutFoundryUiHost.Navigate(new OverviewNavigationTarget(selection[0].Id));
        SetStatus(result.Succeeded ? string.Empty : result.Message);
    }

    private async void OpenBatchProperties()
    {
        var snapshot = LayoutFoundryUiHost.CaptureSnapshot();
        if (snapshot is null) return;
        var targets = BatchTargetResolver.Resolve(snapshot, SelectedSheets());
        if (targets.Count == 0)
        {
            SetStatus("The selection does not contain any layouts.");
            return;
        }

        var dialog = new BatchCreateLayoutsDialog(snapshot, targets);
        dialog.ShowModal(this);
        await dialog.PreviewCleanup;
        if (dialog.Succeeded) RefreshSnapshot();
    }

    private async Task DuplicateSelectionAsync()
    {
        var selection = SelectedSheets();
        if (selection.Length == 0) return;
        var result = await LayoutFoundryUiHost.DuplicateSelectionAsync(selection);
        if (result.Succeeded) RefreshSnapshot();
        SetStatus(ResultMessage(
            result,
            $"Duplicated {selection.Length} layout{(selection.Length == 1 ? string.Empty : "s")}."));
    }

    private void CopySelection()
    {
        SetStatus(HierarchyClipboard.CopyCurrentSelection().Message);
    }

    private async Task PasteSelectionAsync()
    {
        var result = await HierarchyClipboard.PasteAsync();
        if (result.Succeeded) RefreshSnapshot();
        SetStatus(result.Message);
    }

    private void RequestDeleteSelection()
    {
        var selection = SelectedSheets();
        if (selection.Length == 0) return;
        DeleteSelectionRequested?.Invoke(
            this,
            new DeleteSelectionRequestedEventArgs(selection));
    }

    private async Task SetPrintInclusionAsync(bool include)
    {
        var selection = SelectedSheets();
        if (selection.Length == 0) return;
        var result = await LayoutFoundryUiHost.SetPrintInclusionAsync(selection, include);
        if (result.Succeeded) RefreshSnapshot();
        SetStatus(ResultMessage(
            result,
            include ? "Enabled for printing." : "Disabled from printing."));
    }

    private static string ResultMessage(OperationResult result, string success) =>
        result.Succeeded
            ? success
            : string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message));

    private void UpdateStatus()
    {
        SetStatus(_snapshot.HasDocument ? string.Empty : "No active Rhino document");
    }

    private void SetStatus(string? message)
    {
        _status.Text = message ?? string.Empty;
        _status.Visible = !string.IsNullOrWhiteSpace(_status.Text);
    }

    private void QueueVisiblePreviews()
    {
        if (!_snapshot.HasDocument || _snapshot.Sheets.Count == 0) return;
        var visible = _scrollable.VisibleRect;
        if (visible.Width <= 0 || visible.Height <= 0)
            visible = new Rectangle(0, 0, Math.Max(1, _scrollable.Size.Width), Math.Max(1, _scrollable.Size.Height));
        var sheets = _grid.VisibleSheets(visible, overscanRows: 1);
        var retained = sheets.Select(sheet => sheet.PageViewId).ToHashSet();
        _grid.PrunePreviews(retained);
        foreach (var sheet in sheets)
        {
            var currentBucket = _grid.CurrentPreviewBucket(sheet.PageViewId);
            var bucket = ObserverThumbnailResolution.Select(_grid.GridLayout.CardWidth, currentBucket);
            if (_grid.HasCurrentPreview(sheet.PageViewId, sheet.PreviewContentVersion, bucket)) continue;
            var (width, height) = PreviewDimensions(sheet, bucket);
            var key = new OverviewThumbnailKey(
                _snapshot.DocumentRuntimeSerialNumber,
                sheet.PageViewId,
                width,
                height,
                sheet.PreviewContentVersion,
                bucket);
            if (_thumbnailCache.TryGet(key, out var bytes))
            {
                _grid.SetPreview(key, new Bitmap(bytes));
                continue;
            }
            var selected = LayoutFoundryUiHost.Selection.Selected.Contains(
                new OverviewNodeKey(OverviewNodeKind.Sheet, sheet.PageViewId));
            _thumbnailQueue.Enqueue(new OverviewThumbnailRequest(key, selected ? 0 : 10));
        }
        if (_thumbnailQueue.PendingCount > 0 && !_thumbnailTimer.Started) _thumbnailTimer.Start();
    }

    private async Task CaptureNextThumbnailAsync()
    {
        if (_thumbnailCaptureInProgress) return;
        var request = _thumbnailQueue.TakeNext();
        if (request is null)
        {
            _thumbnailTimer.Stop();
            return;
        }
        _thumbnailCaptureInProgress = true;
        try
        {
            var result = await LayoutFoundryUiHost.CaptureThumbnailAsync(request, _thumbnailCancellation.Token);
            var sheet = _snapshot.Sheets.FirstOrDefault(candidate =>
                candidate.PageViewId == request.Key.SheetPageViewId);
            if (result.Succeeded && sheet is not null &&
                sheet.PreviewContentVersion == request.Key.ContentVersion &&
                _snapshot.DocumentRuntimeSerialNumber == request.Key.DocumentRuntimeSerialNumber)
            {
                _thumbnailCache.Store(result.Key, result.PngBytes!);
                _grid.SetPreview(result.Key, new Bitmap(result.PngBytes!));
            }
        }
        finally
        {
            _thumbnailQueue.Complete(request.Key);
            _thumbnailCaptureInProgress = false;
        }
    }

    private void ResetThumbnailCapture()
    {
        _thumbnailTimer.Stop();
        _thumbnailCancellation.Cancel();
        _thumbnailCancellation.Dispose();
        _thumbnailCancellation = new CancellationTokenSource();
        _thumbnailQueue.Clear();
        _thumbnailCaptureInProgress = false;
    }

    private IReadOnlySet<Guid> ResolveAffectedSheetIds(IReadOnlySet<Guid>? entityIds)
    {
        if (entityIds is null || entityIds.Count == 0) return new HashSet<Guid>();
        return _snapshot.Sheets
            .Where(sheet => entityIds.Contains(sheet.PageViewId) ||
                            sheet.Details.Any(detail => entityIds.Contains(detail.DetailViewportId)))
            .Select(sheet => sheet.PageViewId)
            .ToHashSet();
    }

    private void AdvancePreviewVersions(IReadOnlySet<Guid>? sheetIds)
    {
        var targets = sheetIds is null || sheetIds.Count == 0
            ? _snapshot.Sheets.Select(sheet => sheet.PageViewId)
            : sheetIds;
        foreach (var sheetId in targets)
            _previewContentVersions[sheetId] = ++_previewContentSequence;
    }

    private static (int Width, int Height) PreviewDimensions(ObserverSheetSnapshot sheet, int bucket)
    {
        var width = Math.Max(1, sheet.PaperWidthMillimeters);
        var height = Math.Max(1, sheet.PaperHeightMillimeters);
        return width >= height
            ? (bucket, Math.Max(1, (int)Math.Round(bucket * height / width)))
            : (Math.Max(1, (int)Math.Round(bucket * width / height)), bucket);
    }
}

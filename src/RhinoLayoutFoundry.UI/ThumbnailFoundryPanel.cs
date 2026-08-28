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
    private readonly Slider _sizeSlider;
    private readonly Label _densityLabel;
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
        _sizeSlider = new Slider
        {
            MinValue = 120,
            MaxValue = 360,
            Value = 210,
            TickFrequency = 10,
            Width = 150,
            ToolTip = "Resize page thumbnails to fit more or fewer layouts per row",
        };
        _densityLabel = FoundryTheme.MutedLabel();
        _densityLabel.Width = 92;
        _status = FoundryTheme.MutedLabel();
        _thumbnailTimer = new UITimer { Interval = 0.06 };
        _invalidationTimer = new UITimer { Interval = 0.12 };
        _resizeTimer = new UITimer { Interval = 0.12 };

        var smaller = ToolbarButton("−", "Show more layouts per row");
        var larger = ToolbarButton("+", "Show fewer, larger layouts per row");
        smaller.Click += (_, _) => _sizeSlider.Value = Math.Max(_sizeSlider.MinValue, _sizeSlider.Value - 20);
        larger.Click += (_, _) => _sizeSlider.Value = Math.Min(_sizeSlider.MaxValue, _sizeSlider.Value + 20);
        _sizeSlider.ValueChanged += (_, _) => ApplyGridSize();
        _scrollable.Scroll += (_, _) => QueueVisiblePreviews();
        _scrollable.SizeChanged += (_, _) =>
        {
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
            _status.Text = result.Succeeded ? string.Empty : result.Message;
        };
        _grid.ContextRequested += (_, eventArgs) => ShowContextMenu(eventArgs.ControlPoint);

        Content = new StackLayout
        {
            Padding = new Padding(FoundryTheme.Space3),
            Spacing = FoundryTheme.Space2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = FoundryTheme.Space1,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Items =
                    {
                        new Label
                        {
                            Text = "Page thumbnails",
                            Font = SystemFonts.Bold(11),
                            TextColor = FoundryTheme.PrimaryText,
                        },
                        new StackLayoutItem(null, true),
                        smaller,
                        _sizeSlider,
                        larger,
                        _densityLabel,
                    },
                },
                new StackLayoutItem(_scrollable, true),
                _status,
            },
        };

        _thumbnailTimer.Elapsed += async (_, _) => await CaptureNextThumbnailAsync();
        _invalidationTimer.Elapsed += OnInvalidationTimer;
        _resizeTimer.Elapsed += (_, _) =>
        {
            _resizeTimer.Stop();
            ApplyGridSize();
        };
        Load += OnLoaded;
        UnLoad += OnUnloaded;
        RefreshSnapshot();
    }

    private static Button ToolbarButton(string text, string toolTip) =>
        FoundryTheme.ConfigureToolbarButton(new Button { Text = text, ToolTip = toolTip });

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
        _grid.SetGridSize(GridWidth(), _sizeSlider.Value, GridMinimumHeight());
        _scrollable.UpdateScrollSizes();
        UpdateStatus();
        QueueVisiblePreviews();
    }

    private double GridWidth() => Math.Max(240, _scrollable.Size.Width - 2);

    private double GridMinimumHeight() => Math.Max(1, _scrollable.Size.Height - 2);

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
        _grid.SetSnapshot(visible, GridWidth(), _sizeSlider.Value, GridMinimumHeight());
        _scrollable.UpdateScrollSizes();
    }

    private void ShowContextMenu(PointF location)
    {
        var selection = SelectedSheets();
        if (selection.Length == 0) return;

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

        open.Click += (_, _) => OpenSelectedSheet();
        properties.Click += (_, _) => OpenBatchProperties();
        duplicate.Click += async (_, _) => await DuplicateSelectionAsync();
        delete.Click += async (_, _) => await DeleteSelectionAsync();
        include.Click += async (_, _) => await SetPrintInclusionAsync(true);
        exclude.Click += async (_, _) => await SetPrintInclusionAsync(false);

        new ContextMenu(
            open,
            properties,
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
        _status.Text = result.Succeeded ? string.Empty : result.Message;
    }

    private void OpenBatchProperties()
    {
        var snapshot = LayoutFoundryUiHost.CaptureSnapshot();
        if (snapshot is null) return;
        var targets = BatchTargetResolver.Resolve(snapshot, SelectedSheets());
        if (targets.Count == 0)
        {
            _status.Text = "The selection does not contain any layouts.";
            return;
        }

        var dialog = new BatchPropertiesDialog(snapshot, targets);
        dialog.ShowModal(this);
        if (dialog.Succeeded) RefreshSnapshot();
    }

    private async Task DuplicateSelectionAsync()
    {
        var selection = SelectedSheets();
        if (selection.Length == 0) return;
        var result = await LayoutFoundryUiHost.DuplicateSelectionAsync(selection);
        _status.Text = ResultMessage(
            result,
            $"Duplicated {selection.Length} layout{(selection.Length == 1 ? string.Empty : "s")}.");
        if (result.Succeeded) RefreshSnapshot();
    }

    private async Task DeleteSelectionAsync()
    {
        var selection = SelectedSheets();
        if (selection.Length == 0) return;
        var response = MessageBox.Show(
            this,
            $"Permanently delete {selection.Length} Rhino layout{(selection.Length == 1 ? string.Empty : "s")}?\n\nLayout deletion cannot be undone.",
            selection.Length == 1 ? "Delete layout" : "Delete layouts",
            MessageBoxButtons.YesNo,
            MessageBoxType.Warning,
            MessageBoxDefaultButton.No);
        if (response != DialogResult.Yes) return;

        var result = await LayoutFoundryUiHost.DeleteSelectionAsync(selection);
        _status.Text = ResultMessage(result, "Selection deleted.");
        if (!result.Succeeded) return;
        LayoutFoundryUiHost.Selection.Clear(_snapshot.DocumentRuntimeSerialNumber, this);
        RefreshSnapshot();
    }

    private async Task SetPrintInclusionAsync(bool include)
    {
        var selection = SelectedSheets();
        if (selection.Length == 0) return;
        var result = await LayoutFoundryUiHost.SetPrintInclusionAsync(selection, include);
        _status.Text = ResultMessage(
            result,
            include ? "Enabled for printing." : "Disabled from printing.");
        if (result.Succeeded) RefreshSnapshot();
    }

    private static string ResultMessage(OperationResult result, string success) =>
        result.Succeeded
            ? success
            : string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message));

    private void UpdateStatus()
    {
        _densityLabel.Text = _grid.GridLayout.Columns == 1
            ? "1 per row"
            : $"{_grid.GridLayout.Columns} per row";
        _status.Text = !_snapshot.HasDocument
            ? "No active Rhino document"
            : _filter.IsActive
                ? $"{_grid.Snapshot.Sheets.Count} of {_snapshot.Sheets.Count} layouts"
                : $"{_snapshot.Sheets.Count} layouts  ·  {_snapshot.Sheets.Sum(sheet => sheet.Details.Count)} details";
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

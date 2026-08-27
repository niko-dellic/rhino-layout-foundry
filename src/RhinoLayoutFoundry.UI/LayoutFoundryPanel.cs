using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

[Guid("c43e26dd-b64b-454b-8b50-a10560e5045f")]
public sealed class LayoutFoundryPanel : Panel
{
    private const string InternalHierarchyDragType = "application/x-layout-foundry-hierarchy";
    private readonly Label _emptyTitleLabel;
    private readonly Label _emptyDescriptionLabel;
    private readonly Label _statusLabel;
    private readonly SearchBox _filterTextBox;
    private readonly DropDown _filterKindDropDown;
    private readonly Button _clearFilterButton;
    private readonly TextBox _renameTextBox;
    private readonly Button _renameButton;
    private readonly Button _openButton;
    private readonly Button _manageButton;
    private readonly Button _clearSelectionButton;
    private readonly Button _addFolderButton;
    private readonly bool _usesMacSafeHierarchy = OperatingSystem.IsMacOS();
    private readonly TreeGridView _treeGrid;
    private readonly GridColumn _layoutsColumn;
    private readonly GridColumn _detailsColumn;
    private readonly Panel _contentHost;
    private readonly Panel _toolbarSurface;
    private readonly Panel _renameActions;
    private ButtonMenuItem _setCurrentMenuItem = null!;
    private ButtonMenuItem _newFolderMenuItem = null!;
    private ButtonMenuItem _newPageMenuItem = null!;
    private ButtonMenuItem _duplicatePageMenuItem = null!;
    private ButtonMenuItem _deletePageMenuItem = null!;
    private ButtonMenuItem _renamePageMenuItem = null!;
    private ButtonMenuItem _newDetailMenuItem = null!;
    private ButtonMenuItem _printPageMenuItem = null!;
    private ButtonMenuItem _propertiesPageMenuItem = null!;
    private ButtonMenuItem _renameFolderMenuItem = null!;
    private ButtonMenuItem _deleteFolderMenuItem = null!;
    private readonly UITimer _layoutPollTimer;
    private readonly UITimer _invalidationTimer;
    private readonly UITimer _responsiveTimer;
    private readonly UITimer _thumbnailTimer;
    private readonly OverviewSelectionModel _selection = new();
    private readonly OverviewThumbnailCache _thumbnailCache = new();
    private readonly OverviewThumbnailRequestQueue _thumbnailQueue = new();
    private readonly Dictionary<OverviewThumbnailKey, Bitmap> _thumbnailBitmaps = [];
    private readonly Dictionary<Guid, HierarchyTreeItem> _sheetItems = [];
    private IReadOnlyList<HierarchyTreeItem> _renderedTreeItems = [];
    private readonly object _invalidationSyncRoot = new();
    private DocumentOverview _overview = DocumentOverview.NoDocument;
    private OverviewInvalidation? _pendingInvalidation;
    private CancellationTokenSource _thumbnailCancellation = new();
    private FoundryResponsiveLayout _responsiveLayout = FoundryResponsiveLayout.ForWidth(420);
    private uint? _documentSerialNumber;
    private bool _isLoaded;
    private bool _isPopulatingTree;
    private bool _isApplyingResponsiveLayout;
    private bool _thumbnailCaptureInProgress;
    private bool _dragInProgress;
    private PointF? _dragStart;
    private HierarchyTreeItem? _dragSourceItem;
    private IReadOnlyList<OverviewNodeKey> _dragSourceKeys = [];
    private InlineDraft? _inlineDraft;
    private Guid? _contextDestinationFolderId;

    public LayoutFoundryPanel()
    {
        BackgroundColor = FoundryTheme.PanelBackground;

        _emptyTitleLabel = new Label
        {
            Font = FoundryTheme.EmptyTitleFont,
            TextColor = FoundryTheme.PrimaryText,
            TextAlignment = TextAlignment.Center,
        };
        _emptyDescriptionLabel = FoundryTheme.MutedLabel();
        _emptyDescriptionLabel.TextAlignment = TextAlignment.Center;
        _statusLabel = FoundryTheme.MutedLabel();

        _filterTextBox = new SearchBox
        {
            PlaceholderText = "Search",
            ToolTip = "Search layouts, folders, details, and tags",
        };
        _filterKindDropDown = new DropDown
        {
            ToolTip = "Choose which layout rows to show",
            Width = 108,
            DataStore = new[] { "All rows", "Sheets", "Details", "Tagged", "Untagged" },
            SelectedIndex = 0,
        };
        _clearFilterButton = FoundryTheme.ConfigureToolbarButton(new Button
        {
            Text = "×",
            ToolTip = "Clear search and row filter",
            Visible = false,
        });
        _renameTextBox = new TextBox
        {
            PlaceholderText = "Sheet name",
        };
        _renameButton = FoundryTheme.ConfigureButton(new Button
        {
            Text = "Rename",
        });
        _addFolderButton = FoundryTheme.ConfigureToolbarButton(new Button
        {
            Text = "+",
            ToolTip = "Create folder at the hierarchy root",
            Enabled = false,
        });
        _openButton = FoundryTheme.ConfigureToolbarButton(new Button
        {
            Text = "↗",
            ToolTip = "Open selected layout or detail in Rhino",
            Enabled = false,
        });
        _manageButton = FoundryTheme.ConfigureToolbarButton(new Button
        {
            Text = "⋯",
            ToolTip = "Manage selected layouts",
            Enabled = false,
        });
        _clearSelectionButton = FoundryTheme.ConfigureToolbarButton(new Button
        {
            Text = "×",
            ToolTip = "Clear selection",
            Enabled = false,
        });

        (_treeGrid, _layoutsColumn, _detailsColumn) = CreateTreeGrid();
        CreateHierarchyContextMenu();
        _toolbarSurface = FoundryTheme.Surface(
            CreateToolbarContent(),
            new Padding(0));
        _contentHost = FoundryTheme.Surface(CreateEmptyState());
        _renameActions = CreateRenameActions();

        Content = new StackLayout
        {
            Padding = new Padding(FoundryTheme.Space4),
            Spacing = FoundryTheme.Space3,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                CreateHeader(),
                _toolbarSurface,
                new StackLayoutItem(_contentHost, expand: true),
                CreateFooter(),
            },
        };

        _treeGrid.SelectedItemChanged += OnSelectionChanged;
        _treeGrid.CellDoubleClick += (_, _) => NavigateSelected();
        _treeGrid.KeyDown += OnTreeKeyDown;
        _treeGrid.MouseDown += OnTreeMouseDown;
        _treeGrid.MouseMove += OnTreeMouseMove;
        _treeGrid.DragOver += OnTreeDragOver;
        _treeGrid.DragDrop += async (_, eventArgs) => await CompleteInternalDragAsync(eventArgs);
        _treeGrid.DragEnd += (_, _) => ResetPendingDrag();
        _treeGrid.CellEdited += async (_, eventArgs) => await CommitInlineDraftAsync(eventArgs);
        _filterTextBox.TextChanged += (_, _) => OnFilterChanged();
        _filterKindDropDown.SelectedIndexChanged += (_, _) => OnFilterChanged();
        _clearFilterButton.Click += (_, _) => ClearFilter();
        _clearSelectionButton.Click += (_, _) => ClearSelection();
        _addFolderButton.Click += (_, _) => BeginInlineCreation(InlineDraftKind.Folder);
        _openButton.Click += (_, _) => NavigateSelected();
        _manageButton.Click += (_, _) => OpenBatchProperties();
        _renameButton.Click += async (_, _) => await RenameSelectedSheetAsync();

        _layoutPollTimer = new UITimer { Interval = 0.5 };
        _layoutPollTimer.Elapsed += OnLayoutPoll;
        _invalidationTimer = new UITimer { Interval = 0.12 };
        _invalidationTimer.Elapsed += OnInvalidationTimer;
        _responsiveTimer = new UITimer { Interval = 0.16 };
        _responsiveTimer.Elapsed += (_, _) =>
        {
            _responsiveTimer.Stop();
            ApplyResponsiveLayout();
        };
        _thumbnailTimer = new UITimer { Interval = 0.08 };
        _thumbnailTimer.Elapsed += async (_, _) => await CaptureNextThumbnailAsync();
        Load += OnPanelLoaded;
        UnLoad += OnPanelUnloaded;
        // Rhino's macOS dock splitter runs a nested AppKit tracking loop. Any
        // managed Eto frame mutation scheduled from SizeChanged can recursively
        // enter NSView geometry validation. Mac density is chosen on panel load;
        // live breakpoint transitions remain enabled on Windows.
        if (!OperatingSystem.IsMacOS())
        {
            SizeChanged += (_, _) => QueueResponsiveLayout();
        }
        RefreshOverview();
    }

    private (TreeGridView TreeGrid, GridColumn LayoutsColumn, GridColumn DetailsColumn) CreateTreeGrid()
    {
        var treeGrid = new TreeGridView
        {
            AllowMultipleSelection = true,
            AllowDrop = true,
            ShowHeader = true,
        };
        var layoutsColumn = new GridColumn
        {
            HeaderText = _usesMacSafeHierarchy ? "Layouts" : "Name",
            DataCell = CreateLayoutsDataCell(inlineEditing: false),
            Expand = true,
            Editable = false,
        };
        treeGrid.Columns.Add(layoutsColumn);
        var detailsColumn = new GridColumn
        {
            HeaderText = "Details / tags",
            DataCell = new TextBoxCell
            {
                Binding = Binding.Property<HierarchyTreeItem, string>(item => item.SecondaryText),
            },
            Width = 190,
        };
        if (!_usesMacSafeHierarchy)
        {
            treeGrid.Columns.Add(detailsColumn);
            treeGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Status",
                DataCell = new TextBoxCell
                {
                    Binding = Binding.Property<HierarchyTreeItem, string>(item => item.StatusText),
                },
                Width = 82,
            });
        }

        return (treeGrid, layoutsColumn, detailsColumn);
    }

    private Cell CreateLayoutsDataCell(bool inlineEditing)
    {
        return _usesMacSafeHierarchy || inlineEditing
            ? new TextBoxCell
            {
                Binding = Binding.Property<HierarchyTreeItem, string>(item => item.DisplayText),
            }
            : new ImageTextCell(
                nameof(HierarchyTreeItem.Thumbnail),
                nameof(HierarchyTreeItem.PrimaryText));
    }

    private void SetInlineEditing(bool enabled)
    {
        _layoutsColumn.DataCell = CreateLayoutsDataCell(enabled);
        _layoutsColumn.Editable = enabled;
    }

    private Control CreateHeader()
    {
        var brandLabel = new Label
        {
            Text = "Layout Foundry",
            Font = SystemFonts.Bold(14),
            TextColor = FoundryTheme.PrimaryText,
            TextAlignment = TextAlignment.Left,
        };
        return new TableLayout
        {
            Rows =
            {
                new TableRow(brandLabel, new TableCell(null, scaleWidth: true)),
            },
        };
    }

    private Control CreateToolbarContent()
    {
        return new StackLayout
        {
            Spacing = FoundryTheme.Space1,
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
                        _addFolderButton,
                        _openButton,
                        _manageButton,
                        _clearSelectionButton,
                        new StackLayoutItem(null, expand: true),
                    },
                },
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = FoundryTheme.Space1,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Items =
                    {
                        new StackLayoutItem(_filterTextBox, expand: true),
                        _filterKindDropDown,
                        _clearFilterButton,
                    },
                },
            },
        };
    }

    private void CreateHierarchyContextMenu()
    {
        _setCurrentMenuItem = new ButtonMenuItem { Text = "Set Current" };
        _newFolderMenuItem = new ButtonMenuItem { Text = "New Folder" };
        _newPageMenuItem = new ButtonMenuItem { Text = "New Layout…" };
        _duplicatePageMenuItem = new ButtonMenuItem { Text = "Duplicate Layout" };
        _deletePageMenuItem = new ButtonMenuItem { Text = "Delete" };
        _renamePageMenuItem = new ButtonMenuItem { Text = "Rename" };
        _newDetailMenuItem = new ButtonMenuItem { Text = "New Detail" };
        _printPageMenuItem = new ButtonMenuItem { Text = "Print…" };
        _propertiesPageMenuItem = new ButtonMenuItem { Text = "Properties…" };
        _renameFolderMenuItem = new ButtonMenuItem { Text = "Rename Folder…" };
        _deleteFolderMenuItem = new ButtonMenuItem { Text = "Delete Folder…" };

        _setCurrentMenuItem.Click += (_, _) => NavigateSelected();
        _newFolderMenuItem.Click += (_, _) => BeginInlineCreation(
            InlineDraftKind.Folder,
            _contextDestinationFolderId);
        _newPageMenuItem.Click += (_, _) => BeginInlineCreation(
            InlineDraftKind.Sheet,
            _contextDestinationFolderId);
        _duplicatePageMenuItem.Click += async (_, _) => await DuplicateSelectedSheetAsync();
        _deletePageMenuItem.Click += async (_, _) => await DeleteSelectedSheetAsync();
        _renamePageMenuItem.Click += (_, _) => BeginInlineSheetRename();
        _newDetailMenuItem.Click += (_, _) => RunSelectedSheetCommand(LayoutSheetCommand.NewDetail);
        _printPageMenuItem.Click += (_, _) => RunSelectedSheetCommand(LayoutSheetCommand.Print);
        _propertiesPageMenuItem.Click += (_, _) => RunSelectedSheetCommand(LayoutSheetCommand.Properties);
        _renameFolderMenuItem.Click += async (_, _) => await RenameSelectedFolderAsync();
        _deleteFolderMenuItem.Click += async (_, _) => await DeleteSelectedFolderAsync();

        var contextMenu = new ContextMenu(
            _setCurrentMenuItem,
            new SeparatorMenuItem(),
            _newFolderMenuItem,
            _newPageMenuItem,
            _duplicatePageMenuItem,
            _deletePageMenuItem,
            _renamePageMenuItem,
            new SeparatorMenuItem(),
            _newDetailMenuItem,
            new SeparatorMenuItem(),
            _printPageMenuItem,
            _propertiesPageMenuItem,
            new SeparatorMenuItem(),
            _renameFolderMenuItem,
            _deleteFolderMenuItem);
        contextMenu.Opening += (_, _) => UpdateContextMenuActions();
        _treeGrid.ContextMenu = contextMenu;
    }

    private Control CreateEmptyState()
    {
        return new StackLayout
        {
            Padding = new Padding(FoundryTheme.Space6),
            Spacing = FoundryTheme.Space2,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Items =
            {
                _emptyTitleLabel,
                _emptyDescriptionLabel,
            },
        };
    }

    private Panel CreateRenameActions()
    {
        return new Panel
        {
            Content = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = FoundryTheme.Space2,
                VerticalContentAlignment = VerticalAlignment.Center,
                Items =
                {
                    new StackLayoutItem(_renameTextBox, expand: true),
                    _renameButton,
                },
            },
        };
    }

    private Control CreateFooter()
    {
        return new StackLayout
        {
            Spacing = FoundryTheme.Space2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                _renameActions,
                _statusLabel,
            },
        };
    }

    private OverviewTreeFilter CurrentFilter => new(
        _filterTextBox.Text,
        _filterKindDropDown.SelectedIndex switch
        {
            1 => OverviewFilterKind.Sheets,
            2 => OverviewFilterKind.Details,
            3 => OverviewFilterKind.Tagged,
            4 => OverviewFilterKind.Untagged,
            _ => OverviewFilterKind.All,
        });

    private void OnFilterChanged()
    {
        // SearchBox already supplies its platform-native cancel affordance.
        // Keep the explicit reset action only when a row-kind filter is active.
        _clearFilterButton.Visible = _filterKindDropDown.SelectedIndex != 0;
        PopulateTree();
    }

    private void ClearFilter()
    {
        _filterTextBox.Text = string.Empty;
        _filterKindDropDown.SelectedIndex = 0;
    }

    private void OnSelectionChanged(object? sender, EventArgs eventArgs)
    {
        if (_isPopulatingTree)
        {
            return;
        }

        var selectedItems = SelectedItems();
        var anchor = (_treeGrid.SelectedItem as HierarchyTreeItem)?.Node.Key;
        _selection.Replace(selectedItems.Select(item => item.Node.Key), anchor);
        UpdatePresentation();
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Keys.Enter)
        {
            if (_inlineDraft is not null)
            {
                _treeGrid.CommitEdit();
                eventArgs.Handled = true;
                return;
            }

            NavigateSelected();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Keys.Escape)
        {
            if (_inlineDraft is not null)
            {
                _inlineDraft = null;
                SetInlineEditing(false);
                _treeGrid.CancelEdit();
                PopulateTree();
                _statusLabel.Text = "Creation cancelled.";
                eventArgs.Handled = true;
                return;
            }

            ClearSelection();
            eventArgs.Handled = true;
        }
    }

    private void NavigateSelected()
    {
        var targets = SelectedItems()
            .Select(item => item.Node.NavigationTarget)
            .Where(target => target is not null)
            .Select(target => target!.Value)
            .Take(2)
            .ToArray();
        if (targets.Length != 1 || SelectedItemCount() != 1)
        {
            return;
        }

        var result = LayoutFoundryUiHost.Navigate(targets[0]);
        _statusLabel.Text = result.Succeeded ? string.Empty : result.Message;
    }

    private async Task RenameSelectedSheetAsync()
    {
        var selected = SelectedSheets().Take(2).ToArray();
        if (selected.Length != 1 || SelectedItemCount() != 1)
        {
            return;
        }

        _renameButton.Enabled = false;
        _renameTextBox.Enabled = false;
        _statusLabel.Text = "Applying rename…";

        var result = await LayoutFoundryUiHost.RenameSheetAsync(
            selected[0].PageViewId,
            selected[0].Name,
            _renameTextBox.Text);

        _statusLabel.Text = result.Succeeded
            ? "Layout renamed."
            : string.Join(" ", result.Diagnostics.Select(item => item.Message));

        if (!result.Succeeded)
        {
            _renameButton.Enabled = true;
            _renameTextBox.Enabled = true;
        }
    }

    private Task DuplicateSelectedSheetAsync()
    {
        var pageViewId = SelectedItems().Count == 1
            ? ResolveSheetPageViewId(SelectedItems()[0])
            : null;
        if (pageViewId is null)
        {
            return Task.CompletedTask;
        }

        var result = LayoutFoundryUiHost.DuplicateSheet(pageViewId.Value);
        _statusLabel.Text = result.Succeeded ? "Layout duplicated." : result.Message;
        if (result.Succeeded)
        {
            RefreshOverview();
        }

        return Task.CompletedTask;
    }

    private Task DeleteSelectedSheetAsync()
    {
        var selectedItems = SelectedItems();
        var pageViewId = selectedItems.Count == 1
            ? ResolveSheetPageViewId(selectedItems[0])
            : null;
        if (pageViewId is null)
        {
            return Task.CompletedTask;
        }

        var sheet = _overview.Sheets.FirstOrDefault(candidate => candidate.PageViewId == pageViewId.Value);
        if (sheet is null || MessageBox.Show(
                this,
                $"Delete the layout '{sheet.Name}'?",
                "Delete layout",
                MessageBoxButtons.YesNo,
                MessageBoxType.Warning,
                MessageBoxDefaultButton.No) != DialogResult.Yes)
        {
            return Task.CompletedTask;
        }

        var result = LayoutFoundryUiHost.DeleteSheet(pageViewId.Value);
        _statusLabel.Text = result.Succeeded ? $"Deleted '{sheet.Name}'." : result.Message;
        if (result.Succeeded)
        {
            ClearSelection();
            RefreshOverview();
        }

        return Task.CompletedTask;
    }

    private void BeginInlineSheetRename()
    {
        var selected = SelectedSheets().Take(2).ToArray();
        if (selected.Length != 1 || SelectedItemCount() != 1)
        {
            return;
        }

        var sheet = selected[0];
        _inlineDraft = new InlineDraft(
            InlineDraftKind.RenameSheet,
            sheet.PageViewId,
            sheet.FolderId,
            sheet.Name);
        SetInlineEditing(true);
        _statusLabel.Text = "Rename the layout, then press Return. Rhino does not support Undo for this change.";
        PopulateTree();

        Application.Instance.AsyncInvoke(() =>
        {
            var rows = VisibleTreeRows.Flatten(
                    _renderedTreeItems,
                    candidate => candidate.Children.OfType<HierarchyTreeItem>(),
                    candidate => candidate.Expanded)
                .ToArray();
            var row = Array.FindIndex(rows, candidate => candidate.Node.Key.Id == sheet.PageViewId);
            if (row < 0)
            {
                return;
            }

            _treeGrid.SelectedItem = rows[row];
            _treeGrid.ScrollToRow(row);
            _treeGrid.BeginEdit(row, 0);
        });
    }

    private void RunSelectedSheetCommand(LayoutSheetCommand command)
    {
        var selectedItems = SelectedItems();
        var pageViewId = selectedItems.Count == 1
            ? ResolveSheetPageViewId(selectedItems[0])
            : null;
        if (pageViewId is null)
        {
            return;
        }

        var result = LayoutFoundryUiHost.RunSheetCommand(pageViewId.Value, command);
        _statusLabel.Text = result.Succeeded ? string.Empty : result.Message;
    }

    private void OnOverviewChanged(object? sender, OverviewInvalidationEventArgs eventArgs)
    {
        var application = Application.Instance;
        if (application is null)
        {
            return;
        }

        application.AsyncInvoke(() =>
        {
            lock (_invalidationSyncRoot)
            {
                _pendingInvalidation = _pendingInvalidation is null
                    ? eventArgs.Invalidation
                    : _pendingInvalidation.Merge(eventArgs.Invalidation);
            }

            _invalidationTimer.Stop();
            _invalidationTimer.Start();
        });
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

        if (invalidation is null)
        {
            return;
        }

        if (invalidation.DocumentRuntimeSerialNumber is { } serial &&
            serial != _overview.DocumentRuntimeSerialNumber)
        {
            return;
        }

        if ((invalidation.Kind & OverviewInvalidationKind.Thumbnails) != 0 &&
            _overview.DocumentRuntimeSerialNumber is { } documentSerial)
        {
            InvalidateThumbnails(documentSerial, invalidation.AffectedEntityIds);
        }

        var requiresHierarchy = (invalidation.Kind & (
            OverviewInvalidationKind.DocumentIdentity |
            OverviewInvalidationKind.Hierarchy |
            OverviewInvalidationKind.Metadata |
            OverviewInvalidationKind.Diagnostics)) != 0;
        if (requiresHierarchy)
        {
            RefreshOverview();
        }
        else if ((invalidation.Kind & OverviewInvalidationKind.Thumbnails) != 0)
        {
            QueueThumbnails();
        }
    }

    private void OnPanelLoaded(object? sender, EventArgs eventArgs)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        ApplyResponsiveLayout();
        LayoutFoundryUiHost.OverviewChanged += OnOverviewChanged;
        _layoutPollTimer.Start();
        RefreshOverview();
        QueueThumbnails();
    }

    private void OnPanelUnloaded(object? sender, EventArgs eventArgs)
    {
        if (!_isLoaded)
        {
            return;
        }

        _isLoaded = false;
        _layoutPollTimer.Stop();
        _invalidationTimer.Stop();
        _responsiveTimer.Stop();
        ResetThumbnailCapture();
        LayoutFoundryUiHost.OverviewChanged -= OnOverviewChanged;
    }

    private void OnLayoutPoll(object? sender, EventArgs eventArgs)
    {
        var identity = LayoutFoundryUiHost.CaptureOverviewIdentity();
        if (identity.DocumentRuntimeSerialNumber != _overview.DocumentRuntimeSerialNumber ||
            identity.SheetCount != _overview.Sheets.Count)
        {
            RefreshOverview();
        }
    }

    private void RefreshOverview()
    {
        _overview = LayoutFoundryUiHost.CaptureOverview();
        if (_documentSerialNumber != _overview.DocumentRuntimeSerialNumber)
        {
            if (_documentSerialNumber is { } previousSerial)
            {
                _thumbnailCache.Invalidate(previousSerial);
                _thumbnailQueue.RemoveDocument(previousSerial);
            }

            ResetThumbnailCapture();
            _selection.Clear();
            _documentSerialNumber = _overview.DocumentRuntimeSerialNumber;
        }

        _selection.Prune(Flatten(OverviewTreeBuilder.Build(_overview)).Select(node => node.Key));
        PopulateTree();
    }

    private void PopulateTree()
    {
        var filter = CurrentFilter;
        var renderOverview = OverviewWithInlineDraft();
        var nodes = OverviewTreeBuilder.Build(renderOverview, filter);
        var nodeKeys = Flatten(nodes).Select(node => node.Key).ToHashSet();
        var draftKey = _inlineDraft is { } draft
            ? new OverviewNodeKey(
                draft.Kind == InlineDraftKind.Folder ? OverviewNodeKind.Folder : OverviewNodeKind.Sheet,
                draft.Id)
            : (OverviewNodeKey?)null;
        var preferredSelection = draftKey is { } visibleDraft && nodeKeys.Contains(visibleDraft)
            ? visibleDraft
            : _selection.Anchor is { } anchor && nodeKeys.Contains(anchor)
                ? anchor
                : _selection.VisibleSelection(nodeKeys).FirstOrDefault();
        var items = nodes
            .Select(node => new HierarchyTreeItem(
                node,
                filter.IsActive,
                preferredSelection,
                _usesMacSafeHierarchy,
                _inlineDraft?.Id))
            .ToArray();
        _renderedTreeItems = items;
        var visibleItems = Flatten(items).ToDictionary(item => item.Node.Key);
        _sheetItems.Clear();
        foreach (var sheetItem in visibleItems.Values.Where(item =>
                     item.Node.Key.Kind == OverviewNodeKind.Sheet))
        {
            _sheetItems[sheetItem.Node.Key.Id] = sheetItem;
            var key = ThumbnailKey(sheetItem.Node.Key.Id);
            if (_usesMacSafeHierarchy)
            {
                continue;
            }

            if (_thumbnailBitmaps.TryGetValue(key, out var bitmap))
            {
                sheetItem.Thumbnail = bitmap;
            }
            else if (_thumbnailCache.TryGet(key, out var bytes))
            {
                bitmap = new Bitmap(bytes);
                _thumbnailBitmaps[key] = bitmap;
                sheetItem.Thumbnail = bitmap;
            }
        }

        _isPopulatingTree = true;
        try
        {
            _treeGrid.DataStore = new TreeGridItemCollection(items);
            if (preferredSelection != default && visibleItems.TryGetValue(preferredSelection, out var item))
            {
                _treeGrid.SelectedItem = item;
            }
        }
        finally
        {
            _isPopulatingTree = false;
        }

        UpdatePresentation();
        QueueThumbnails();
    }

    private DocumentOverview OverviewWithInlineDraft()
    {
        if (_inlineDraft is not { } draft)
        {
            return _overview;
        }

        return draft.Kind switch
        {
            InlineDraftKind.Folder => _overview with
            {
                Folders = _overview.Folders.Append(new FolderOverview(
                    draft.Id,
                    draft.ParentFolderId,
                    draft.Name,
                    int.MaxValue)).ToArray(),
            },
            InlineDraftKind.Sheet => _overview with
            {
                Sheets = _overview.Sheets.Append(new SheetOverview(
                    draft.Id,
                    draft.ParentFolderId,
                    draft.Name,
                    int.MaxValue,
                    [],
                    [])).ToArray(),
            },
            InlineDraftKind.RenameSheet => _overview,
            _ => _overview,
        };
    }

    private void UpdatePresentation()
    {
        var selectedItems = SelectedItems();
        var presentation = OverviewPanelPresentation.Create(
            _overview,
            CurrentFilter,
            selectedItems.Select(item => item.Node.Key));

        var hierarchyContext = selectedItems.Count > 0
            ? presentation.SelectionSummary
            : presentation.ResultSummary;
        _layoutsColumn.HeaderText = OverviewHierarchyHeader.Create(_overview, hierarchyContext);
        _emptyTitleLabel.Text = presentation.EmptyTitle;
        _emptyDescriptionLabel.Text = presentation.EmptyDescription;

        var showHierarchy = presentation.ContentState == OverviewContentState.Hierarchy;
        if (showHierarchy && !ReferenceEquals(_contentHost.Content, _treeGrid))
        {
            _contentHost.Padding = new Padding(0);
            _contentHost.Content = _treeGrid;
        }
        else if (!showHierarchy && ReferenceEquals(_contentHost.Content, _treeGrid))
        {
            _contentHost.Padding = new Padding(FoundryTheme.Space6);
            _contentHost.Content = CreateEmptyState();
        }

        UpdateSelectionActions(presentation, selectedItems);
    }

    private void UpdateSelectionActions(
        OverviewPanelPresentation presentation,
        IReadOnlyList<HierarchyTreeItem> selectedItems)
    {
        var selectedSheets = selectedItems
            .Select(item => item.Node.Sheet)
            .Where(sheet => sheet is not null)
            .Cast<SheetOverview>()
            .Take(2)
            .ToArray();
        var selectionCount = selectedItems.Count;
        var canRename = selectedSheets.Length == 1 && selectionCount == 1;
        var canOpen = selectionCount == 1 && selectedItems[0].Node.NavigationTarget is not null;
        var capabilities = LayoutFoundryUiHost.CaptureMutationCapabilities();

        _addFolderButton.Enabled = _overview.DocumentRuntimeSerialNumber is not null;
        var folderDestination = FolderCreationDestination.Resolve(
            _overview,
            selectedItems.Select(item => item.Node.Key));
        _addFolderButton.ToolTip = folderDestination is not null &&
                                   folderDestination.ParentFolderId != _overview.RootFolderId
            ? $"Create folder inside {folderDestination.DisplayName}"
            : "Create folder at the hierarchy root";
        _openButton.Enabled = canOpen;
        _manageButton.Enabled = selectionCount > 0;
        _clearSelectionButton.Enabled = selectionCount > 0;
        var renameAvailable = canRename && capabilities.PageRenameUndo.IsSupported;
        _renameActions.Visible = renameAvailable;
        _renameTextBox.Enabled = renameAvailable;
        _renameButton.Enabled = renameAvailable;
        if (renameAvailable && !_renameTextBox.HasFocus)
        {
            _renameTextBox.Text = selectedSheets[0].Name;
        }

        if (selectionCount == 0)
        {
            _statusLabel.Text = string.Empty;
        }
    }

    private void BeginInlineCreation(InlineDraftKind kind, Guid? explicitParentFolderId = null)
    {
        if (_overview.RootFolderId is not { } rootFolderId)
        {
            _statusLabel.Text = "Open a Rhino document before creating hierarchy items.";
            return;
        }

        if (_treeGrid.IsEditing)
        {
            _inlineDraft = null;
            SetInlineEditing(false);
            _treeGrid.CancelEdit();
        }

        var destination = explicitParentFolderId ?? ResolveCreationDestinationFolderId() ?? rootFolderId;
        if (_overview.Folders.All(folder => folder.Id != destination))
        {
            destination = rootFolderId;
        }

        if (CurrentFilter.IsActive)
        {
            ClearFilter();
        }

        _inlineDraft = new InlineDraft(
            kind,
            Guid.NewGuid(),
            destination,
            kind == InlineDraftKind.Folder ? "New Folder" : "New Page");
        SetInlineEditing(true);
        _statusLabel.Text = kind == InlineDraftKind.Folder
            ? "Name the new folder, then press Return."
            : "Name the new layout, then press Return.";
        PopulateTree();

        Application.Instance.AsyncInvoke(() =>
        {
            if (_inlineDraft is not { } draft)
            {
                return;
            }

            var item = Flatten(_renderedTreeItems)
                .FirstOrDefault(candidate => candidate.Node.Key.Id == draft.Id);
            if (item is null)
            {
                return;
            }

            _treeGrid.SelectedItem = item;
            var row = VisibleTreeRows.Flatten(
                    _renderedTreeItems,
                    candidate => candidate.Children.OfType<HierarchyTreeItem>(),
                    candidate => candidate.Expanded)
                .TakeWhile(candidate => !ReferenceEquals(candidate, item))
                .Count();
            _treeGrid.ScrollToRow(row);
            _treeGrid.BeginEdit(row, 0);
        });
    }

    private async Task CommitInlineDraftAsync(GridViewCellEventArgs eventArgs)
    {
        if (_inlineDraft is not { } draft ||
            eventArgs.Item is not HierarchyTreeItem { IsInlineDraft: true } item ||
            item.Node.Key.Id != draft.Id)
        {
            return;
        }

        var name = item.DisplayText.Trim();
        if (name.Length == 0)
        {
            _statusLabel.Text = "A name is required.";
            BeginEditingDraftAgain();
            return;
        }

        draft.Name = name;
        _statusLabel.Text = draft.Kind switch
        {
            InlineDraftKind.Folder => "Creating folder…",
            InlineDraftKind.Sheet => "Creating layout…",
            InlineDraftKind.RenameSheet => "Renaming layout…",
            _ => string.Empty,
        };
        var result = draft.Kind switch
        {
            InlineDraftKind.Folder => await LayoutFoundryUiHost.CreateFolderAsync(
                draft.Id,
                draft.ParentFolderId,
                name),
            InlineDraftKind.Sheet => await LayoutFoundryUiHost.CreateSheetAsync(
                draft.ParentFolderId,
                name),
            InlineDraftKind.RenameSheet => ToOperationResult(
                LayoutFoundryUiHost.RenameSheetDirect(draft.Id, name)),
            _ => throw new ArgumentOutOfRangeException(),
        };
        if (!result.Succeeded)
        {
            _statusLabel.Text = DiagnosticMessage(result);
            BeginEditingDraftAgain();
            return;
        }

        _inlineDraft = null;
        SetInlineEditing(false);
        _overview = LayoutFoundryUiHost.CaptureOverview();
        var createdKey = draft.Kind == InlineDraftKind.Folder
            ? new OverviewNodeKey(OverviewNodeKind.Folder, draft.Id)
            : draft.Kind == InlineDraftKind.RenameSheet
                ? new OverviewNodeKey(OverviewNodeKind.Sheet, draft.Id)
                : _overview.Sheets
                    .Where(sheet => sheet.FolderId == draft.ParentFolderId)
                    .FirstOrDefault(sheet => string.Equals(sheet.Name, name, StringComparison.Ordinal)) is { } sheet
                    ? new OverviewNodeKey(OverviewNodeKind.Sheet, sheet.PageViewId)
                    : (OverviewNodeKey?)null;
        if (createdKey is { } key)
        {
            _selection.Replace([key], key);
        }

        _statusLabel.Text = draft.Kind switch
        {
            InlineDraftKind.Folder => $"Created folder '{name}'.",
            InlineDraftKind.Sheet => $"Created layout '{name}'. Rhino does not support Undo for layout creation.",
            InlineDraftKind.RenameSheet => $"Renamed layout to '{name}'. Rhino does not support Undo for this change.",
            _ => string.Empty,
        };
        PopulateTree();
    }

    private static OperationResult ToOperationResult(OverviewNavigationResult result)
    {
        return result.Succeeded
            ? new OperationResult(true, [])
            : new OperationResult(
                false,
                [new RhinoLayoutFoundry.Core.Diagnostics.Diagnostic(
                    "layout.rename_failed",
                    RhinoLayoutFoundry.Core.Diagnostics.DiagnosticSeverity.Error,
                    result.Message)]);
    }

    private void BeginEditingDraftAgain()
    {
        Application.Instance.AsyncInvoke(() =>
        {
            if (_inlineDraft is null)
            {
                return;
            }

            PopulateTree();
            var rows = VisibleTreeRows.Flatten(
                    _renderedTreeItems,
                    candidate => candidate.Children.OfType<HierarchyTreeItem>(),
                    candidate => candidate.Expanded)
                .ToArray();
            var row = Array.FindIndex(rows, candidate => candidate.IsInlineDraft);
            if (row >= 0)
            {
                _treeGrid.SelectedItem = rows[row];
                _treeGrid.BeginEdit(row, 0);
            }
        });
    }

    private void UpdateContextMenuActions()
    {
        var folder = SelectedFolderItem();
        var selectedItems = SelectedItems();
        var selectedSheet = selectedItems.Count == 1
            ? ResolveSheetPageViewId(selectedItems[0])
            : null;
        var isFolderContext = folder is not null;
        var isSheetContext = selectedSheet is not null;
        var isRootContext = selectedItems.Count == 0;
        _contextDestinationFolderId = ResolveCreationDestinationFolderId();
        var destinationName = _overview.Folders
            .FirstOrDefault(candidate => candidate.Id == _contextDestinationFolderId)?.Name;
        _setCurrentMenuItem.Visible = isSheetContext;
        _setCurrentMenuItem.Enabled = isSheetContext;
        _newFolderMenuItem.Visible = isFolderContext || isRootContext;
        _newFolderMenuItem.Enabled = _overview.DocumentRuntimeSerialNumber is not null;
        _newPageMenuItem.Visible = true;
        _newPageMenuItem.Enabled = _overview.DocumentRuntimeSerialNumber is not null;
        _newFolderMenuItem.Text = destinationName is null || _contextDestinationFolderId == _overview.RootFolderId
            ? "New Folder"
            : $"New Folder in {destinationName}";
        _newPageMenuItem.Text = destinationName is null || _contextDestinationFolderId == _overview.RootFolderId
            ? "New Layout…"
            : $"New Layout in {destinationName}…";
        _duplicatePageMenuItem.Visible = isSheetContext;
        _duplicatePageMenuItem.Enabled = isSheetContext;
        _deletePageMenuItem.Visible = isSheetContext;
        _deletePageMenuItem.Enabled = isSheetContext;
        _renamePageMenuItem.Visible = isSheetContext;
        _renamePageMenuItem.Enabled = isSheetContext;
        _newDetailMenuItem.Visible = isSheetContext;
        _newDetailMenuItem.Enabled = isSheetContext;
        _printPageMenuItem.Visible = isSheetContext;
        _printPageMenuItem.Enabled = isSheetContext;
        _propertiesPageMenuItem.Visible = isSheetContext;
        _propertiesPageMenuItem.Enabled = isSheetContext;
        _renameFolderMenuItem.Enabled = folder is not null;
        _renameFolderMenuItem.Visible = folder is not null;
        _deleteFolderMenuItem.Enabled = folder is not null && folder.Node.Children.Count == 0;
        _deleteFolderMenuItem.Visible = folder is not null;
    }

    private async Task RenameSelectedFolderAsync()
    {
        var folder = SelectedFolderItem();
        if (folder is null)
        {
            return;
        }

        var dialog = new RenameFolderDialog(folder.Node.Label);
        dialog.ShowModal(this);
        if (!dialog.Accepted)
        {
            return;
        }

        _statusLabel.Text = "Renaming folder…";
        var result = await LayoutFoundryUiHost.RenameFolderAsync(
            folder.Node.Key.Id,
            folder.Node.Label,
            dialog.FolderName);
        if (!result.Succeeded)
        {
            _statusLabel.Text = DiagnosticMessage(result);
            return;
        }

        var folderKey = folder.Node.Key;
        _selection.Replace([folderKey], folderKey);
        _statusLabel.Text = $"Renamed folder to '{dialog.FolderName}'.";
        RefreshOverview();
    }

    private async Task DeleteSelectedFolderAsync()
    {
        var folder = SelectedFolderItem();
        if (folder is null)
        {
            return;
        }

        if (folder.Node.Children.Count > 0)
        {
            _statusLabel.Text = "Move this folder's sheets and nested folders before deleting it.";
            return;
        }

        var response = MessageBox.Show(
            this,
            $"Delete the empty folder '{folder.Node.Label}'?\n\nYou can undo this in Rhino.",
            "Delete folder",
            MessageBoxButtons.YesNo,
            MessageBoxType.Question,
            MessageBoxDefaultButton.No);
        if (response != DialogResult.Yes)
        {
            return;
        }

        _statusLabel.Text = "Deleting folder…";
        var result = await LayoutFoundryUiHost.DeleteFolderAsync(
            folder.Node.Key.Id,
            folder.Node.Label);
        if (!result.Succeeded)
        {
            _statusLabel.Text = DiagnosticMessage(result);
            return;
        }

        ClearSelection();
        _statusLabel.Text = $"Deleted '{folder.Node.Label}'.";
        RefreshOverview();
    }

    private void OnTreeMouseDown(object? sender, MouseEventArgs eventArgs)
    {
        var item = _treeGrid.GetCellAt(eventArgs.Location).Item as HierarchyTreeItem;
        if ((eventArgs.Buttons & MouseButtons.Alternate) != 0)
        {
            if (item is null)
            {
                ClearSelection();
            }
            else if (!SelectedItems().Contains(item))
            {
                _treeGrid.SelectedItem = item;
            }

            ResetPendingDrag();
            return;
        }

        if ((eventArgs.Buttons & MouseButtons.Primary) == 0 ||
            eventArgs.Modifiers != Keys.None ||
            item is null ||
            item.IsInlineDraft)
        {
            ResetPendingDrag();
            return;
        }

        if (!SelectedItems().Contains(item))
        {
            _treeGrid.SelectedItem = item;
        }

        _dragStart = eventArgs.Location;
        _dragSourceItem = item;
    }

    private void OnTreeMouseMove(object? sender, MouseEventArgs eventArgs)
    {
        if (_dragInProgress ||
            _dragStart is not { } start ||
            _dragSourceItem is null ||
            (eventArgs.Buttons & MouseButtons.Primary) == 0)
        {
            return;
        }

        var deltaX = eventArgs.Location.X - start.X;
        var deltaY = eventArgs.Location.Y - start.Y;
        if ((deltaX * deltaX) + (deltaY * deltaY) < 36)
        {
            return;
        }

        var selectedItems = SelectedItems();
        var sources = selectedItems.Contains(_dragSourceItem)
            ? selectedItems
            : [_dragSourceItem];
        _dragSourceKeys = sources.Select(item => item.Node.Key).Distinct().ToArray();
        _dragInProgress = true;
        _statusLabel.Text = "Drop on a folder, or on empty hierarchy space to move to the root.";
        var dragData = new DataObject();
        dragData.SetString("move", InternalHierarchyDragType);
        _treeGrid.DoDragDrop(dragData, DragEffects.Move);
    }

    private void OnTreeDragOver(object? sender, DragEventArgs eventArgs)
    {
        if (!_dragInProgress || !eventArgs.Data.Contains(InternalHierarchyDragType))
        {
            eventArgs.Effects = DragEffects.None;
            return;
        }

        var dragInfo = _treeGrid.GetDragInfo(eventArgs);
        dragInfo.RestrictToOver();
        var target = dragInfo.Item as HierarchyTreeItem;
        eventArgs.Effects = target is null || target.Node.Key.Kind == OverviewNodeKind.Folder
            ? DragEffects.Move
            : DragEffects.None;
    }

    private async Task CompleteInternalDragAsync(DragEventArgs eventArgs)
    {
        if (!_dragInProgress ||
            !eventArgs.Data.Contains(InternalHierarchyDragType) ||
            _dragSourceItem is not { } dragSource)
        {
            eventArgs.Effects = DragEffects.None;
            ResetPendingDrag();
            return;
        }

        var target = _treeGrid.GetDragInfo(eventArgs).Item as HierarchyTreeItem;
        var destinationFolderId = target is null
            ? _overview.RootFolderId
            : target.Node.Key.Kind == OverviewNodeKind.Folder
                ? target.Node.Key.Id
                : null;
        var sourceKeys = _dragSourceKeys.Count > 0
            ? _dragSourceKeys
            : [dragSource.Node.Key];
        ResetPendingDrag();

        if (destinationFolderId is null)
        {
            eventArgs.Effects = DragEffects.None;
            _statusLabel.Text = "Move cancelled. Drop on a folder or empty hierarchy space.";
            return;
        }

        eventArgs.Effects = DragEffects.Move;

        var destinationName = _overview.Folders
            .FirstOrDefault(folder => folder.Id == destinationFolderId.Value)?.Name ?? "Layouts";
        var folderIds = sourceKeys
            .Where(key => key.Kind == OverviewNodeKind.Folder)
            .Select(key => key.Id)
            .Distinct()
            .ToArray();
        OperationResult result;
        OverviewNodeKey[] movedKeys;
        string noun;
        if (folderIds.Length > 0)
        {
            if (sourceKeys.Any(key => key.Kind != OverviewNodeKind.Folder))
            {
                _statusLabel.Text = "Move folders and layouts separately.";
                return;
            }

            _statusLabel.Text = $"Moving {folderIds.Length} folder{(folderIds.Length == 1 ? string.Empty : "s")} to {destinationName}…";
            result = await LayoutFoundryUiHost.MoveFoldersAsync(destinationFolderId.Value, folderIds);
            movedKeys = folderIds.Select(id => new OverviewNodeKey(OverviewNodeKind.Folder, id)).ToArray();
            noun = folderIds.Length == 1 ? "folder" : "folders";
        }
        else
        {
            var sheetIds = sourceKeys
                .Select(ResolveSheetPageViewId)
                .Where(id => id is not null)
                .Select(id => id!.Value)
                .Distinct()
                .ToArray();
            if (sheetIds.Length == 0)
            {
                _statusLabel.Text = "Nothing was moved.";
                return;
            }

            _statusLabel.Text = $"Moving {sheetIds.Length} layout{(sheetIds.Length == 1 ? string.Empty : "s")} to {destinationName}…";
            result = await LayoutFoundryUiHost.MoveSheetsAsync(destinationFolderId.Value, sheetIds);
            movedKeys = sheetIds.Select(id => new OverviewNodeKey(OverviewNodeKind.Sheet, id)).ToArray();
            noun = sheetIds.Length == 1 ? "layout" : "layouts";
        }

        if (!result.Succeeded)
        {
            _statusLabel.Text = DiagnosticMessage(result);
            return;
        }

        _selection.Replace(movedKeys, movedKeys.FirstOrDefault());
        _statusLabel.Text = $"Moved {movedKeys.Length} {noun} to {destinationName}.";
        RefreshOverview();
    }

    private Guid? ResolveSheetPageViewId(HierarchyTreeItem item)
    {
        return ResolveSheetPageViewId(item.Node.Key);
    }

    private Guid? ResolveSheetPageViewId(OverviewNodeKey key)
    {
        if (key.Kind == OverviewNodeKind.Sheet)
        {
            return key.Id;
        }

        if (key.Kind != OverviewNodeKind.Detail)
        {
            return null;
        }

        return _overview.Sheets
            .FirstOrDefault(sheet => sheet.Details.Any(detail =>
                detail.DetailViewportId == key.Id))?
            .PageViewId;
    }

    private HierarchyTreeItem? SelectedFolderItem()
    {
        var selected = SelectedItems().Take(2).ToArray();
        return selected.Length == 1 && selected[0].Node.Key.Kind == OverviewNodeKind.Folder
            ? selected[0]
            : null;
    }

    private Guid? ResolveCreationDestinationFolderId()
    {
        if (_overview.RootFolderId is not { } rootFolderId)
        {
            return null;
        }

        var selected = SelectedItems().Take(2).ToArray();
        if (selected.Length != 1)
        {
            return rootFolderId;
        }

        var item = selected[0];
        if (item.Node.Key.Kind == OverviewNodeKind.Folder)
        {
            return item.Node.Key.Id;
        }

        var sheetId = ResolveSheetPageViewId(item);
        return sheetId is { } id
            ? _overview.Sheets.FirstOrDefault(sheet => sheet.PageViewId == id)?.FolderId ?? rootFolderId
            : rootFolderId;
    }

    private void ResetPendingDrag()
    {
        _dragStart = null;
        _dragSourceItem = null;
        _dragSourceKeys = [];
        _dragInProgress = false;
    }

    private static string DiagnosticMessage(OperationResult result)
    {
        return string.Join(" ", result.Diagnostics.Select(item => item.Message));
    }

    private void OpenBatchProperties()
    {
        var targets = SelectedItems()
            .Select(item => new BatchTarget(item.Node.Key, item.Node.Label))
            .ToArray();
        var context = LayoutFoundryUiHost.CaptureDocumentContext();
        if (targets.Length == 0 || context is null)
        {
            _statusLabel.Text = "The active Rhino document is unavailable.";
            return;
        }

        var dialog = new BatchPropertiesDialog(
            context.Value.DocumentRuntimeSerialNumber,
            context.Value.Revision,
            targets,
            LayoutFoundryUiHost.CaptureMutationCapabilities());
        dialog.ShowModal(this);
    }

    private void ApplyResponsiveLayout()
    {
        if (_isApplyingResponsiveLayout)
        {
            return;
        }

        _isApplyingResponsiveLayout = true;
        try
        {
            var next = FoundryResponsiveLayout.Transition(
                Math.Max(Width, 1),
                _responsiveLayout.Density);
            if (next == _responsiveLayout)
            {
                return;
            }

            var dimensionsChanged = next.ThumbnailWidth != _responsiveLayout.ThumbnailWidth ||
                                    next.ThumbnailHeight != _responsiveLayout.ThumbnailHeight;
            _responsiveLayout = next;
            // Eto maps this to NSTableColumn.setHidden on macOS. Toggling that
            // property while Rhino's dock splitter runs its nested tracking loop
            // recursively enters AppKit geometry validation. Keep the column on
            // Mac; compact mode still stacks tools, reduces previews, and hides
            // secondary footer copy. Windows can safely collapse the column.
            if (!OperatingSystem.IsMacOS())
            {
                _detailsColumn.Visible = next.ShowSecondaryColumn;
            }
            _toolbarSurface.Content = CreateToolbarContent();
            if (dimensionsChanged && _overview.DocumentRuntimeSerialNumber is { } serial)
            {
                InvalidateThumbnails(serial, null);
                PopulateTree();
            }
        }
        finally
        {
            _isApplyingResponsiveLayout = false;
        }
    }

    private void QueueResponsiveLayout()
    {
        if (!_isLoaded)
        {
            return;
        }

        // Coalesce Windows resize bursts so toolbar reflow and preview-size
        // invalidation happen once after the splitter settles. Mac live density
        // changes are disabled at subscription time because of AppKit recursion.
        _responsiveTimer.Stop();
        _responsiveTimer.Start();
    }

    private OverviewThumbnailKey ThumbnailKey(Guid sheetPageViewId)
    {
        return new OverviewThumbnailKey(
            _overview.DocumentRuntimeSerialNumber ?? 0,
            sheetPageViewId,
            _responsiveLayout.ThumbnailWidth,
            _responsiveLayout.ThumbnailHeight);
    }

    private void QueueThumbnails()
    {
        if (_usesMacSafeHierarchy ||
            !_isLoaded ||
            _overview.DocumentRuntimeSerialNumber is null)
        {
            return;
        }

        var selectedSheetId = SelectedItems()
            .FirstOrDefault(item => item.Node.Key.Kind == OverviewNodeKind.Sheet)?
            .Node.Key.Id;
        var index = 0;
        foreach (var pair in _sheetItems)
        {
            var key = ThumbnailKey(pair.Key);
            if (_thumbnailCache.TryGet(key, out _) || _thumbnailBitmaps.ContainsKey(key))
            {
                continue;
            }

            var priority = pair.Key == selectedSheetId
                ? -1
                : index < 18
                    ? index
                    : 100 + index;
            _thumbnailQueue.Enqueue(new OverviewThumbnailRequest(key, priority));
            index++;
        }

        if (_thumbnailQueue.PendingCount > 0 && !_thumbnailTimer.Started)
        {
            _thumbnailTimer.Start();
        }
    }

    private async Task CaptureNextThumbnailAsync()
    {
        if (_thumbnailCaptureInProgress)
        {
            return;
        }

        var request = _thumbnailQueue.TakeNext();
        if (request is null)
        {
            _thumbnailTimer.Stop();
            return;
        }

        _thumbnailCaptureInProgress = true;
        try
        {
            var result = await LayoutFoundryUiHost.CaptureThumbnailAsync(
                request,
                _thumbnailCancellation.Token);
            if (!result.Succeeded ||
                result.Key.DocumentRuntimeSerialNumber != _overview.DocumentRuntimeSerialNumber)
            {
                return;
            }

            _thumbnailCache.Store(result.Key, result.PngBytes!);
            if (_thumbnailBitmaps.Remove(result.Key, out var previous))
            {
                previous.Dispose();
            }

            var bitmap = new Bitmap(result.PngBytes!);
            _thumbnailBitmaps[result.Key] = bitmap;
            if (_sheetItems.TryGetValue(result.Key.SheetPageViewId, out var item))
            {
                item.Thumbnail = bitmap;
                _treeGrid.ReloadItem(item, reloadChildren: false);
            }

            TrimBitmapCache();
        }
        finally
        {
            _thumbnailQueue.Complete(request.Key);
            _thumbnailCaptureInProgress = false;
        }
    }

    private void InvalidateThumbnails(uint documentSerial, IReadOnlySet<Guid>? sheetIds)
    {
        _thumbnailCache.Invalidate(documentSerial, sheetIds);
        foreach (var pair in _thumbnailBitmaps
                     .Where(pair =>
                         pair.Key.DocumentRuntimeSerialNumber == documentSerial &&
                         (sheetIds is null || sheetIds.Count == 0 || sheetIds.Contains(pair.Key.SheetPageViewId)))
                     .ToArray())
        {
            _thumbnailBitmaps.Remove(pair.Key);
            pair.Value.Dispose();
        }
    }

    private void TrimBitmapCache()
    {
        foreach (var pair in _thumbnailBitmaps.ToArray())
        {
            if (_thumbnailCache.TryGet(pair.Key, out _))
            {
                continue;
            }

            _thumbnailBitmaps.Remove(pair.Key);
            pair.Value.Dispose();
        }
    }

    private void ResetThumbnailCapture()
    {
        _thumbnailTimer.Stop();
        _thumbnailCancellation.Cancel();
        _thumbnailCancellation.Dispose();
        _thumbnailCancellation = new CancellationTokenSource();
        _thumbnailQueue.Clear();
        foreach (var bitmap in _thumbnailBitmaps.Values)
        {
            bitmap.Dispose();
        }

        _thumbnailBitmaps.Clear();
        _thumbnailCaptureInProgress = false;
    }

    private void ClearSelection()
    {
        _selection.Clear();
        _isPopulatingTree = true;
        try
        {
            _treeGrid.SelectedItem = null;
        }
        finally
        {
            _isPopulatingTree = false;
        }

        UpdatePresentation();
    }

    private int SelectedItemCount()
    {
        return _treeGrid.SelectedItems.Cast<object>().Count();
    }

    private IReadOnlyList<HierarchyTreeItem> SelectedItems()
    {
        return _treeGrid.SelectedItems.OfType<HierarchyTreeItem>().ToArray();
    }

    private IEnumerable<SheetOverview> SelectedSheets()
    {
        return SelectedItems()
            .Select(item => item.Node.Sheet)
            .Where(sheet => sheet is not null)
            .Cast<SheetOverview>();
    }

    private static IEnumerable<OverviewTreeNode> Flatten(IEnumerable<OverviewTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<HierarchyTreeItem> Flatten(IEnumerable<HierarchyTreeItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in Flatten(item.Children.OfType<HierarchyTreeItem>()))
            {
                yield return child;
            }
        }
    }

    private sealed class HierarchyTreeItem : TreeGridItem
    {
        public HierarchyTreeItem(
            OverviewTreeNode node,
            bool expandAll,
            OverviewNodeKey preferredSelection,
            bool useMacSafeSingleColumn,
            Guid? inlineDraftId)
        {
            Node = node;
            Presentation = OverviewRowPresentation.Create(node, useMacSafeSingleColumn);
            IsInlineDraft = inlineDraftId == node.Key.Id;
            _displayText = IsInlineDraft ? node.Label : Presentation.PrimaryText;
            foreach (var child in node.Children)
            {
                Children.Add(new HierarchyTreeItem(
                    child,
                    expandAll,
                    preferredSelection,
                    useMacSafeSingleColumn,
                    inlineDraftId));
            }

            Expanded = expandAll ||
                       node.Key.Kind == OverviewNodeKind.Folder ||
                       Contains(node.Children, preferredSelection);
        }

        public OverviewTreeNode Node { get; }

        public OverviewRowPresentation Presentation { get; }

        public bool IsInlineDraft { get; }

        private string _displayText;

        public string DisplayText
        {
            get => _displayText;
            set
            {
                if (IsInlineDraft)
                {
                    _displayText = value;
                }
            }
        }

        public string PrimaryText => Presentation.PrimaryText;

        public string SecondaryText => Presentation.SecondaryText;

        public string StatusText => Presentation.StatusText;

        public Image? Thumbnail { get; set; }

        private static bool Contains(
            IEnumerable<OverviewTreeNode> nodes,
            OverviewNodeKey key)
        {
            return nodes.Any(node => node.Key == key || Contains(node.Children, key));
        }
    }

    private enum InlineDraftKind
    {
        Folder,
        Sheet,
        RenameSheet,
    }

    private sealed class InlineDraft(
        InlineDraftKind kind,
        Guid id,
        Guid parentFolderId,
        string name)
    {
        public InlineDraftKind Kind { get; } = kind;

        public Guid Id { get; } = id;

        public Guid ParentFolderId { get; } = parentFolderId;

        public string Name { get; set; } = name;
    }
}

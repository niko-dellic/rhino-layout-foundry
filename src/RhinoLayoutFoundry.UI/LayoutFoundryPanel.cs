using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

[Guid("c43e26dd-b64b-454b-8b50-a10560e5045f")]
public sealed class LayoutFoundryPanel : Panel
{
    private readonly Label _countLabel;
    private readonly Label _resultLabel;
    private readonly Label _emptyTitleLabel;
    private readonly Label _emptyDescriptionLabel;
    private readonly Label _selectionLabel;
    private readonly Label _selectionHintLabel;
    private readonly Label _statusLabel;
    private readonly TextBox _filterTextBox;
    private readonly DropDown _filterKindDropDown;
    private readonly Button _clearFilterButton;
    private readonly TextBox _renameTextBox;
    private readonly Button _renameButton;
    private readonly Button _openButton;
    private readonly Button _clearSelectionButton;
    private readonly TreeGridView _treeGrid;
    private readonly Panel _contentHost;
    private readonly Panel _selectionActions;
    private readonly Panel _renameActions;
    private readonly UITimer _layoutPollTimer;
    private readonly OverviewSelectionModel _selection = new();
    private DocumentOverview _overview = DocumentOverview.NoDocument;
    private uint? _documentSerialNumber;
    private bool _isLoaded;
    private bool _isPopulatingTree;
    private int _refreshQueued;

    public LayoutFoundryPanel()
    {
        BackgroundColor = FoundryTheme.PanelBackground;

        _countLabel = FoundryTheme.MutedLabel("Open or create a model to begin");
        _resultLabel = FoundryTheme.MutedLabel();
        _emptyTitleLabel = new Label
        {
            Font = FoundryTheme.EmptyTitleFont,
            TextColor = FoundryTheme.PrimaryText,
            TextAlignment = TextAlignment.Center,
        };
        _emptyDescriptionLabel = FoundryTheme.MutedLabel();
        _emptyDescriptionLabel.TextAlignment = TextAlignment.Center;
        _selectionLabel = new Label
        {
            Font = SystemFonts.Bold(),
            TextColor = FoundryTheme.PrimaryText,
            TextAlignment = TextAlignment.Left,
        };
        _selectionHintLabel = FoundryTheme.MutedLabel();
        _statusLabel = FoundryTheme.MutedLabel();

        _filterTextBox = new TextBox
        {
            PlaceholderText = "Search layouts…",
        };
        _filterKindDropDown = new DropDown
        {
            ToolTip = "Choose which layout rows to show",
            Width = 108,
            DataStore = new[] { "All rows", "Sheets", "Details", "Tagged", "Untagged" },
            SelectedIndex = 0,
        };
        _clearFilterButton = FoundryTheme.ConfigureIconButton(new Button
        {
            Text = "×",
            ToolTip = "Clear search and row filter",
            Visible = false,
        });
        var refreshButton = FoundryTheme.ConfigureIconButton(new Button
        {
            Text = "↻",
            ToolTip = "Refresh layouts from Rhino",
        });
        _renameTextBox = new TextBox
        {
            PlaceholderText = "Sheet name",
        };
        _renameButton = FoundryTheme.ConfigureButton(new Button
        {
            Text = "Rename",
        });
        _openButton = FoundryTheme.ConfigureButton(new Button
        {
            Text = "Open",
            ToolTip = "Activate this sheet or detail in Rhino",
        });
        _clearSelectionButton = FoundryTheme.ConfigureButton(new Button
        {
            Text = "Clear",
            ToolTip = "Clear the current selection",
        });

        _treeGrid = CreateTreeGrid();
        _contentHost = FoundryTheme.Surface(CreateEmptyState());
        _selectionActions = CreateSelectionActions();
        _renameActions = CreateRenameActions();

        Content = new StackLayout
        {
            Padding = new Padding(FoundryTheme.Space4),
            Spacing = FoundryTheme.Space3,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                CreateHeader(),
                CreateToolbar(refreshButton),
                new StackLayoutItem(_contentHost, expand: true),
                CreateFooter(),
            },
        };

        _treeGrid.SelectedItemChanged += OnSelectionChanged;
        _treeGrid.CellDoubleClick += (_, _) => NavigateSelected();
        _treeGrid.KeyDown += OnTreeKeyDown;
        _filterTextBox.TextChanged += (_, _) => OnFilterChanged();
        _filterKindDropDown.SelectedIndexChanged += (_, _) => OnFilterChanged();
        _clearFilterButton.Click += (_, _) => ClearFilter();
        _clearSelectionButton.Click += (_, _) => ClearSelection();
        _openButton.Click += (_, _) => NavigateSelected();
        _renameButton.Click += async (_, _) => await RenameSelectedSheetAsync();
        refreshButton.Click += (_, _) => RefreshOverview();

        _layoutPollTimer = new UITimer { Interval = 0.5 };
        _layoutPollTimer.Elapsed += OnLayoutPoll;
        Load += OnPanelLoaded;
        UnLoad += OnPanelUnloaded;
        RefreshOverview();
    }

    private TreeGridView CreateTreeGrid()
    {
        var treeGrid = new TreeGridView
        {
            AllowMultipleSelection = true,
            ShowHeader = true,
        };
        treeGrid.Columns.Add(new GridColumn
        {
            HeaderText = "Name",
            DataCell = new TextBoxCell
            {
                Binding = Binding.Property<HierarchyTreeItem, string>(item => item.Label),
            },
            Expand = true,
        });
        treeGrid.Columns.Add(new GridColumn
        {
            HeaderText = "Details / tags",
            DataCell = new TextBoxCell
            {
                Binding = Binding.Property<HierarchyTreeItem, string>(item => item.SecondaryText),
            },
            Width = 190,
        });
        return treeGrid;
    }

    private Control CreateHeader()
    {
        var brandLabel = new Label
        {
            Text = "LAYOUT FOUNDRY",
            Font = FoundryTheme.BrandFont,
            TextColor = FoundryTheme.MutedText,
            TextAlignment = TextAlignment.Left,
        };
        return new StackLayout
        {
            Spacing = FoundryTheme.Space1,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                brandLabel,
                _countLabel,
            },
        };
    }

    private Control CreateToolbar(Button refreshButton)
    {
        return FoundryTheme.Surface(
            new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = FoundryTheme.Space2,
                VerticalContentAlignment = VerticalAlignment.Center,
                Items =
                {
                    new StackLayoutItem(_filterTextBox, expand: true),
                    _filterKindDropDown,
                    _clearFilterButton,
                    refreshButton,
                },
            },
            new Padding(FoundryTheme.Space2));
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

    private Panel CreateSelectionActions()
    {
        var actionRow = new TableLayout
        {
            Spacing = new Size(FoundryTheme.Space2, 0),
            Rows =
            {
                new TableRow(
                    _selectionLabel,
                    new TableCell(null, scaleWidth: true),
                    _openButton,
                    _clearSelectionButton),
            },
        };

        return FoundryTheme.Surface(
            new StackLayout
            {
                Spacing = FoundryTheme.Space1,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Items =
                {
                    actionRow,
                    _selectionHintLabel,
                },
            },
            new Padding(FoundryTheme.Space2));
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
                _resultLabel,
                _selectionActions,
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
        _clearFilterButton.Visible = CurrentFilter.IsActive;
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
            NavigateSelected();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Keys.Escape)
        {
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

    private void OnOverviewChanged(object? sender, EventArgs eventArgs)
    {
        var application = Application.Instance;
        if (application is null || Interlocked.Exchange(ref _refreshQueued, 1) == 1)
        {
            return;
        }

        application.AsyncInvoke(() =>
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
            RefreshOverview();
        });
    }

    private void OnPanelLoaded(object? sender, EventArgs eventArgs)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        LayoutFoundryUiHost.OverviewChanged += OnOverviewChanged;
        _layoutPollTimer.Start();
        RefreshOverview();
    }

    private void OnPanelUnloaded(object? sender, EventArgs eventArgs)
    {
        if (!_isLoaded)
        {
            return;
        }

        _isLoaded = false;
        _layoutPollTimer.Stop();
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
            _selection.Clear();
            _documentSerialNumber = _overview.DocumentRuntimeSerialNumber;
        }

        _selection.Prune(Flatten(OverviewTreeBuilder.Build(_overview)).Select(node => node.Key));
        PopulateTree();
    }

    private void PopulateTree()
    {
        var filter = CurrentFilter;
        var nodes = OverviewTreeBuilder.Build(_overview, filter);
        var nodeKeys = Flatten(nodes).Select(node => node.Key).ToHashSet();
        var preferredSelection = _selection.Anchor is { } anchor && nodeKeys.Contains(anchor)
            ? anchor
            : _selection.VisibleSelection(nodeKeys).FirstOrDefault();
        var items = nodes
            .Select(node => new HierarchyTreeItem(node, filter.IsActive, preferredSelection))
            .ToArray();
        var visibleItems = Flatten(items).ToDictionary(item => item.Node.Key);

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
    }

    private void UpdatePresentation()
    {
        var selectedItems = SelectedItems();
        var presentation = OverviewPanelPresentation.Create(
            _overview,
            CurrentFilter,
            selectedItems.Select(item => item.Node.Key));

        _countLabel.Text = presentation.DocumentSummary;
        _resultLabel.Text = presentation.ResultSummary;
        _resultLabel.Visible = !string.IsNullOrWhiteSpace(presentation.ResultSummary);
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

        _selectionActions.Visible = presentation.ContentState == OverviewContentState.Hierarchy;
        _selectionLabel.Text = presentation.SelectionSummary;
        _selectionHintLabel.Text = selectionCount switch
        {
            0 => "Select folders, sheets, or details to manage them.",
            1 when canOpen => "Double-click or press Enter to open this item in Rhino.",
            1 => "This folder is ready for layout actions.",
            _ => "This selection is ready for the batch properties editor.",
        };
        _openButton.Visible = canOpen;
        _clearSelectionButton.Visible = selectionCount > 0;
        _renameActions.Visible = canRename;
        _renameTextBox.Enabled = canRename;
        _renameButton.Enabled = canRename;
        if (canRename && !_renameTextBox.HasFocus)
        {
            _renameTextBox.Text = selectedSheets[0].Name;
        }

        if (selectionCount == 0)
        {
            _statusLabel.Text = string.Empty;
        }
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
            OverviewNodeKey preferredSelection)
        {
            Node = node;
            foreach (var child in node.Children)
            {
                Children.Add(new HierarchyTreeItem(child, expandAll, preferredSelection));
            }

            Expanded = expandAll ||
                       node.Key.Kind == OverviewNodeKind.Folder ||
                       Contains(node.Children, preferredSelection);
        }

        public OverviewTreeNode Node { get; }

        public string Label => Node.Label;

        public string SecondaryText => Node.SecondaryText;

        private static bool Contains(
            IEnumerable<OverviewTreeNode> nodes,
            OverviewNodeKey key)
        {
            return nodes.Any(node => node.Key == key || Contains(node.Children, key));
        }
    }
}

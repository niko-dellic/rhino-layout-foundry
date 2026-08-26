using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

[Guid("c43e26dd-b64b-454b-8b50-a10560e5045f")]
public sealed class LayoutFoundryPanel : Panel
{
    private readonly Label _documentLabel;
    private readonly Label _countLabel;
    private readonly Label _statusLabel;
    private readonly TextBox _filterTextBox;
    private readonly TextBox _renameTextBox;
    private readonly Button _renameButton;
    private readonly TreeGridView _treeGrid;
    private readonly OverviewSelectionModel _selection = new();
    private DocumentOverview _overview = DocumentOverview.NoDocument;
    private uint? _documentSerialNumber;
    private bool _isPopulatingTree;
    private int _refreshQueued;

    public LayoutFoundryPanel()
    {
        _documentLabel = new Label
        {
            Font = SystemFonts.Bold(),
            Text = "No active document",
        };
        _countLabel = new Label { TextColor = SystemColors.DisabledText };
        _statusLabel = new Label { TextColor = SystemColors.DisabledText };
        _filterTextBox = new TextBox();
        _renameTextBox = new TextBox { Enabled = false };
        _renameButton = new Button
        {
            Text = "Rename",
            Enabled = false,
        };
        _treeGrid = new TreeGridView
        {
            AllowMultipleSelection = true,
            ShowHeader = true,
        };
        _treeGrid.Columns.Add(new GridColumn
        {
            HeaderText = "Folder / Sheet / Detail",
            DataCell = new TextBoxCell
            {
                Binding = Binding.Property<HierarchyTreeItem, string>(item => item.Label),
            },
            Expand = true,
        });
        _treeGrid.Columns.Add(new GridColumn
        {
            HeaderText = "Info",
            DataCell = new TextBoxCell
            {
                Binding = Binding.Property<HierarchyTreeItem, string>(item => item.SecondaryText),
            },
            Width = 180,
        });
        _treeGrid.SelectedItemChanged += OnSelectionChanged;
        _filterTextBox.TextChanged += (_, _) => PopulateTree();
        _renameButton.Click += async (_, _) => await RenameSelectedSheetAsync();

        var layout = new DynamicLayout
        {
            Padding = 12,
            Spacing = new Eto.Drawing.Size(6, 6),
        };
        layout.AddRow(_documentLabel);
        layout.AddRow(_countLabel);
        layout.AddRow(new Label { Text = "Filter" }, _filterTextBox);
        layout.Add(_treeGrid, yscale: true);
        layout.AddRow(_renameTextBox, _renameButton);
        layout.AddRow(_statusLabel);
        Content = layout;

        LayoutFoundryUiHost.OverviewChanged += OnOverviewChanged;
        UnLoad += (_, _) => LayoutFoundryUiHost.OverviewChanged -= OnOverviewChanged;
        RefreshOverview();
    }

    private void OnSelectionChanged(object? sender, EventArgs eventArgs)
    {
        if (_isPopulatingTree)
        {
            return;
        }

        var selectedItems = _treeGrid.SelectedItems.OfType<HierarchyTreeItem>().ToArray();
        var anchor = (_treeGrid.SelectedItem as HierarchyTreeItem)?.Node.Key;
        _selection.Replace(selectedItems.Select(item => item.Node.Key), anchor);
        UpdateRenameControls();
    }

    private void UpdateRenameControls()
    {
        var selected = SelectedSheets().Take(2).ToArray();
        var canRename = selected.Length == 1 &&
                        _treeGrid.SelectedItems.Cast<object>().Count() == 1;
        _renameTextBox.Enabled = canRename;
        _renameButton.Enabled = canRename;
        _renameTextBox.Text = canRename ? selected[0].Name : string.Empty;
        _statusLabel.Text = selected.Length > 1
            ? "Select one sheet to rename. Batch rename follows in Milestone 3."
            : string.Empty;
    }

    private async Task RenameSelectedSheetAsync()
    {
        var selected = SelectedSheets().Take(2).ToArray();
        if (selected.Length != 1 || _treeGrid.SelectedItems.Cast<object>().Count() != 1)
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
            ? "Layout renamed. Use Rhino Undo to revert it."
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

    private void RefreshOverview()
    {
        _overview = LayoutFoundryUiHost.CaptureOverview();
        if (_documentSerialNumber != _overview.DocumentRuntimeSerialNumber)
        {
            _selection.Clear();
            _documentSerialNumber = _overview.DocumentRuntimeSerialNumber;
        }

        _selection.Prune(
            Flatten(OverviewTreeBuilder.Build(_overview)).Select(node => node.Key));
        _documentLabel.Text = _overview.DocumentName;
        _countLabel.Text = _overview.Sheets.Count == 0
            ? "No layout sheets"
            : $"{_overview.Sheets.Count} sheet{(_overview.Sheets.Count == 1 ? string.Empty : "s")} · " +
              $"{_overview.Sheets.Sum(sheet => sheet.DetailCount)} details";
        PopulateTree();
    }

    private void PopulateTree()
    {
        var nodes = OverviewTreeBuilder.Build(_overview, _filterTextBox.Text);
        var expandAll = !string.IsNullOrWhiteSpace(_filterTextBox.Text);
        var items = nodes.Select(node => new HierarchyTreeItem(node, expandAll)).ToArray();
        var visibleItems = Flatten(items).ToDictionary(item => item.Node.Key);
        var preferredSelection = _selection.Anchor is { } anchor && visibleItems.ContainsKey(anchor)
            ? anchor
            : _selection.VisibleSelection(visibleItems.Keys).FirstOrDefault();

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

        UpdateRenameControls();
    }

    private IEnumerable<SheetOverview> SelectedSheets()
    {
        return _treeGrid.SelectedItems
            .OfType<HierarchyTreeItem>()
            .Select(item => item.Node.Sheet)
            .Where(sheet => sheet is not null)
            .Cast<SheetOverview>();
    }

    private static IEnumerable<OverviewTreeNode> Flatten(
        IEnumerable<OverviewTreeNode> nodes)
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

    private static IEnumerable<HierarchyTreeItem> Flatten(
        IEnumerable<HierarchyTreeItem> items)
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
        public HierarchyTreeItem(OverviewTreeNode node, bool expandAll)
        {
            Node = node;
            foreach (var child in node.Children)
            {
                Children.Add(new HierarchyTreeItem(child, expandAll));
            }

            Expanded = expandAll || node.Key.Kind == OverviewNodeKind.Folder;
        }

        public OverviewTreeNode Node { get; }

        public string Label => Node.Label;

        public string SecondaryText => Node.SecondaryText;
    }
}

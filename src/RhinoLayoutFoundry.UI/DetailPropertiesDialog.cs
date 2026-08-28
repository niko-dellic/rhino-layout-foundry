using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

internal sealed class DetailPropertiesDialog : Dialog
{
    private readonly DetailRow[] _targets;
    private readonly IReadOnlyDictionary<Guid, string> _displayModes;
    private readonly FilteredPicker _displayModePicker;
    private readonly Label _review;
    private readonly Label _status;
    private readonly FoundryDialogButton _applyButton;

    internal DetailPropertiesDialog(DocumentSnapshot snapshot, IReadOnlyList<Guid> detailViewportIds)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(detailViewportIds);
        var requested = detailViewportIds.ToHashSet();
        _targets = snapshot.Sheets.Values
            .OrderBy(sheet => sheet.FolderId)
            .ThenBy(sheet => sheet.Order)
            .SelectMany(sheet => sheet.Details
                .Where(detail => requested.Contains(detail.DetailViewportId))
                .Select(detail => new DetailRow(
                    detail.DetailViewportId,
                    sheet.Name,
                    detail.Name,
                    detail.DisplayModeName)))
            .ToArray();
        _displayModes = snapshot.DisplayModes;

        Title = _targets.Length == 1 ? "Detail properties" : "Batch detail properties";
        MinimumSize = new Size(720, 520);
        Resizable = true;
        Padding = new Padding(FoundryTheme.Space4);
        BackgroundColor = FoundryTheme.PanelBackground;

        _displayModePicker = new FilteredPicker(_displayModes.Values, "Search display modes");
        var existingModes = _targets.Select(target => target.DisplayMode)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (existingModes.Length == 1) _displayModePicker.Text = existingModes[0];
        _displayModePicker.ValueChanged += (_, _) => RefreshEditor();

        _review = FoundryTheme.MutedLabel();
        _review.Wrap = WrapMode.Word;
        _status = FoundryTheme.MutedLabel();
        _status.Wrap = WrapMode.Word;
        _applyButton = new FoundryDialogButton(
            "Apply changes",
            FoundryDialogButtonStyle.Primary,
            112);
        _applyButton.Click += async (_, _) => await ApplyAsync();
        var cancel = new FoundryDialogButton(
            "Cancel",
            FoundryDialogButtonStyle.Secondary);
        cancel.Click += (_, _) => Close();
        FoundryDialogActions.Bind(this, _applyButton, cancel);

        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space3,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new Label
                {
                    Text = Title,
                    Font = SystemFonts.Bold(17),
                    TextColor = FoundryTheme.PrimaryText,
                },
                FoundryTheme.MutedLabel(
                    $"Edit {_targets.Length} selected detail{(_targets.Length == 1 ? string.Empty : "s")} without changing the other details on their layouts."),
                new Label { Text = "Targets", Font = SystemFonts.Bold(13) },
                new StackLayoutItem(CreateTargetGrid(), true),
                FoundryTheme.Surface(new StackLayout
                {
                    Padding = new Padding(FoundryTheme.Space3),
                    Spacing = FoundryTheme.Space2,
                    Items =
                    {
                        new Label { Text = "Display mode", Font = SystemFonts.Bold(13) },
                        new Label { Text = "Search or choose a Rhino display mode." },
                        _displayModePicker,
                    },
                }),
                _review,
                _status,
                new TableLayout
                {
                    Rows = { new TableRow(new TableCell(null, true), cancel, _applyButton) },
                    Spacing = new Size(FoundryTheme.Space2, 0),
                },
            },
        };
        RefreshEditor();
    }

    internal bool Succeeded { get; private set; }

    private GridView CreateTargetGrid()
    {
        var grid = new GridView
        {
            DataStore = _targets,
            AllowMultipleSelection = false,
            Height = 210,
        };
        grid.Columns.Add(TextColumn("Layout", row => row.LayoutName, 220));
        grid.Columns.Add(TextColumn("Detail", row => row.DetailName, 220));
        grid.Columns.Add(TextColumn("Display mode", row => row.DisplayMode, 190, true));
        return grid;
    }

    private void RefreshEditor()
    {
        var selected = _displayModePicker.Text.Trim();
        var match = _displayModes.FirstOrDefault(pair =>
            string.Equals(pair.Value, selected, StringComparison.OrdinalIgnoreCase));
        var valid = _targets.Length > 0 && match.Key != Guid.Empty;
        _review.Text = valid
            ? $"{_targets.Length} detail{(_targets.Length == 1 ? string.Empty : "s")}  →  {match.Value}"
            : "Choose an available Rhino display mode.";
        _status.Text = _targets.Length == 0
            ? "The selected detail viewports no longer exist."
            : valid ? string.Empty : "Choose an available Rhino display mode.";
        _applyButton.Enabled = valid;
    }

    private async Task ApplyAsync()
    {
        var match = _displayModes.FirstOrDefault(pair => string.Equals(
            pair.Value,
            _displayModePicker.Text.Trim(),
            StringComparison.OrdinalIgnoreCase));
        if (match.Key == Guid.Empty) return;
        _applyButton.Enabled = false;
        _status.Text = "Applying detail properties…";
        var result = await LayoutFoundryUiHost.SetDisplayModeAsync(
            _targets.Select(target => new OverviewNodeKey(OverviewNodeKind.Detail, target.DetailViewportId)).ToArray(),
            match.Key);
        if (!result.Succeeded)
        {
            _status.Text = string.Join(" ", result.Diagnostics.Select(item => item.Message));
            RefreshEditor();
            return;
        }

        Succeeded = true;
        Close();
    }

    private static GridColumn TextColumn(
        string header,
        System.Linq.Expressions.Expression<Func<DetailRow, string>> binding,
        int width,
        bool expand = false) => new()
    {
        HeaderText = header,
        Width = width,
        Expand = expand,
        DataCell = new TextBoxCell { Binding = Binding.Property(binding) },
    };

    private sealed record DetailRow(
        Guid DetailViewportId,
        string LayoutName,
        string DetailName,
        string DisplayMode);
}

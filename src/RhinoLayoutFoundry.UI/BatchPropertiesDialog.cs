using Eto.Drawing;
using Eto.Forms;
using System.Linq.Expressions;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.UI;

internal sealed class BatchPropertiesDialog : Dialog
{
    private readonly DocumentSnapshot _snapshot;
    private readonly TargetRow[] _targets;
    private readonly IReadOnlyDictionary<Guid, string> _displayModes;
    private readonly TitleBlockChoice[] _titleBlocks;
    private readonly FoundryCheckBox _renameCheck;
    private readonly TextBox _patternBox;
    private readonly NumericStepper _startStepper;
    private readonly NumericStepper _stepStepper;
    private readonly FoundryCheckBox _paperCheck;
    private readonly DropDown _paperPreset;
    private readonly NumericStepper _widthStepper;
    private readonly NumericStepper _heightStepper;
    private readonly DropDown _unitDropDown;
    private readonly FoundryCheckBox _displayModeCheck;
    private readonly FilteredPicker _displayModePicker;
    private readonly FoundryCheckBox _titleBlockCheck;
    private readonly FilteredPicker _titleBlockPicker;
    private readonly FoundryCheckBox _revisionCheck;
    private readonly TextArea _revisionEditor;
    private readonly TextArea _review;
    private readonly Label _status;
    private readonly FoundryDialogButton _applyButton;

    internal BatchPropertiesDialog(DocumentSnapshot snapshot, IReadOnlyList<BatchTarget> targets)
    {
        _snapshot = snapshot;
        _targets = targets.Select(target => new TargetRow(target)).ToArray();
        _displayModes = snapshot.DisplayModes;
        _titleBlocks = new[] { new TitleBlockChoice(null, "Remove title block") }
            .Concat(snapshot.TitleBlockInstances.Values
                .OrderBy(instance => instance.InstanceDefinitionName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(instance => instance.SourcePageName, StringComparer.OrdinalIgnoreCase)
                .Select(instance => new TitleBlockChoice(
                    instance.InstanceObjectId,
                    $"{instance.InstanceDefinitionName}  ·  {instance.SourcePageName}  ·  {instance.InstanceObjectId.ToString()[..8]}")))
            .ToArray();
        Title = "Batch properties";
        MinimumSize = new Size(980, 720);
        Resizable = true;
        Padding = new Padding(FoundryTheme.Space4);
        BackgroundColor = FoundryTheme.PanelBackground;

        _renameCheck = new FoundryCheckBox("Rename layouts");
        _patternBox = new TextBox { PlaceholderText = "Example: A-{index:000}" };
        _startStepper = IntegerStepper(1);
        _stepStepper = IntegerStepper(1);
        _paperCheck = new FoundryCheckBox("Set paper size");
        _paperPreset = new DropDown
        {
            DataStore = new[] { "Custom", "A0 — 841 × 1189 mm", "A1 — 594 × 841 mm", "A2 — 420 × 594 mm", "A3 — 297 × 420 mm", "A4 — 210 × 297 mm", "ANSI A — 8.5 × 11 in", "ANSI B — 11 × 17 in", "ANSI C — 17 × 22 in", "ANSI D — 22 × 34 in" },
            SelectedIndex = 0,
        };
        var first = targets.FirstOrDefault();
        _widthStepper = DimensionStepper(first?.PageWidth > 0 ? first.PageWidth : 420);
        _heightStepper = DimensionStepper(first?.PageHeight > 0 ? first.PageHeight : 297);
        _unitDropDown = new DropDown
        {
            DataStore = new[] { "Millimeters", "Centimeters", "Meters", "Inches", "Feet" },
            SelectedIndex = UnitIndex(first?.PageUnitSystem),
        };
        _displayModeCheck = new FoundryCheckBox("Set detail display mode");
        _displayModePicker = new FilteredPicker(
            _displayModes.Values,
            "Search display modes");
        _titleBlockCheck = new FoundryCheckBox("Assign, replace, or remove title block");
        _titleBlockPicker = new FilteredPicker(
            _titleBlocks.Select(choice => choice.Label),
            "Search title-block instances");
        _revisionCheck = new FoundryCheckBox(
            targets.Count == 1 ? "Replace revision schedule" : "Append revision to included layouts");
        _revisionEditor = new TextArea
        {
            Height = 76,
            Wrap = false,
            ToolTip = "One row per line: Code | Date | Description | Issued by | Checked by",
        };
        if (targets.Count == 1 && snapshot.Sheets.GetValueOrDefault(targets[0].Key.Id)?.TitleBlockData is { } data)
            _revisionEditor.Text = FormatRevisions(data.Revisions);
        _displayModePicker.Opened += (_, _) => _titleBlockPicker.CloseResults();
        _titleBlockPicker.Opened += (_, _) => _displayModePicker.CloseResults();
        var firstTitleBlock = targets.Count == 1
            ? snapshot.Sheets.GetValueOrDefault(targets[0].Key.Id)?.TitleBlockInstanceObjectId
            : null;
        if (firstTitleBlock is { } currentTitleBlock)
            _titleBlockPicker.Text = _titleBlocks.FirstOrDefault(choice => choice.InstanceObjectId == currentTitleBlock)?.Label ?? string.Empty;
        _review = new TextArea { ReadOnly = true, Wrap = false, Height = 120 };
        _status = FoundryTheme.MutedLabel();
        _status.Wrap = WrapMode.Word;
        _applyButton = new FoundryDialogButton(
            "Apply changes",
            FoundryDialogButtonStyle.Primary,
            112);
        var cancel = new FoundryDialogButton(
            "Cancel",
            FoundryDialogButtonStyle.Secondary);
        cancel.Click += (_, _) => Close();
        _applyButton.Click += async (_, _) => await ApplyAsync();
        FoundryDialogActions.Bind(this, _applyButton, cancel);

        var targetGrid = CreateTargetGrid();
        foreach (var check in new[] { _renameCheck, _paperCheck, _displayModeCheck, _titleBlockCheck, _revisionCheck })
            check.CheckedChanged += (_, _) => RefreshEditor();
        _patternBox.TextChanged += (_, _) => RefreshEditor();
        _startStepper.ValueChanged += (_, _) => RefreshEditor();
        _stepStepper.ValueChanged += (_, _) => RefreshEditor();
        _widthStepper.ValueChanged += (_, _) => RefreshEditor();
        _heightStepper.ValueChanged += (_, _) => RefreshEditor();
        _unitDropDown.SelectedIndexChanged += (_, _) => RefreshEditor();
        _displayModePicker.ValueChanged += (_, _) => RefreshEditor();
        _titleBlockPicker.ValueChanged += (_, _) => RefreshEditor();
        _revisionEditor.TextChanged += (_, _) => RefreshEditor();
        _paperPreset.SelectedIndexChanged += (_, _) => ApplyPreset();

        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space3,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                Header(targets.Count),
                new Label { Text = "Targets", Font = SystemFonts.Bold(13) },
                new StackLayoutItem(targetGrid, true),
                new TableLayout
                {
                    Spacing = new Size(FoundryTheme.Space4, 0),
                    Rows =
                    {
                        new TableRow(
                            new TableCell(PropertyCard("Naming", _renameCheck, CreateNamingEditor()), true),
                            new TableCell(PropertyCard("Page", _paperCheck, CreatePaperEditor()), true),
                            new TableCell(new StackLayout
                            {
                                Spacing = FoundryTheme.Space3,
                                Items =
                                {
                                    PropertyCard("Details", _displayModeCheck, CreateDisplayEditor()),
                                    PropertyCard("Title block", _titleBlockCheck, CreateTitleBlockEditor()),
                                    PropertyCard("Revisions", _revisionCheck, CreateRevisionEditor()),
                                },
                            }, true)),
                    },
                },
                new Label { Text = "Review", Font = SystemFonts.Bold(13) },
                new FoundryFormField(_review),
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
            Height = 220,
        };
        grid.Columns.Add(new GridColumn
        {
            HeaderText = "Use",
            Width = 44,
            Editable = true,
            DataCell = new CheckBoxCell
            {
                Binding = Binding.Property<TargetRow, bool?>(row => row.Included),
            },
        });
        grid.Columns.Add(TextColumn("Layout", row => row.Name, 210, true));
        grid.Columns.Add(TextColumn("Paper", row => row.Paper, 190));
        grid.Columns.Add(TextColumn("Details", row => row.DetailCountText, 70));
        grid.Columns.Add(TextColumn("Display mode", row => row.DisplayModes, 180, true));
        grid.Columns.Add(TextColumn("Title block", row => row.TitleBlock, 180, true));
        grid.CellEdited += (_, _) => RefreshEditor();
        return grid;
    }

    private Control CreateNamingEditor() => new StackLayout
    {
        Spacing = FoundryTheme.Space2,
        Items =
        {
            new FoundryFormField(_patternBox),
            new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = FoundryTheme.Space2,
                Items =
                {
                    new Label { Text = "Start" },
                    new FoundryFormField(_startStepper),
                    new Label { Text = "Step" },
                    new FoundryFormField(_stepStepper),
                },
            },
        },
    };

    private Control CreatePaperEditor() => new StackLayout
    {
        Spacing = FoundryTheme.Space2,
        Items =
        {
            new FoundryFormField(_paperPreset),
            new TableLayout
            {
                Spacing = new Size(FoundryTheme.Space2, FoundryTheme.Space2),
                Rows =
                {
                    new TableRow(new Label { Text = "Width" }, new FoundryFormField(_widthStepper)),
                    new TableRow(new Label { Text = "Height" }, new FoundryFormField(_heightStepper)),
                    new TableRow(new Label { Text = "Units" }, new FoundryFormField(_unitDropDown)),
                },
            },
        },
    };

    private Control CreateDisplayEditor() => new StackLayout
    {
        Spacing = FoundryTheme.Space2,
        Items =
        {
            new Label { Text = "Search or choose a Rhino display mode." },
            _displayModePicker,
        },
    };

    private Control CreateTitleBlockEditor() => new StackLayout
    {
        Spacing = FoundryTheme.Space2,
        Items =
        {
            new Label { Text = "Search page-space block instances or remove the assigned title block." },
            _titleBlockPicker,
        },
    };

    private Control CreateRevisionEditor() => new StackLayout
    {
        Spacing = FoundryTheme.Space2,
        Items =
        {
            FoundryTheme.MutedLabel(_targets.Length == 1
                ? "Edit, remove, or reorder rows. Use: Code | Date | Description | Issued by | Checked by"
                : "Enter one row to append: Code | Date | Description | Issued by | Checked by"),
            new FoundryFormField(_revisionEditor),
        },
    };

    private static Control PropertyCard(string title, FoundryCheckBox toggle, Control editor) =>
        FoundryTheme.Surface(new StackLayout
        {
            Padding = new Padding(FoundryTheme.Space3),
            Spacing = FoundryTheme.Space2,
            Items =
            {
                new Label { Text = title, Font = SystemFonts.Bold(13) },
                toggle,
                editor,
            },
        });

    private BatchUpdateSheetsRequest Request()
    {
        var ids = _targets.Where(row => row.Included == true).Select(row => row.Id).ToArray();
        var modeId = _displayModeCheck.Checked == true
            ? _displayModes.FirstOrDefault(pair => string.Equals(pair.Value, _displayModePicker.Text.Trim(), StringComparison.OrdinalIgnoreCase)).Key
            : (Guid?)null;
        if (modeId == Guid.Empty) modeId = null;
        var changesTitleBlock = _titleBlockCheck.Checked == true;
        var titleBlockChoice = changesTitleBlock
            ? _titleBlocks.FirstOrDefault(choice => string.Equals(
                choice.Label,
                _titleBlockPicker.Text.Trim(),
                StringComparison.OrdinalIgnoreCase))
            : null;
        var revisions = ParseRevisions(out _);
        return new BatchUpdateSheetsRequest(
            _snapshot.DocumentRuntimeSerialNumber,
            _snapshot.Revision,
            ids,
            _renameCheck.Checked == true ? _patternBox.Text : null,
            (int)_startStepper.Value,
            (int)_stepStepper.Value,
            _paperCheck.Checked == true ? _widthStepper.Value : null,
            _paperCheck.Checked == true ? _heightStepper.Value : null,
            _paperCheck.Checked == true ? _unitDropDown.SelectedValue?.ToString() : null,
            modeId,
            changesTitleBlock,
            titleBlockChoice?.InstanceObjectId,
            ReplaceRevisionSchedule: _revisionCheck.Checked == true && _targets.Length == 1 ? revisions : null,
            AppendRevision: _revisionCheck.Checked == true && _targets.Length > 1 ? revisions.FirstOrDefault() : null);
    }

    private void RefreshEditor()
    {
        var rename = _renameCheck.Checked == true;
        _patternBox.Enabled = rename;
        _startStepper.Enabled = rename;
        _stepStepper.Enabled = rename;
        var paper = _paperCheck.Checked == true;
        _paperPreset.Enabled = paper;
        _widthStepper.Enabled = paper;
        _heightStepper.Enabled = paper;
        _unitDropDown.Enabled = paper;
        var display = _displayModeCheck.Checked == true;
        _displayModePicker.Enabled = display;
        var titleBlock = _titleBlockCheck.Checked == true;
        _titleBlockPicker.Enabled = titleBlock;
        var revisionsEnabled = _revisionCheck.Checked == true;
        _revisionEditor.Enabled = revisionsEnabled;

        var plan = new BatchUpdateSheetsPlanner().Plan(Request(), _snapshot);
        var change = plan.Changes.OfType<BatchUpdateSheetsChange>().SingleOrDefault();
        var review = new List<string>();
        if (change is not null)
        {
            review.Add($"{change.SheetPageViewIds.Count} layout{(change.SheetPageViewIds.Count == 1 ? string.Empty : "s")} included");
            if (change.NewNames.Count > 0)
                review.AddRange(change.NewNames.Select(pair =>
                    $"{_snapshot.Sheets[pair.Key].Name}  →  {pair.Value}"));
            if (change.PaperWidth is { } width && change.PaperHeight is { } height)
                review.Add($"Paper  →  {width:0.###} × {height:0.###} {change.PaperUnitSystem}");
            if (change.DetailDisplayModeId is { } modeId)
                review.Add($"All included details  →  {_displayModes.GetValueOrDefault(modeId, "Unknown")}");
            if (change.ChangeTitleBlock)
            {
                var choice = _titleBlocks.FirstOrDefault(item =>
                    item.InstanceObjectId == change.TitleBlockSourceInstanceObjectId);
                review.Add(change.TitleBlockSourceInstanceObjectId is null
                    ? "Title block  →  Remove assigned title block"
                    : $"Title block  →  {choice?.Label ?? "Unavailable instance"}");
            }
            if (change.ReplaceRevisionSchedule is { } replacements)
                review.Add($"Revision schedule  →  {replacements.Count} row{(replacements.Count == 1 ? string.Empty : "s")}");
            if (change.AppendRevision is { } appended)
                review.Add($"Append revision  →  {FormatRevision(appended)}");
        }
        _review.Text = review.Count == 0 ? "Choose a property to change." : string.Join(Environment.NewLine, review);
        var localError = display && !_displayModes.Values.Any(name =>
            string.Equals(name, _displayModePicker.Text.Trim(), StringComparison.OrdinalIgnoreCase))
            ? "Choose an available Rhino display mode."
            : null;
        localError ??= titleBlock && !_titleBlocks.Any(choice =>
            string.Equals(choice.Label, _titleBlockPicker.Text.Trim(), StringComparison.OrdinalIgnoreCase))
            ? "Choose an available title-block instance or Remove title block."
            : null;
        ParseRevisions(out var revisionError);
        localError ??= revisionsEnabled ? revisionError : null;
        _status.Text = localError ?? string.Join(" ", plan.Diagnostics.Select(item => item.Message));
        _applyButton.Enabled = plan.CanApply && localError is null;
    }

    private IReadOnlyList<SheetRevisionRecord> ParseRevisions(out string? error)
    {
        error = null;
        var result = new List<SheetRevisionRecord>();
        var lines = _revisionEditor.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in lines)
        {
            var parts = raw.Split('|').Select(part => part.Trim()).ToArray();
            if (parts.Length is < 1 or > 5)
            {
                error = "Revision rows must contain at most five pipe-separated values.";
                return result;
            }
            Array.Resize(ref parts, 5);
            for (var index = 0; index < parts.Length; index++) parts[index] ??= string.Empty;
            result.Add(new SheetRevisionRecord(parts[0], parts[1], parts[2], parts[3], parts[4]));
        }
        if (_targets.Length > 1 && result.Count != 1)
            error = "Enter exactly one revision row when editing multiple layouts.";
        return result;
    }

    private static string FormatRevisions(IEnumerable<SheetRevisionRecord> revisions) =>
        string.Join(Environment.NewLine, revisions.Select(FormatRevision));

    private static string FormatRevision(SheetRevisionRecord revision) => string.Join(" | ",
        revision.Code, revision.Date, revision.Description, revision.IssuedBy, revision.CheckedBy);

    private async Task ApplyAsync()
    {
        _applyButton.Enabled = false;
        _status.Text = "Applying batch changes…";
        var result = await LayoutFoundryUiHost.BatchUpdateSheetsAsync(Request());
        if (!result.Succeeded)
        {
            _status.Text = string.Join(" ", result.Diagnostics.Select(item => item.Message));
            RefreshEditor();
            return;
        }
        Succeeded = true;
        Close();
    }

    private void ApplyPreset()
    {
        var preset = _paperPreset.SelectedIndex switch
        {
            1 => (841d, 1189d, "Millimeters"),
            2 => (594d, 841d, "Millimeters"),
            3 => (420d, 594d, "Millimeters"),
            4 => (297d, 420d, "Millimeters"),
            5 => (210d, 297d, "Millimeters"),
            6 => (8.5d, 11d, "Inches"),
            7 => (11d, 17d, "Inches"),
            8 => (17d, 22d, "Inches"),
            9 => (22d, 34d, "Inches"),
            _ => ((double?)null, (double?)null, (string?)null),
        };
        if (preset.Item1 is not { } width || preset.Item2 is not { } height || preset.Item3 is not { } unit) return;
        _widthStepper.Value = width;
        _heightStepper.Value = height;
        _unitDropDown.SelectedIndex = UnitIndex(unit);
        RefreshEditor();
    }

    private static GridColumn TextColumn(
        string header,
        Expression<Func<TargetRow, string>> property,
        int width,
        bool expand = false) => new()
    {
        HeaderText = header,
        Width = width,
        Expand = expand,
        DataCell = new TextBoxCell { Binding = Binding.Property(property) },
    };

    private static NumericStepper IntegerStepper(double value) => new()
    {
        Value = value, MinValue = -999999, MaxValue = 999999, DecimalPlaces = 0, Width = 72,
    };

    private static NumericStepper DimensionStepper(double value) => new()
    {
        Value = value, MinValue = 0.001, MaxValue = 1000000, DecimalPlaces = 3,
    };

    private static int UnitIndex(string? unit) => unit?.ToLowerInvariant() switch
    {
        "centimeters" => 1,
        "meters" => 2,
        "inches" => 3,
        "feet" => 4,
        _ => 0,
    };

    private static Control Header(int count) => new StackLayout
    {
        Spacing = FoundryTheme.Space1,
        Items =
        {
            new Label { Text = "Batch properties", Font = SystemFonts.Bold(17), TextColor = FoundryTheme.PrimaryText },
            FoundryTheme.MutedLabel($"Review {count} resolved layout{(count == 1 ? string.Empty : "s")}, then apply only the properties you enable."),
        },
    };

    private sealed class TargetRow
    {
        internal TargetRow(BatchTarget target)
        {
            Id = target.Key.Id;
            Included = target.Included;
            Name = target.Label;
            Paper = target.PageWidth > 0
                ? $"{target.PageWidth:0.###} × {target.PageHeight:0.###} {target.PageUnitSystem}"
                : "—";
            DetailCountText = target.DetailCount.ToString();
            DisplayModes = target.DisplayModeSummary;
            TitleBlock = target.TitleBlockSummary;
        }

        public Guid Id { get; }
        public bool? Included { get; set; }
        public string Name { get; }
        public string Paper { get; }
        public string DetailCountText { get; }
        public string DisplayModes { get; }
        public string TitleBlock { get; }
    }

    private sealed record TitleBlockChoice(Guid? InstanceObjectId, string Label);
}

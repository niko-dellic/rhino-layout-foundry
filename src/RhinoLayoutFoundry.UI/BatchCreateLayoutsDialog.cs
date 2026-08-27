using System.Linq.Expressions;
using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.UI;

internal sealed class BatchCreateLayoutsDialog : Dialog
{
    private const string InheritDisplayMode = "Use layout/template setting";
    private const string InheritNamedView = "Use layout/template camera";
    private readonly DocumentSnapshot _snapshot;
    private readonly IReadOnlyList<(Guid Id, string Label)> _folders;
    private readonly LayoutChoice[] _layoutChoices;
    private readonly TitleBlockChoice[] _titleBlockChoices;
    private readonly DropDown _destinationDropDown;
    private readonly NumericStepper _quantityStepper;
    private readonly TextBox _patternBox;
    private readonly NumericStepper _startStepper;
    private readonly NumericStepper _stepStepper;
    private readonly DropDown _layoutTypeDropDown;
    private readonly DropDown _paperPresetDropDown;
    private readonly DropDown _orientationDropDown;
    private readonly NumericStepper _widthStepper;
    private readonly NumericStepper _heightStepper;
    private readonly DropDown _unitDropDown;
    private readonly FilteredPicker _displayModePicker;
    private readonly FilteredPicker _titleBlockPicker;
    private readonly DropDown _namedViewDropDown;
    private readonly GridView _previewGrid;
    private readonly Label _countLabel;
    private readonly Label _status;
    private readonly Button _createButton;
    private bool _updatingPaper;

    internal BatchCreateLayoutsDialog(DocumentSnapshot snapshot, Guid? preferredFolderId)
    {
        _snapshot = snapshot;
        _folders = FolderChoices(snapshot);
        _layoutChoices = LayoutChoices(snapshot);
        _titleBlockChoices = TitleBlockChoices(snapshot);
        Title = "Create layouts";
        MinimumSize = new Size(1080, 760);
        Resizable = true;
        Padding = new Padding(FoundryTheme.Space4);
        BackgroundColor = FoundryTheme.PanelBackground;

        _destinationDropDown = new DropDown { DataStore = _folders.Select(item => item.Label).ToArray() };
        _destinationDropDown.SelectedIndex = PreferredFolderIndex(preferredFolderId);
        _quantityStepper = IntegerStepper(1, 1, 999);
        _patternBox = new TextBox { Text = "Page {index}" };
        _startStepper = IntegerStepper(FirstAvailablePageNumber(snapshot), -999999, 999999);
        _stepStepper = IntegerStepper(1, -999999, 999999);
        _layoutTypeDropDown = new DropDown
        {
            DataStore = _layoutChoices.Select(choice => choice.Label).ToArray(),
            SelectedIndex = 1,
        };
        _paperPresetDropDown = new DropDown
        {
            DataStore = PaperPresets.Select(preset => preset.Label).ToArray(),
            SelectedIndex = 3,
        };
        _orientationDropDown = new DropDown
        {
            DataStore = new[] { "Landscape", "Portrait" },
            SelectedIndex = 0,
        };
        _widthStepper = DimensionStepper(594);
        _heightStepper = DimensionStepper(420);
        _unitDropDown = new DropDown
        {
            DataStore = Units,
            SelectedIndex = 0,
        };
        _displayModePicker = new FilteredPicker(
            new[] { InheritDisplayMode }.Concat(snapshot.DisplayModes.Values),
            "Search display modes");
        _displayModePicker.Text = InheritDisplayMode;
        _titleBlockPicker = new FilteredPicker(
            _titleBlockChoices.Select(choice => choice.Label),
            "Search title blocks");
        _titleBlockPicker.Text = _titleBlockChoices[0].Label;
        _namedViewDropDown = new DropDown
        {
            DataStore = new[] { InheritNamedView }
                .Concat(snapshot.NamedViews.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
                .ToArray(),
            SelectedIndex = 0,
        };
        _previewGrid = CreatePreviewGrid();
        _countLabel = new Label { Font = SystemFonts.Bold(13), TextColor = FoundryTheme.PrimaryText };
        _status = FoundryTheme.MutedLabel();
        _status.Wrap = WrapMode.Word;
        _createButton = FoundryTheme.ConfigureButton(new Button { Text = "Create layouts" }, 118);
        var cancel = FoundryTheme.ConfigureButton(new Button { Text = "Cancel" });
        cancel.Click += (_, _) => Close();
        _createButton.Click += async (_, _) => await CreateAsync();
        AbortButton = cancel;

        foreach (var dropDown in new[]
                 {
                     _destinationDropDown, _unitDropDown, _namedViewDropDown,
                 })
            dropDown.SelectedIndexChanged += (_, _) => QueueRefreshPreview();
        foreach (var stepper in new[]
                 {
                     _quantityStepper, _startStepper, _stepStepper, _widthStepper, _heightStepper,
                 })
            stepper.ValueChanged += (_, _) => RefreshPreview();
        _patternBox.TextChanged += (_, _) => RefreshPreview();
        _paperPresetDropDown.SelectedIndexChanged += (_, _) =>
            Application.Instance.AsyncInvoke(ApplyPaperPreset);
        _orientationDropDown.SelectedIndexChanged += (_, _) =>
            Application.Instance.AsyncInvoke(ApplyPaperPreset);
        _layoutTypeDropDown.SelectedIndexChanged += (_, _) =>
            Application.Instance.AsyncInvoke(ApplyLayoutDefaults);
        _displayModePicker.ValueChanged += (_, _) => RefreshPreview();
        _titleBlockPicker.ValueChanged += (_, _) => RefreshPreview();
        _displayModePicker.Opened += (_, _) => _titleBlockPicker.CloseResults();
        _titleBlockPicker.Opened += (_, _) => _displayModePicker.CloseResults();

        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space3,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                Header(),
                new TableLayout
                {
                    Spacing = new Size(FoundryTheme.Space3, FoundryTheme.Space3),
                    Rows =
                    {
                        new TableRow(
                            new TableCell(Card("Batch", CreateBatchEditor()), true),
                            new TableCell(Card("Layout", CreateLayoutEditor()), true),
                            new TableCell(Card("Paper", CreatePaperEditor()), true)),
                        new TableRow(
                            new TableCell(Card("Details", CreateDetailEditor()), true),
                            new TableCell(Card("Title block", CreateTitleBlockEditor()), true),
                            new TableCell(Card("View", CreateNamedViewEditor()), true)),
                    },
                },
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Items = { _countLabel, new StackLayoutItem(null, true) },
                },
                new StackLayoutItem(_previewGrid, true),
                _status,
                new TableLayout
                {
                    Rows = { new TableRow(new TableCell(null, true), cancel, _createButton) },
                    Spacing = new Size(FoundryTheme.Space2, 0),
                },
            },
        };
        RefreshPreview();
    }

    internal int CreatedCount { get; private set; }
    internal bool Succeeded { get; private set; }

    private Control CreateBatchEditor() => new TableLayout
    {
        Spacing = new Size(FoundryTheme.Space2, FoundryTheme.Space2),
        Rows =
        {
            new TableRow(new Label { Text = "Destination" }, new TableCell(_destinationDropDown, true)),
            new TableRow(new Label { Text = "Quantity" }, _quantityStepper),
            new TableRow(new Label { Text = "Name / pattern" }, new TableCell(_patternBox, true)),
            new TableRow(new Label { Text = "Start / step" }, new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = FoundryTheme.Space1,
                Items = { _startStepper, _stepStepper },
            }),
        },
    };

    private Control CreateLayoutEditor() => new StackLayout
    {
        Spacing = FoundryTheme.Space2,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Items =
        {
            new Label { Text = "Choose a built-in arrangement or a captured layout template." },
            _layoutTypeDropDown,
        },
    };

    private Control CreatePaperEditor() => new StackLayout
    {
        Spacing = FoundryTheme.Space2,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Items =
        {
            _paperPresetDropDown,
            _orientationDropDown,
            new TableLayout
            {
                Spacing = new Size(FoundryTheme.Space2, FoundryTheme.Space1),
                Rows =
                {
                    new TableRow(new Label { Text = "Width" }, _widthStepper),
                    new TableRow(new Label { Text = "Height" }, _heightStepper),
                    new TableRow(new Label { Text = "Units" }, _unitDropDown),
                },
            },
        },
    };

    private Control CreateDetailEditor() => new StackLayout
    {
        Spacing = FoundryTheme.Space2,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Items =
        {
            new Label { Text = "Apply one Rhino display mode to every created detail." },
            _displayModePicker,
        },
    };

    private Control CreateTitleBlockEditor() => new StackLayout
    {
        Spacing = FoundryTheme.Space2,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Items =
        {
            new Label { Text = "Use the template, no block, or copy a page-space block instance." },
            _titleBlockPicker,
        },
    };

    private Control CreateNamedViewEditor() => new StackLayout
    {
        Spacing = FoundryTheme.Space2,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Items =
        {
            new Label { Text = "Optionally apply one named view to every created detail." },
            _namedViewDropDown,
        },
    };

    private GridView CreatePreviewGrid()
    {
        var grid = new GridView { AllowMultipleSelection = false, Height = 270 };
        grid.Columns.Add(TextColumn("#", row => row.Index, 44));
        grid.Columns.Add(TextColumn("Layout name", row => row.Name, 190, true));
        grid.Columns.Add(TextColumn("Layout type", row => row.LayoutType, 190, true));
        grid.Columns.Add(TextColumn("Paper", row => row.Paper, 170));
        grid.Columns.Add(TextColumn("Details", row => row.Details, 70));
        grid.Columns.Add(TextColumn("Display mode", row => row.DisplayMode, 150, true));
        grid.Columns.Add(TextColumn("Title block", row => row.TitleBlock, 160, true));
        return grid;
    }

    private BatchCreateSheetsRequest Request()
    {
        var layout = _layoutChoices[Math.Max(0, _layoutTypeDropDown.SelectedIndex)];
        var displayModeId = _snapshot.DisplayModes.FirstOrDefault(pair => string.Equals(
            pair.Value, _displayModePicker.Text.Trim(), StringComparison.OrdinalIgnoreCase)).Key;
        var titleBlock = _titleBlockChoices.FirstOrDefault(choice => string.Equals(
            choice.Label, _titleBlockPicker.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        var namedView = _namedViewDropDown.SelectedIndex > 0
            ? _namedViewDropDown.SelectedValue?.ToString()
            : null;
        var spec = new LayoutCreationSpec(
            (int)_quantityStepper.Value,
            new PaperRecipe(_widthStepper.Value, _heightStepper.Value, Units[Math.Max(0, _unitDropDown.SelectedIndex)]),
            layout.BuiltInLayout,
            layout.TemplateId,
            displayModeId == Guid.Empty ? null : displayModeId,
            titleBlock?.UseTemplate ?? false,
            titleBlock?.SourceInstanceObjectId,
            namedView);
        return new BatchCreateSheetsRequest(
            _snapshot.DocumentRuntimeSerialNumber,
            _snapshot.Revision,
            _folders[Math.Max(0, _destinationDropDown.SelectedIndex)].Id,
            [],
            _patternBox.Text,
            (int)_startStepper.Value,
            (int)_stepStepper.Value,
            CreationSpecs: [spec]);
    }

    private void RefreshPreview()
    {
        if (_updatingPaper || _folders.Count == 0) return;
        var plan = new BatchCreateSheetsPlanner().Plan(Request(), _snapshot);
        var changes = plan.Changes.OfType<CreateSheetFromTemplateChange>().ToArray();
        _previewGrid.DataStore = changes.Select((change, index) => new CreationPreviewRow(
            (index + 1).ToString(),
            change.Name,
            change.Template.Name,
            $"{change.Template.Paper.Width:0.###} × {change.Template.Paper.Height:0.###} {change.Template.Paper.UnitSystem}",
            change.Template.DetailSlots.Count.ToString(),
            DisplayModeSummary(change.Template),
            change.Template.TitleBlock?.InstanceDefinitionName ?? "None")).ToArray();
        CreatedCount = changes.Length;
        _countLabel.Text = $"Layouts to create  ·  {CreatedCount}";
        var pickerError = PickerError();
        var diagnostics = string.Join(" ", plan.Diagnostics
            .Where(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(item => item.Message));
        _status.Text = pickerError ?? diagnostics;
        _createButton.Text = CreatedCount == 1 ? "Create layout" : $"Create {CreatedCount} layouts";
        _createButton.Enabled = plan.CanApply && pickerError is null;
    }

    private void QueueRefreshPreview()
    {
        Application.Instance.AsyncInvoke(RefreshPreview);
    }

    private string? PickerError()
    {
        if (!string.Equals(_displayModePicker.Text.Trim(), InheritDisplayMode, StringComparison.OrdinalIgnoreCase) &&
            !_snapshot.DisplayModes.Values.Any(name => string.Equals(
                name, _displayModePicker.Text.Trim(), StringComparison.OrdinalIgnoreCase)))
            return "Choose an available Rhino display mode or use the layout/template setting.";
        if (!_titleBlockChoices.Any(choice => string.Equals(
                choice.Label, _titleBlockPicker.Text.Trim(), StringComparison.OrdinalIgnoreCase)))
            return "Choose Use layout template, No title block, or an available title-block instance.";
        return null;
    }

    private async Task CreateAsync()
    {
        _createButton.Enabled = false;
        _status.Text = $"Creating {CreatedCount} layout{(CreatedCount == 1 ? string.Empty : "s")}…";
        var result = await LayoutFoundryUiHost.BatchCreateSheetsAsync(Request());
        if (!result.Succeeded)
        {
            _status.Text = string.Join(" ", result.Diagnostics.Select(item => item.Message));
            RefreshPreview();
            return;
        }
        Succeeded = true;
        Close();
    }

    private void ApplyLayoutDefaults()
    {
        var choice = _layoutChoices[Math.Max(0, _layoutTypeDropDown.SelectedIndex)];
        if (choice.Template is null)
        {
            RefreshPreview();
            return;
        }
        _updatingPaper = true;
        _paperPresetDropDown.SelectedIndex = 0;
        _widthStepper.Value = choice.Template.Paper.Width;
        _heightStepper.Value = choice.Template.Paper.Height;
        _unitDropDown.SelectedIndex = UnitIndex(choice.Template.Paper.UnitSystem);
        _orientationDropDown.SelectedIndex = choice.Template.Paper.Width >= choice.Template.Paper.Height ? 0 : 1;
        _updatingPaper = false;
        RefreshPreview();
    }

    private void ApplyPaperPreset()
    {
        if (_updatingPaper || _paperPresetDropDown.SelectedIndex <= 0)
        {
            RefreshPreview();
            return;
        }
        _updatingPaper = true;
        var preset = PaperPresets[_paperPresetDropDown.SelectedIndex];
        var landscape = _orientationDropDown.SelectedIndex == 0;
        _widthStepper.Value = landscape ? Math.Max(preset.Width, preset.Height) : Math.Min(preset.Width, preset.Height);
        _heightStepper.Value = landscape ? Math.Min(preset.Width, preset.Height) : Math.Max(preset.Width, preset.Height);
        _unitDropDown.SelectedIndex = UnitIndex(preset.UnitSystem);
        _updatingPaper = false;
        RefreshPreview();
    }

    private string DisplayModeSummary(SheetTemplateRecipe template)
    {
        var names = template.DetailSlots
            .Select(slot => slot.DisplayModeId is { } id ? _snapshot.DisplayModes.GetValueOrDefault(id) : null)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return template.DetailSlots.Count == 0
            ? "—"
            : names.Length switch { 0 => "Rhino default", 1 => names[0]!, _ => "Mixed" };
    }

    private int PreferredFolderIndex(Guid? preferredFolderId)
    {
        var match = _folders.Select((folder, index) => (folder, index))
            .FirstOrDefault(item => item.folder.Id == preferredFolderId);
        return match.folder == default ? 0 : match.index;
    }

    private static Control Card(string title, Control content) => FoundryTheme.Surface(new StackLayout
    {
        Padding = new Padding(FoundryTheme.Space3),
        Spacing = FoundryTheme.Space2,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Items =
        {
            new Label { Text = title, Font = SystemFonts.Bold(13), TextColor = FoundryTheme.PrimaryText },
            content,
        },
    });

    private static GridColumn TextColumn(
        string header,
        Expression<Func<CreationPreviewRow, string>> property,
        int width,
        bool expand = false) => new()
    {
        HeaderText = header,
        Width = width,
        Expand = expand,
        DataCell = new TextBoxCell { Binding = Binding.Property(property) },
    };

    private static NumericStepper IntegerStepper(double value, double min, double max) => new()
    {
        Value = value, MinValue = min, MaxValue = max, DecimalPlaces = 0, Width = 76,
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

    private static int FirstAvailablePageNumber(DocumentSnapshot snapshot)
    {
        var maximum = snapshot.Sheets.Values
            .Select(sheet => sheet.Name.StartsWith("Page ", StringComparison.OrdinalIgnoreCase) &&
                             int.TryParse(sheet.Name[5..].Trim(), out var index)
                ? index
                : 0)
            .DefaultIfEmpty(0)
            .Max();
        return maximum + 1;
    }

    private static LayoutChoice[] LayoutChoices(DocumentSnapshot snapshot) =>
    [
        new LayoutChoice("Blank — no details", BuiltInLayoutKind.Blank, null, null),
        new LayoutChoice("1 Detail — Top", BuiltInLayoutKind.SingleDetail, null, null),
        new LayoutChoice("2 Details — Horizontal", BuiltInLayoutKind.TwoDetailsHorizontal, null, null),
        new LayoutChoice("2 Details — Vertical", BuiltInLayoutKind.TwoDetailsVertical, null, null),
        new LayoutChoice("4 Details — Grid", BuiltInLayoutKind.FourDetailsGrid, null, null),
        .. snapshot.Templates
            .OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
            .Select(template => new LayoutChoice(
                $"Template — {template.Name}", BuiltInLayoutKind.Blank, template.Id, template)),
    ];

    private static TitleBlockChoice[] TitleBlockChoices(DocumentSnapshot snapshot) =>
    [
        new TitleBlockChoice(true, null, "Use layout template"),
        new TitleBlockChoice(false, null, "No title block"),
        .. snapshot.TitleBlockInstances.Values
            .Where(instance => instance.Transform is { Count: 16 })
            .OrderBy(instance => instance.InstanceDefinitionName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(instance => instance.SourcePageName, StringComparer.OrdinalIgnoreCase)
            .Select(instance => new TitleBlockChoice(
                false,
                instance.InstanceObjectId,
                $"{instance.InstanceDefinitionName}  ·  {instance.SourcePageName}  ·  {instance.InstanceObjectId.ToString()[..8]}")),
    ];

    private static IReadOnlyList<(Guid Id, string Label)> FolderChoices(DocumentSnapshot snapshot)
    {
        string Path(Guid id)
        {
            var parts = new List<string>();
            var seen = new HashSet<Guid>();
            while (snapshot.Folders.TryGetValue(id, out var folder) && seen.Add(id))
            {
                if (id != snapshot.RootFolderId) parts.Add(folder.Name);
                if (folder.ParentId is not { } parent) break;
                id = parent;
            }
            parts.Reverse();
            return parts.Count == 0 ? "Root" : string.Join(" / ", parts);
        }
        return snapshot.Folders.Values.OrderBy(folder => Path(folder.Id), StringComparer.OrdinalIgnoreCase)
            .Select(folder => (folder.Id, Path(folder.Id))).ToArray();
    }

    private static Control Header() => new StackLayout
    {
        Spacing = FoundryTheme.Space1,
        Items =
        {
            new Label { Text = "Create layouts", Font = SystemFonts.Bold(17), TextColor = FoundryTheme.PrimaryText },
            FoundryTheme.MutedLabel("Configure the batch once, review every resulting layout, then create it atomically."),
        },
    };

    private static readonly string[] Units = ["Millimeters", "Centimeters", "Meters", "Inches", "Feet"];

    private static readonly PaperPreset[] PaperPresets =
    [
        new("Custom", 0, 0, "Millimeters"),
        new("A0 — 841 × 1189 mm", 841, 1189, "Millimeters"),
        new("A1 — 594 × 841 mm", 594, 841, "Millimeters"),
        new("A2 — 420 × 594 mm", 420, 594, "Millimeters"),
        new("A3 — 297 × 420 mm", 297, 420, "Millimeters"),
        new("A4 — 210 × 297 mm", 210, 297, "Millimeters"),
        new("ANSI A — 8.5 × 11 in", 8.5, 11, "Inches"),
        new("ANSI B — 11 × 17 in", 11, 17, "Inches"),
        new("ANSI C — 17 × 22 in", 17, 22, "Inches"),
        new("ANSI D — 22 × 34 in", 22, 34, "Inches"),
    ];

    private sealed record LayoutChoice(
        string Label,
        BuiltInLayoutKind BuiltInLayout,
        Guid? TemplateId,
        SheetTemplateRecipe? Template);
    private sealed record TitleBlockChoice(bool UseTemplate, Guid? SourceInstanceObjectId, string Label);
    private sealed record PaperPreset(string Label, double Width, double Height, string UnitSystem);
    private sealed record CreationPreviewRow(
        string Index,
        string Name,
        string LayoutType,
        string Paper,
        string Details,
        string DisplayMode,
        string TitleBlock);
}

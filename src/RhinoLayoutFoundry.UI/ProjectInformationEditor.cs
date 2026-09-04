using System.Security.Cryptography;
using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.UI;

internal sealed class ProjectInformationEditor : Panel
{
    private const int MaximumLogoBytes = 5 * 1024 * 1024;
    private readonly Dictionary<string, TextBox> _fields = new(StringComparer.Ordinal);
    private readonly Dictionary<TitleBlockContentField, FoundryCheckBox> _includeChecks = new();
    private readonly List<CustomFieldRow> _customRows = [];
    private readonly StackLayout _customRowsLayout = new()
    {
        Spacing = FoundryTheme.Space2,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
    };
    private readonly TextArea _siteAddress = Area(48);
    private readonly TextArea _firmAddress = Area(48);
    private readonly FoundryCheckBox _reserveRevisionArea = new("Reserve blank revision area");
    private readonly Label _logoLabel = FoundryTheme.MutedLabel();
    private readonly ImageView _logoPreview = new() { Size = new Size(112, 54) };
    private BrandAsset? _logo;

    internal ProjectInformationEditor(ProjectInformation value)
    {
        MinimumSize = new Size(620, 0);
        Content = new StackLayout
        {
            Padding = new Padding(FoundryTheme.Space1, FoundryTheme.Space2),
            Spacing = FoundryTheme.Space4,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            Items =
            {
                FoundryTheme.MutedLabel(
                    "Checked fields are included in every Foundry-managed title block. Values remain saved when hidden."),
                Section("Project", new StackLayout
                {
                    Spacing = FoundryTheme.Space3,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Items =
                    {
                        Pair(
                            Included(TitleBlockContentField.ProjectName, "Project name", Field("projectName")),
                            Included(TitleBlockContentField.ProjectNumber, "Project number", Field("projectNumber"))),
                        Pair(
                            Included(TitleBlockContentField.ClientName, "Client", Field("clientName")),
                            Included(TitleBlockContentField.ProjectPhase, "Phase", Field("projectPhase"))),
                        Pair(
                            Included(TitleBlockContentField.ProjectStatus, "Status", Field("projectStatus")),
                            Included(TitleBlockContentField.SiteAddress, "Site address", _siteAddress)),
                    },
                }),
                Section("Firm", new StackLayout
                {
                    Spacing = FoundryTheme.Space3,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Items =
                    {
                        Pair(
                            Included(TitleBlockContentField.FirmName, "Firm name", Field("firmName")),
                            Included(TitleBlockContentField.FirmPhone, "Phone", Field("firmPhone"))),
                        Pair(
                            Included(TitleBlockContentField.FirmEmail, "Email", Field("firmEmail")),
                            Included(TitleBlockContentField.FirmWebsite, "Website", Field("firmWebsite"))),
                        Pair(
                            Included(TitleBlockContentField.FirmAddress, "Firm address", _firmAddress),
                            Included(TitleBlockContentField.FirmRegistration, "Registration / license",
                                Field("firmRegistration"))),
                        LogoEditor(),
                    },
                }),
                Section("Issue information", new StackLayout
                {
                    Spacing = FoundryTheme.Space3,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Items =
                    {
                        Pair(
                            Included(TitleBlockContentField.IssueDate, "Issue date", Field("issueDate")),
                            Included(TitleBlockContentField.IssuePurpose, "Issue purpose", Field("issuePurpose"))),
                        Pair(
                            Included(TitleBlockContentField.DrawnBy, "Drawn by", Field("drawnBy")),
                            Included(TitleBlockContentField.CheckedBy, "Checked by", Field("checkedBy"))),
                        Pair(
                            Included(TitleBlockContentField.ApprovedBy, "Approved by", Field("approvedBy")),
                            new Panel()),
                    },
                }),
                Section("Custom fields", CustomFieldsEditor()),
                Section("Title-block options", new StackLayout
                {
                    Spacing = FoundryTheme.Space1,
                    Items =
                    {
                        _reserveRevisionArea,
                        FoundryTheme.MutedLabel(
                            "Adds an outlined REVISIONS bay without rows or revision data."),
                    },
                }),
            },
        };

        foreach (var field in _fields.Values) field.TextChanged += OnChanged;
        _siteAddress.TextChanged += OnChanged;
        _firmAddress.TextChanged += OnChanged;
        _reserveRevisionArea.CheckedChanged += OnChanged;
        LoadValues(value);
    }

    internal event EventHandler? Changed;

    internal ProjectInformation Value
    {
        get
        {
            var custom = ParseCustomFields(out _);
            var options = new TitleBlockContentOptions(
                _includeChecks.Where(pair => pair.Value.Checked == true).Select(pair => pair.Key).ToArray(),
                _customRows.Select(row => new CustomTitleBlockFieldOption(
                    row.Label.Text.Trim(), row.Include.Checked == true)).ToArray(),
                _reserveRevisionArea.Checked == true).Normalize(custom);
            return new ProjectInformation(
                ProjectName: Text("projectName"), ProjectNumber: Text("projectNumber"), ClientName: Text("clientName"), SiteAddress: _siteAddress.Text.Trim(),
                ProjectPhase: Text("projectPhase"), ProjectStatus: Text("projectStatus"), FirmName: Text("firmName"), FirmAddress: _firmAddress.Text.Trim(),
                FirmPhone: Text("firmPhone"), FirmEmail: Text("firmEmail"), FirmWebsite: Text("firmWebsite"), FirmRegistration: Text("firmRegistration"),
                IssueDate: Text("issueDate"), IssuePurpose: Text("issuePurpose"), DrawnBy: Text("drawnBy"), CheckedBy: Text("checkedBy"), ApprovedBy: Text("approvedBy"),
                CustomFields: custom, Logo: _logo)
            {
                ContentOptions = options
            };
        }
    }

    internal string? ValidationError
    {
        get
        {
            ParseCustomFields(out var error);
            return error;
        }
    }

    private void LoadValues(ProjectInformation value)
    {
        Set("projectName", value.ProjectName);
        Set("projectNumber", value.ProjectNumber);
        Set("clientName", value.ClientName);
        _siteAddress.Text = value.SiteAddress;
        Set("projectPhase", value.ProjectPhase);
        Set("projectStatus", value.ProjectStatus);
        Set("firmName", value.FirmName);
        _firmAddress.Text = value.FirmAddress;
        Set("firmPhone", value.FirmPhone);
        Set("firmEmail", value.FirmEmail);
        Set("firmWebsite", value.FirmWebsite);
        Set("firmRegistration", value.FirmRegistration);
        Set("issueDate", value.IssueDate);
        Set("issuePurpose", value.IssuePurpose);
        Set("drawnBy", value.DrawnBy);
        Set("checkedBy", value.CheckedBy);
        Set("approvedBy", value.ApprovedBy);
        var options = value.ContentOptions;
        foreach (var pair in _includeChecks) pair.Value.Checked = options.Includes(pair.Key);
        _reserveRevisionArea.Checked = options.ReserveRevisionArea;
        _customRows.Clear();
        foreach (var option in options.CustomFields)
            AddCustomRow(option.Label, value.CustomFields.GetValueOrDefault(option.Label) ?? string.Empty,
                option.IsIncluded, rebuild: false);
        RebuildCustomRows();
        _logo = value.Logo;
        UpdateLogoPreview();
    }

    private Control LogoEditor()
    {
        var include = IncludeCheck(TitleBlockContentField.Logo, "Logo");
        var chooseLogo = new FoundryDialogButton("Choose image…", FoundryDialogButtonStyle.Secondary, 116);
        var clearLogo = new FoundryDialogButton("Clear", FoundryDialogButtonStyle.Secondary, 62);
        chooseLogo.Click += (_, _) => ChooseLogo();
        clearLogo.Click += (_, _) =>
        {
            _logo = null;
            UpdateLogoPreview();
            Changed?.Invoke(this, EventArgs.Empty);
        };
        return new StackLayout
        {
            Spacing = FoundryTheme.Space1,
            Items =
            {
                include,
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = FoundryTheme.Space2,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Items = { _logoPreview, chooseLogo, clearLogo, _logoLabel },
                },
            },
        };
    }

    private Control CustomFieldsEditor()
    {
        var add = new FoundryDialogButton("Add field", FoundryDialogButtonStyle.Secondary, 92);
        add.Click += (_, _) => AddCustomRow("", "", true);
        return new StackLayout
        {
            Spacing = FoundryTheme.Space2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                FoundryTheme.MutedLabel("Custom fields render in this order after the standard project fields."),
                _customRowsLayout,
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Items = { add, new StackLayoutItem(null, true) },
                },
            },
        };
    }

    private void AddCustomRow(string label, string value, bool included, bool rebuild = true)
    {
        var row = new CustomFieldRow(label, value, included);
        row.Include.CheckedChanged += OnChanged;
        row.Label.TextChanged += OnChanged;
        row.Value.TextChanged += OnChanged;
        row.Up.Click += (_, _) => MoveCustomRow(row, -1);
        row.Down.Click += (_, _) => MoveCustomRow(row, 1);
        row.Remove.Click += (_, _) =>
        {
            _customRows.Remove(row);
            RebuildCustomRows();
            Changed?.Invoke(this, EventArgs.Empty);
        };
        _customRows.Add(row);
        if (rebuild)
        {
            RebuildCustomRows();
            row.Label.Focus();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void MoveCustomRow(CustomFieldRow row, int offset)
    {
        var index = _customRows.IndexOf(row);
        var target = Math.Clamp(index + offset, 0, _customRows.Count - 1);
        if (target == index) return;
        _customRows.RemoveAt(index);
        _customRows.Insert(target, row);
        RebuildCustomRows();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildCustomRows()
    {
        _customRowsLayout.Items.Clear();
        for (var index = 0; index < _customRows.Count; index++)
        {
            var row = _customRows[index];
            row.Up.Enabled = index > 0;
            row.Down.Enabled = index < _customRows.Count - 1;
            _customRowsLayout.Items.Add(row.Control);
        }
        if (_customRows.Count == 0)
            _customRowsLayout.Items.Add(FoundryTheme.MutedLabel("No custom fields."));
    }

    private TextBox Field(string key)
    {
        var result = Editor();
        _fields.Add(key, result);
        return result;
    }

    private Control Included(TitleBlockContentField field, string label, Control editor) => new StackLayout
    {
        Spacing = FoundryTheme.Space1,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Items = { IncludeCheck(field, label), new FoundryFormField(editor) },
    };

    private FoundryCheckBox IncludeCheck(TitleBlockContentField field, string label)
    {
        var check = new FoundryCheckBox(label);
        check.CheckedChanged += OnChanged;
        _includeChecks.Add(field, check);
        return check;
    }

    private string Text(string key) => _fields[key].Text.Trim();
    private void Set(string key, string value) => _fields[key].Text = value ?? string.Empty;

    private IReadOnlyDictionary<string, string> ParseCustomFields(out string? error)
    {
        error = null;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _customRows)
        {
            var label = row.Label.Text.Trim();
            if (label.Length == 0)
            {
                error = "Custom field labels cannot be empty.";
                return result;
            }
            if (!result.TryAdd(label, row.Value.Text.Trim()))
            {
                error = $"Custom field '{label}' is duplicated.";
                return result;
            }
        }
        return result;
    }

    private void ChooseLogo()
    {
        var dialog = new OpenFileDialog { Title = "Choose firm logo", MultiSelect = false };
        dialog.Filters.Add(new FileFilter("PNG or JPEG image", ".png", ".jpg", ".jpeg"));
        if (dialog.ShowDialog(this) != DialogResult.Ok) return;
        try
        {
            var extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
            var mediaType = extension == ".png" ? "image/png" : "image/jpeg";
            var bytes = File.ReadAllBytes(dialog.FileName);
            if (bytes.Length > MaximumLogoBytes)
                throw new InvalidDataException("The logo exceeds the 5 MB limit.");
            using var image = new Bitmap(bytes);
            if (image.Width <= 0 || image.Height <= 0)
                throw new InvalidDataException("The selected image has invalid dimensions.");
            _logo = new BrandAsset(Path.GetFileName(dialog.FileName), mediaType,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes);
            UpdateLogoPreview();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, exception.Message, "Firm logo", MessageBoxType.Error);
        }
    }

    private void UpdateLogoPreview()
    {
        _logoPreview.Image?.Dispose();
        _logoPreview.Image = _logo is null ? null : new Bitmap(_logo.Data);
        _logoLabel.Text = _logo is null
            ? "No logo selected"
            : $"{_logo.FileName} · {_logo.Data.Length / 1024d:0.#} KB";
    }

    private void OnChanged(object? sender, EventArgs eventArgs) => Changed?.Invoke(this, EventArgs.Empty);

    private static TextBox Editor() => new() { ShowBorder = false, BackgroundColor = Colors.Transparent };

    private static TextArea Area(int height) => new()
    {
        Height = height,
        Wrap = true,
        BackgroundColor = Colors.Transparent,
    };

    private static Control Pair(Control left, Control right) => new TableLayout
    {
        Spacing = new Size(FoundryTheme.Space4, 0),
        Rows = { new TableRow(new TableCell(left, true), new TableCell(right, true)) },
    };

    private static Control Section(string title, Control content) => new StackLayout
    {
        Spacing = FoundryTheme.Space2,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Items =
        {
            new Label
            {
                Text = title,
                Font = SystemFonts.Bold(11),
                TextColor = FoundryTheme.PrimaryText,
            },
            new Panel
            {
                Height = 1,
                BackgroundColor = FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 145),
            },
            content,
        },
    };

    private sealed class CustomFieldRow
    {
        internal CustomFieldRow(string label, string value, bool included)
        {
            Include = new FoundryCheckBox("Include", included);
            Label = Editor();
            Label.Text = label;
            Value = Editor();
            Value.Text = value;
            Up = new FoundryDialogButton("↑", FoundryDialogButtonStyle.Secondary, 36);
            Down = new FoundryDialogButton("↓", FoundryDialogButtonStyle.Secondary, 36);
            Remove = new FoundryDialogButton("Remove", FoundryDialogButtonStyle.Destructive, 64);
            Control = new TableLayout
            {
                Spacing = new Size(FoundryTheme.Space2, 0),
                Rows =
                {
                    new TableRow(
                        Include,
                        new TableCell(new FoundryFormField(Label), true),
                        new TableCell(new FoundryFormField(Value), true),
                        Up,
                        Down,
                        Remove),
                },
            };
        }

        internal FoundryCheckBox Include { get; }
        internal TextBox Label { get; }
        internal TextBox Value { get; }
        internal FoundryDialogButton Up { get; }
        internal FoundryDialogButton Down { get; }
        internal FoundryDialogButton Remove { get; }
        internal Control Control { get; }
    }
}

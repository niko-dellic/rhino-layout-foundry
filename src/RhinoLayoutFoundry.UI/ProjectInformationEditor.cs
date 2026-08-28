using System.Security.Cryptography;
using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.UI;

internal sealed class ProjectInformationEditor : Panel
{
    private const int MaximumLogoBytes = 5 * 1024 * 1024;
    private readonly Dictionary<string, TextBox> _fields = new(StringComparer.Ordinal);
    private readonly TextArea _siteAddress = Area(48);
    private readonly TextArea _firmAddress = Area(48);
    private readonly TextArea _customFields = Area(72, wrap: false);
    private readonly Label _logoLabel = FoundryTheme.MutedLabel();
    private BrandAsset? _logo;

    internal ProjectInformationEditor(ProjectInformation value)
    {
        MinimumSize = new Size(700, 0);
        var chooseLogo = new FoundryDialogButton(
            "Choose image…",
            FoundryDialogButtonStyle.Secondary,
            116);
        var clearLogo = new FoundryDialogButton(
            "Clear",
            FoundryDialogButtonStyle.Secondary,
            62);
        chooseLogo.Click += (_, _) => ChooseLogo();
        clearLogo.Click += (_, _) =>
        {
            _logo = null;
            UpdateLogoLabel();
            Changed?.Invoke(this, EventArgs.Empty);
        };

        Content = new StackLayout
        {
            Padding = new Padding(FoundryTheme.Space1, FoundryTheme.Space2),
            Spacing = FoundryTheme.Space4,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            Items =
            {
                FoundryTheme.MutedLabel(
                    "Project information is stored in this Rhino document and drives every Foundry-managed title block."),
                Section("Project", new StackLayout
                {
                    Spacing = FoundryTheme.Space3,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Top,
                    Items =
                    {
                        Pair(
                            Labeled("Project name", Field("projectName")),
                            Labeled("Project number", Field("projectNumber"))),
                        Pair(
                            Labeled("Client", Field("clientName")),
                            Labeled("Phase", Field("projectPhase"))),
                        Pair(
                            Labeled("Status", Field("projectStatus")),
                            Labeled("Site address", _siteAddress)),
                    },
                }),
                Section("Firm", new StackLayout
                {
                    Spacing = FoundryTheme.Space3,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Top,
                    Items =
                    {
                        Pair(
                            Labeled("Firm name", Field("firmName")),
                            Labeled("Phone", Field("firmPhone"))),
                        Pair(
                            Labeled("Email", Field("firmEmail")),
                            Labeled("Website", Field("firmWebsite"))),
                        Pair(
                            Labeled("Firm address", _firmAddress),
                            Labeled("Registration / license", Field("firmRegistration"))),
                        FieldGroup("Logo", new StackLayout
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = FoundryTheme.Space2,
                            VerticalContentAlignment = VerticalAlignment.Center,
                            Items = { chooseLogo, clearLogo, _logoLabel },
                        }),
                    },
                }),
                Section("Issue defaults", new StackLayout
                {
                    Spacing = FoundryTheme.Space3,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Top,
                    Items =
                    {
                        Pair(
                            Labeled("Issue date", Field("issueDate")),
                            Labeled("Issue purpose", Field("issuePurpose"))),
                        Pair(
                            Labeled("Drawn by", Field("drawnBy")),
                            Labeled("Checked by", Field("checkedBy"))),
                        Pair(
                            Labeled("Approved by", Field("approvedBy")),
                            new Panel()),
                    },
                }),
                Section("Default revision", new StackLayout
                {
                    Spacing = FoundryTheme.Space3,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Top,
                    Items =
                    {
                        Pair(
                            Labeled("Code", Field("revisionCode")),
                            Labeled("Date", Field("revisionDate"))),
                        Labeled("Description", Field("revisionDescription")),
                        Pair(
                            Labeled("Issued by", Field("revisionIssuedBy")),
                            Labeled("Checked by", Field("revisionCheckedBy"))),
                    },
                }),
                Section("Custom fields", new StackLayout
                {
                    Spacing = FoundryTheme.Space2,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Top,
                    Items =
                    {
                        FoundryTheme.MutedLabel("Enter one field per line as Label = Value."),
                        new FoundryFormField(_customFields),
                    },
                }),
            },
        };

        foreach (var field in _fields.Values) field.TextChanged += OnChanged;
        _siteAddress.TextChanged += OnChanged;
        _firmAddress.TextChanged += OnChanged;
        _customFields.TextChanged += OnChanged;
        LoadValues(value);
    }

    internal event EventHandler? Changed;

    internal ProjectInformation Value
    {
        get
        {
            var revision = new SheetRevisionRecord(
                Text("revisionCode"), Text("revisionDate"), Text("revisionDescription"),
                Text("revisionIssuedBy"), Text("revisionCheckedBy"));
            var hasRevision = new[]
            {
                revision.Code, revision.Date, revision.Description, revision.IssuedBy, revision.CheckedBy,
            }.Any(value => !string.IsNullOrWhiteSpace(value));
            return new ProjectInformation(
                Text("projectName"), Text("projectNumber"), Text("clientName"), _siteAddress.Text.Trim(),
                Text("projectPhase"), Text("projectStatus"), Text("firmName"), _firmAddress.Text.Trim(),
                Text("firmPhone"), Text("firmEmail"), Text("firmWebsite"), Text("firmRegistration"),
                Text("issueDate"), Text("issuePurpose"), Text("drawnBy"), Text("checkedBy"), Text("approvedBy"),
                ParseCustomFields(out _), _logo, hasRevision ? revision : null);
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
        Set("revisionCode", value.DefaultRevision?.Code ?? string.Empty);
        Set("revisionDate", value.DefaultRevision?.Date ?? string.Empty);
        Set("revisionDescription", value.DefaultRevision?.Description ?? string.Empty);
        Set("revisionIssuedBy", value.DefaultRevision?.IssuedBy ?? string.Empty);
        Set("revisionCheckedBy", value.DefaultRevision?.CheckedBy ?? string.Empty);
        _customFields.Text = string.Join(Environment.NewLine,
            value.CustomFields.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{pair.Key} = {pair.Value}"));
        _logo = value.Logo;
        UpdateLogoLabel();
    }

    private TextBox Field(string key)
    {
        var result = new TextBox
        {
            ShowBorder = false,
            BackgroundColor = Colors.Transparent,
        };
        _fields.Add(key, result);
        return result;
    }

    private string Text(string key) => _fields[key].Text.Trim();
    private void Set(string key, string value) => _fields[key].Text = value ?? string.Empty;

    private IReadOnlyDictionary<string, string> ParseCustomFields(out string? error)
    {
        error = null;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lineNumber = 0;
        foreach (var rawLine in _customFields.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                error = $"Custom field line {lineNumber} must use Label = Value.";
                return result;
            }
            var key = line[..separator].Trim();
            if (!result.TryAdd(key, line[(separator + 1)..].Trim()))
            {
                error = $"Custom field '{key}' is duplicated.";
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
            using var image = new Eto.Drawing.Bitmap(bytes);
            if (image.Width <= 0 || image.Height <= 0)
                throw new InvalidDataException("The selected image has invalid dimensions.");
            _logo = new BrandAsset(
                Path.GetFileName(dialog.FileName),
                mediaType,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                bytes);
            UpdateLogoLabel();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, exception.Message, "Firm logo", MessageBoxType.Error);
        }
    }

    private void UpdateLogoLabel() => _logoLabel.Text = _logo is null
        ? "No logo selected"
        : $"{_logo.FileName} · {_logo.Data.Length / 1024d:0.#} KB";

    private void OnChanged(object? sender, EventArgs eventArgs) => Changed?.Invoke(this, EventArgs.Empty);

    private static TextArea Area(int height, bool wrap = true) => new()
    {
        Height = height,
        Wrap = wrap,
        BackgroundColor = Colors.Transparent,
    };

    private static Control Labeled(string label, Control editor) =>
        FieldGroup(label, new FoundryFormField(editor));

    private static Control FieldGroup(string label, Control content) => new StackLayout
    {
        Spacing = FoundryTheme.Space1,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Items =
        {
            new Label
            {
                Text = label,
                Font = SystemFonts.Bold(9),
                TextColor = FoundryTheme.MutedText,
                TextAlignment = TextAlignment.Left,
            },
            content,
        },
    };

    private static Control Pair(Control left, Control right) => new TableLayout
    {
        Spacing = new Size(FoundryTheme.Space4, 0),
        Rows =
        {
            new TableRow(
                new TableCell(left, scaleWidth: true),
                new TableCell(right, scaleWidth: true)),
        },
    };

    private static Control Section(string title, Control content) => new StackLayout
    {
        Spacing = FoundryTheme.Space2,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Top,
        Items =
        {
            new Label
            {
                Text = title,
                Font = SystemFonts.Bold(11),
                TextColor = FoundryTheme.PrimaryText,
                TextAlignment = TextAlignment.Left,
            },
            new Panel
            {
                Height = 1,
                BackgroundColor = FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 145),
            },
            content,
        },
    };
}

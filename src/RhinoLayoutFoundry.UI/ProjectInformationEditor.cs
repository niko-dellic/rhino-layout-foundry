using System.Security.Cryptography;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.UI;

internal sealed class ProjectInformationEditor : Panel
{
    private const int MaximumLogoBytes = 5 * 1024 * 1024;
    private readonly Dictionary<string, TextBox> _fields = new(StringComparer.Ordinal);
    private readonly TextArea _siteAddress = new() { Height = 52 };
    private readonly TextArea _firmAddress = new() { Height = 52 };
    private readonly TextArea _customFields = new() { Height = 92, Wrap = false };
    private readonly Label _logoLabel = FoundryTheme.MutedLabel();
    private BrandAsset? _logo;

    internal ProjectInformationEditor(ProjectInformation value)
    {
        var chooseLogo = FoundryTheme.ConfigureButton(new Button { Text = "Choose PNG/JPEG…" }, 132);
        var clearLogo = FoundryTheme.ConfigureButton(new Button { Text = "Clear" }, 58);
        chooseLogo.Click += (_, _) => ChooseLogo();
        clearLogo.Click += (_, _) =>
        {
            _logo = null;
            UpdateLogoLabel();
            Changed?.Invoke(this, EventArgs.Empty);
        };

        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space3,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                FoundryTheme.MutedLabel(
                    "Project information is stored in this Rhino document and drives every Foundry-managed title block."),
                Section("Project", Form(
                    Row("Project name", Field("projectName")),
                    Row("Project number", Field("projectNumber")),
                    Row("Client", Field("clientName")),
                    Row("Site address", _siteAddress),
                    Row("Phase", Field("projectPhase")),
                    Row("Status", Field("projectStatus")))),
                Section("Firm", Form(
                    Row("Firm name", Field("firmName")),
                    Row("Firm address", _firmAddress),
                    Row("Phone", Field("firmPhone")),
                    Row("Email", Field("firmEmail")),
                    Row("Website", Field("firmWebsite")),
                    Row("Registration / license", Field("firmRegistration")),
                    new TableRow(new Label { Text = "Logo" }, new StackLayout
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = FoundryTheme.Space2,
                        Items = { chooseLogo, clearLogo, _logoLabel },
                    }))),
                Section("Issue defaults", Form(
                    Row("Issue date", Field("issueDate")),
                    Row("Issue purpose", Field("issuePurpose")),
                    Row("Drawn by", Field("drawnBy")),
                    Row("Checked by", Field("checkedBy")),
                    Row("Approved by", Field("approvedBy")))),
                Section("Default revision", Form(
                    Row("Code", Field("revisionCode")),
                    Row("Date", Field("revisionDate")),
                    Row("Description", Field("revisionDescription")),
                    Row("Issued by", Field("revisionIssuedBy")),
                    Row("Checked by", Field("revisionCheckedBy")))),
                Section("Custom fields", new StackLayout
                {
                    Spacing = FoundryTheme.Space1,
                    Items =
                    {
                        FoundryTheme.MutedLabel("Enter one field per line as Label = Value."),
                        _customFields,
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
        var result = new TextBox();
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

    private static TableRow Row(string label, Control editor) =>
        new(new Label { Text = label }, new TableCell(editor, true));

    private static TableLayout Form(params TableRow[] rows)
    {
        var layout = new TableLayout
        {
            Spacing = new Eto.Drawing.Size(FoundryTheme.Space3, FoundryTheme.Space1),
        };
        foreach (var row in rows) layout.Rows.Add(row);
        return layout;
    }

    private static Control Section(string title, Control content) => new GroupBox
    {
        Text = title,
        Padding = new Eto.Drawing.Padding(FoundryTheme.Space3),
        Content = content,
    };
}

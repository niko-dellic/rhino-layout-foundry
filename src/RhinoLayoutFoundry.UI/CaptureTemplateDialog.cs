using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.UI;

internal sealed class CaptureTemplateDialog : Dialog
{
    private readonly Guid _sourcePageViewId;
    private readonly IReadOnlyList<TitleBlockCandidate> _blocks;
    private readonly TextBox _nameBox;
    private readonly TextBox _patternBox;
    private readonly DropDown _titleBlockDropDown;
    private readonly Label _statusLabel;
    private readonly Button _captureButton;

    internal CaptureTemplateDialog(
        Guid sourcePageViewId,
        string sourceName,
        TemplateCaptureContext context)
    {
        _sourcePageViewId = sourcePageViewId;
        _blocks = context.TitleBlockCandidates;
        Title = "Capture layout template";
        MinimumSize = new Size(460, 300);
        Resizable = false;
        Padding = new Padding(FoundryTheme.Space4);
        BackgroundColor = FoundryTheme.PanelBackground;

        _nameBox = new TextBox { Text = $"{sourceName} template" };
        _patternBox = new TextBox { Text = "{folder}-{index:00}" };
        _titleBlockDropDown = new DropDown
        {
            DataStore = new[] { "No title block" }.Concat(_blocks.Select(block =>
                $"{block.InstanceDefinitionName}  ·  {block.InstanceObjectId.ToString()[..8]}")).ToArray(),
            SelectedIndex = _blocks.Count == 1 ? 1 : 0,
        };
        _statusLabel = FoundryTheme.MutedLabel(
            "Paper size, detail rectangles, cameras, scales, display modes, and metadata will be captured.");
        _statusLabel.Wrap = WrapMode.Word;
        _captureButton = FoundryTheme.ConfigureButton(new Button { Text = "Capture template" }, 120);
        var cancel = FoundryTheme.ConfigureButton(new Button { Text = "Cancel" });
        cancel.Click += (_, _) => Close();
        _captureButton.Click += async (_, _) => await CaptureAsync();
        _nameBox.TextChanged += (_, _) => UpdateEnabled();
        _patternBox.TextChanged += (_, _) => UpdateEnabled();
        DefaultButton = _captureButton;
        AbortButton = cancel;

        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space3,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                Header("Capture template", $"Use '{sourceName}' as a reusable sheet recipe."),
                Field("Template name", _nameBox),
                Field("Default naming pattern", _patternBox),
                Field("Title block on this layout", _titleBlockDropDown),
                _statusLabel,
                new TableLayout
                {
                    Rows = { new TableRow(new TableCell(null, true), cancel, _captureButton) },
                    Spacing = new Size(FoundryTheme.Space2, 0),
                },
            },
        };
        UpdateEnabled();
    }

    internal bool Captured { get; private set; }

    private async Task CaptureAsync()
    {
        _captureButton.Enabled = false;
        _statusLabel.Text = "Capturing layout recipe…";
        var blockId = _titleBlockDropDown.SelectedIndex > 0
            ? _blocks[_titleBlockDropDown.SelectedIndex - 1].InstanceObjectId
            : (Guid?)null;
        var result = await LayoutFoundryUiHost.CaptureSheetTemplateAsync(
            _sourcePageViewId, _nameBox.Text, _patternBox.Text, blockId);
        if (!result.Succeeded)
        {
            _statusLabel.Text = string.Join(" ", result.Diagnostics.Select(item => item.Message));
            UpdateEnabled();
            return;
        }
        Captured = true;
        Close();
    }

    private void UpdateEnabled() => _captureButton.Enabled =
        !string.IsNullOrWhiteSpace(_nameBox.Text) && !string.IsNullOrWhiteSpace(_patternBox.Text);

    private static Control Field(string label, Control control) => new StackLayout
    {
        Spacing = FoundryTheme.Space1,
        Items = { new Label { Text = label, Font = SystemFonts.Bold() }, control },
    };

    private static Control Header(string title, string subtitle) => new StackLayout
    {
        Spacing = FoundryTheme.Space1,
        Items =
        {
            new Label { Text = title, Font = SystemFonts.Bold(16), TextColor = FoundryTheme.PrimaryText },
            FoundryTheme.MutedLabel(subtitle),
        },
    };
}

using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.UI;

internal sealed class BatchPropertiesDialog : Dialog
{
    private readonly BatchPropertiesSession _session;
    private readonly bool _mutationCapabilityAvailable;
    private readonly string _capabilityReason;
    private readonly Label _validationLabel;
    private readonly Label _reviewLabel;
    private readonly Button _applyButton;

    internal BatchPropertiesDialog(
        uint documentRuntimeSerialNumber,
        long sourceRevision,
        IReadOnlyList<BatchTarget> targets,
        FoundryMutationCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(targets);
        _session = new BatchPropertiesSession(
            documentRuntimeSerialNumber,
            sourceRevision,
            targets);
        _mutationCapabilityAvailable = capabilities.AtomicBatchUndo.IsSupported;
        _capabilityReason = capabilities.AtomicBatchUndo.Reason;

        Title = "Batch properties";
        MinimumSize = new Size(520, 500);
        Resizable = true;
        Padding = new Padding(FoundryTheme.Space4);

        _validationLabel = FoundryTheme.MutedLabel();
        _reviewLabel = FoundryTheme.MutedLabel();
        _reviewLabel.Wrap = WrapMode.Word;
        _applyButton = FoundryTheme.ConfigureButton(new Button
        {
            Text = "Apply changes",
            Enabled = false,
            ToolTip = "Apply unlocks after Rhino page-property Undo is verified",
        }, minimumWidth: 110);
        var cancelButton = FoundryTheme.ConfigureButton(new Button { Text = "Close" });
        cancelButton.Click += (_, _) => Close();

        var tabs = new TabControl
        {
            Pages =
            {
                new TabPage { Text = "Targets", Content = CreateTargetsPage(targets) },
                new TabPage { Text = "Properties", Content = CreatePropertiesPage() },
                new TabPage { Text = "Review", Content = CreateReviewPage() },
            },
        };
        tabs.SelectedIndexChanged += (_, _) => UpdateValidation();

        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space3,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                CreateHeader(targets.Count),
                new StackLayoutItem(tabs, expand: true),
                _validationLabel,
                CreateFooter(cancelButton),
            },
        };
        UpdateValidation();
    }

    private static Control CreateHeader(int targetCount)
    {
        return new StackLayout
        {
            Spacing = FoundryTheme.Space1,
            Items =
            {
                new Label
                {
                    Text = "Batch properties",
                    Font = SystemFonts.Bold(16),
                    TextColor = FoundryTheme.PrimaryText,
                },
                FoundryTheme.MutedLabel(
                    $"Stage one atomic change across {targetCount} selected item{(targetCount == 1 ? string.Empty : "s")}.")
            },
        };
    }

    private Control CreateTargetsPage(IEnumerable<BatchTarget> targets)
    {
        var rows = new StackLayout
        {
            Padding = new Padding(FoundryTheme.Space2),
            Spacing = FoundryTheme.Space1,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        foreach (var target in targets)
        {
            var checkBox = new CheckBox
            {
                Text = target.Label,
                Checked = target.Included,
                ToolTip = "Include or exclude this item from the pending batch; nothing is deleted.",
            };
            checkBox.CheckedChanged += (_, _) =>
            {
                _session.SetIncluded(target.Key, checkBox.Checked == true);
                UpdateValidation();
            };
            rows.Items.Add(checkBox);
        }

        return new Scrollable { Content = rows };
    }

    private Control CreatePropertiesPage()
    {
        var namePattern = CreateField(
            "Name pattern",
            "Example: {folder}-{index:00}",
            BatchPropertyKind.NamePattern);
        var paperSize = CreateField(
            "Paper size",
            "Mixed / unchanged",
            BatchPropertyKind.PaperSize);
        var tags = CreateField(
            "Tags",
            "Comma-separated tags",
            BatchPropertyKind.Tags);
        var displayMode = CreateField(
            "Detail display mode",
            "Mixed / unchanged",
            BatchPropertyKind.DetailDisplayMode);

        return new Scrollable
        {
            Content = new StackLayout
            {
                Padding = new Padding(FoundryTheme.Space3),
                Spacing = FoundryTheme.Space3,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Items =
                {
                    namePattern,
                    paperSize,
                    tags,
                    displayMode,
                },
            },
        };
    }

    private Control CreateField(
        string label,
        string placeholder,
        BatchPropertyKind property)
    {
        var textBox = new TextBox { PlaceholderText = placeholder };
        textBox.TextChanged += (_, _) =>
        {
            _session.Stage(property, textBox.Text);
            UpdateValidation();
        };
        return new StackLayout
        {
            Spacing = FoundryTheme.Space1,
            Items =
            {
                new Label { Text = label, Font = SystemFonts.Bold() },
                textBox,
            },
        };
    }

    private Control CreateReviewPage()
    {
        return FoundryTheme.Surface(
            new StackLayout
            {
                Padding = new Padding(FoundryTheme.Space3),
                Spacing = FoundryTheme.Space2,
                Items =
                {
                    new Label { Text = "Pending operation", Font = SystemFonts.Bold(13) },
                    _reviewLabel,
                },
            });
    }

    private Control CreateFooter(Button cancelButton)
    {
        return new TableLayout
        {
            Spacing = new Size(FoundryTheme.Space2, 0),
            Rows =
            {
                new TableRow(
                    new TableCell(null, scaleWidth: true),
                    cancelButton,
                    _applyButton),
            },
        };
    }

    private void UpdateValidation()
    {
        var validation = _session.Validate(_mutationCapabilityAvailable);
        _applyButton.Enabled = validation.CanApply;
        _validationLabel.Text = string.Join(
            " ",
            validation.Errors
                .Concat(validation.Warnings)
                .Concat(_mutationCapabilityAvailable ? [] : [_capabilityReason]));
        var included = _session.Targets.Count(target => target.Included);
        var properties = _session.StagedValues.Count == 0
            ? "No property changes staged."
            : string.Join(
                Environment.NewLine,
                _session.StagedValues.Select(pair => $"{pair.Key}: {pair.Value}"));
        _reviewLabel.Text =
            $"{included} of {_session.Targets.Count} targets included.{Environment.NewLine}{Environment.NewLine}{properties}";
    }
}

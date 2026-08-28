using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Persistence;

namespace RhinoLayoutFoundry.UI;

internal sealed class LayoutPackageImportDialog : Dialog
{
    private readonly DropDown _mode;
    private readonly CheckBox _importProjectInformation;
    private readonly Label _projectInformationHelp;
    private readonly Dictionary<string, DropDown> _resolutionControls = new(StringComparer.Ordinal);

    internal LayoutPackageImportDialog(LayoutPackagePreflight preflight)
    {
        var manifest = preflight.Manifest ?? throw new ArgumentException("Preflight has no manifest.", nameof(preflight));
        Title = "Review Layout Package Import";
        MinimumSize = new Size(680, 520);
        Resizable = true;
        Padding = new Padding(FoundryTheme.Space4);
        BackgroundColor = FoundryTheme.PanelBackground;

        _mode = new DropDown
        {
            DataStore = new[] { "Merge into this document", "Replace layouts and Foundry structure" },
            SelectedIndex = 0,
            Width = 280,
        };
        _importProjectInformation = new CheckBox
        {
            Text = "Import project information",
            Checked = false,
            ToolTip = "Include project, firm, issue-default, revision, custom-field, and logo data from this package.",
        };
        _projectInformationHelp = FoundryTheme.MutedLabel();
        _projectInformationHelp.Wrap = WrapMode.Word;
        void UpdateProjectInformationHelp()
        {
            _projectInformationHelp.Text = _mode.SelectedIndex == 1
                ? "When enabled, package project information replaces the current document values."
                : "When enabled, package values fill blank fields; existing document values are kept.";
        }
        _mode.SelectedIndexChanged += (_, _) => UpdateProjectInformationHelp();
        UpdateProjectInformationHelp();
        var conflictRows = new StackLayout
        {
            Spacing = FoundryTheme.Space2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        foreach (var conflict in preflight.Conflicts)
        {
            var choices = conflict.CanOverwrite
                ? new[] { "Import renamed copy", "Reuse destination", "Overwrite destination" }
                : new[] { "Import renamed copy", "Reuse destination" };
            var choice = new DropDown { DataStore = choices, SelectedIndex = 0, Width = 190 };
            _resolutionControls[conflict.Key] = choice;
            conflictRows.Items.Add(new TableLayout
            {
                Rows =
                {
                    new TableRow(
                        new TableCell(new StackLayout
                        {
                            Spacing = FoundryTheme.Space1,
                            Items =
                            {
                                new Label { Text = conflict.Name, TextColor = FoundryTheme.PrimaryText },
                                FoundryTheme.MutedLabel(conflict.Message),
                            },
                        }, scaleWidth: true),
                        choice),
                },
            });
        }

        var apply = FoundryTheme.ConfigureButton(new Button { Text = "Import" }, 88);
        var cancel = FoundryTheme.ConfigureButton(new Button { Text = "Cancel" });
        apply.Click += (_, _) =>
        {
            if (ImportMode == LayoutPackageImportMode.Replace)
            {
                var response = MessageBox.Show(
                    this,
                    "Replace removes the current layouts and Foundry structure. Rhino cannot reliably undo layout creation or deletion. A recovery package will be created first.",
                    "Confirm Replace Import",
                    MessageBoxButtons.YesNo,
                    MessageBoxType.Warning,
                    MessageBoxDefaultButton.No);
                if (response != DialogResult.Yes) return;
            }
            Accepted = true;
            Close();
        };
        cancel.Click += (_, _) => Close();
        DefaultButton = apply;
        AbortButton = cancel;

        var warnings = preflight.Warnings.Count == 0
            ? "No preflight warnings."
            : string.Join(Environment.NewLine, preflight.Warnings.Select(item => $"• {item}"));
        var sourceProject = manifest.FoundryState.ProjectInfo;
        var projectSummary = new[] { sourceProject.ProjectName, sourceProject.FirmName }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space3,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new Label { Text = "Import layout package", Font = SystemFonts.Bold(17), TextColor = FoundryTheme.PrimaryText },
                FoundryTheme.MutedLabel($"{manifest.SourceDocumentName} · {manifest.Sheets.Count} layouts · {manifest.FoundryState.Folders.Count - 1} folders"),
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = FoundryTheme.Space2,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Items = { new Label { Text = "Import mode" }, _mode },
                },
                FoundryTheme.Surface(new StackLayout
                {
                    Spacing = FoundryTheme.Space1,
                    Items =
                    {
                        _importProjectInformation,
                        FoundryTheme.MutedLabel(projectSummary.Length == 0
                            ? "The package does not name a project or firm."
                            : $"Package data: {string.Join(" · ", projectSummary)}"),
                        _projectInformationHelp,
                    },
                }, new Padding(FoundryTheme.Space3)),
                FoundryTheme.Surface(new Scrollable
                {
                    Border = BorderType.None,
                    Content = conflictRows,
                }, new Padding(FoundryTheme.Space3)),
                FoundryTheme.MutedLabel(warnings),
                new StackLayoutItem(null, expand: true),
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = FoundryTheme.Space2,
                    HorizontalContentAlignment = HorizontalAlignment.Right,
                    Items = { cancel, apply },
                },
            },
        };
    }

    internal bool Accepted { get; private set; }

    internal LayoutPackageImportMode ImportMode =>
        _mode.SelectedIndex == 1 ? LayoutPackageImportMode.Replace : LayoutPackageImportMode.Merge;

    internal bool ImportProjectInformation => _importProjectInformation.Checked == true;

    internal IReadOnlyDictionary<string, LayoutPackageConflictResolution> ConflictResolutions =>
        _resolutionControls.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.SelectedIndex switch
            {
                1 => LayoutPackageConflictResolution.ReuseDestination,
                2 => LayoutPackageConflictResolution.OverwriteDestination,
                _ => LayoutPackageConflictResolution.ImportRenamedCopy,
            },
            StringComparer.Ordinal);
}

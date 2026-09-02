using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

internal sealed class NamingPatternHelpDialog : Dialog
{
    internal NamingPatternHelpDialog()
    {
        Title = "Naming-pattern wildcards";
        MinimumSize = new Size(560, 485);
        Padding = new Padding(FoundryTheme.Space4);
        BackgroundColor = FoundryTheme.PanelBackground;

        var close = new FoundryDialogButton("Close", FoundryDialogButtonStyle.Secondary);
        close.Click += (_, _) => Close();
        FoundryDialogActions.Bind(this, close, close);

        var tokens = new TableLayout
        {
            Spacing = new Size(FoundryTheme.Space3, FoundryTheme.Space2),
            Rows =
            {
                TokenRow("{index}", "Number selected by Indexing; occupied layout names are skipped automatically."),
                TokenRow("{index:000}", "Formatted batch number; this example produces 001, 002, 003…"),
                TokenRow("{project}", "Project value from the document metadata."),
                TokenRow("{discipline}", "Discipline value from the document metadata."),
                TokenRow("{folder}", "Destination folder name."),
                TokenRow("{tag}", "First tag from the selected layout template."),
                TokenRow("{view}", "First assigned named view, or the template's default view."),
            },
        };

        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space3,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new Label
                {
                    Text = "Naming-pattern wildcards",
                    Font = SystemFonts.Bold(17),
                    TextColor = FoundryTheme.PrimaryText,
                },
                FoundryTheme.MutedLabel(
                    "Combine ordinary text with these tokens. Only {index} accepts a numeric format."),
                FoundryTheme.Surface(new StackLayout
                {
                    Padding = new Padding(FoundryTheme.Space3),
                    Items = { tokens },
                }),
                FoundryTheme.MutedLabel(
                    "Example: A-{discipline}-{index:000}  →  A-MECH-013"),
                FoundryTheme.MutedLabel(
                    "Created and batch-renamed layouts stay linked to non-index values. " +
                    "Their assigned index is frozen, so moving or reordering a layout never renumbers it. " +
                    "Renaming a layout manually detaches that link; applying a naming pattern again reattaches it."),
                new StackLayoutItem(null, true),
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Items = { new StackLayoutItem(null, true), close },
                },
            },
        };
    }

    private static TableRow TokenRow(string token, string meaning) => new(
        new Label
        {
            Text = token,
            Font = SystemFonts.Bold(10),
            TextColor = FoundryTheme.PrimaryText,
        },
        new Label
        {
            Text = meaning,
            TextColor = FoundryTheme.MutedText,
            Wrap = WrapMode.Word,
        });
}

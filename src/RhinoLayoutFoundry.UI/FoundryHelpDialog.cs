using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

/// <summary>Small Foundry-owned modal for contextual field and section help.</summary>
internal sealed class FoundryHelpDialog : Dialog
{
    internal FoundryHelpDialog(string title, params string[] paragraphs)
    {
        Title = title;
        MinimumSize = new Size(460, 230);
        Resizable = false;
        Padding = new Padding(FoundryTheme.Space4);
        BackgroundColor = FoundryTheme.PanelBackground;

        var close = new FoundryDialogButton("Close", FoundryDialogButtonStyle.Secondary);
        close.Click += (_, _) => Close();
        FoundryDialogActions.Bind(this, close, close);

        var explanation = new StackLayout
        {
            Spacing = FoundryTheme.Space2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        foreach (var paragraph in paragraphs.Where(text => !string.IsNullOrWhiteSpace(text)))
        {
            var label = FoundryTheme.MutedLabel(paragraph);
            label.Wrap = WrapMode.Word;
            explanation.Items.Add(label);
        }

        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space3,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new Label
                {
                    Text = title,
                    Font = SystemFonts.Bold(17),
                    TextColor = FoundryTheme.PrimaryText,
                },
                explanation,
                new StackLayoutItem(null, true),
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Items = { new StackLayoutItem(null, true), close },
                },
            },
        };
    }
}

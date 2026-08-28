using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

/// <summary>
/// Minimal form-field chrome: native text editing with a quiet baseline and a
/// Foundry accent when focused.
/// </summary>
internal sealed class FoundryFormField : Panel
{
    private readonly Control _interactionControl;
    private readonly Panel _underline;
    private bool _hovered;
    private bool _focused;

    internal FoundryFormField(Control input, Control? interactionControl = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        _interactionControl = interactionControl ?? input;
        BackgroundColor = Colors.Transparent;
        _underline = new Panel
        {
            Height = 1,
            BackgroundColor = FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 165),
        };
        Content = new StackLayout
        {
            Spacing = 0,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new StackLayoutItem(input, expand: true),
                _underline,
            },
        };

        input.MouseEnter += (_, _) =>
        {
            _hovered = true;
            UpdateUnderline();
        };
        input.MouseLeave += (_, _) =>
        {
            _hovered = false;
            UpdateUnderline();
        };
        _interactionControl.GotFocus += (_, _) =>
        {
            _focused = true;
            UpdateUnderline();
        };
        _interactionControl.LostFocus += (_, _) =>
        {
            _focused = false;
            UpdateUnderline();
        };
        _interactionControl.EnabledChanged += (_, _) => UpdateUnderline();
    }

    private void UpdateUnderline()
    {
        _underline.Height = _focused ? 2 : 1;
        _underline.BackgroundColor = !_interactionControl.Enabled
            ? FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 70)
            : _focused
                ? FoundryTheme.SelectionAccent
                : _hovered
                    ? FoundryTheme.PrimaryText
                    : FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 165);
    }
}

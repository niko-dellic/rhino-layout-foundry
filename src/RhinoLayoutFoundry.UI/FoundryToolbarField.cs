using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

/// <summary>
/// Gives compact toolbar inputs the same transparent resting state and accent
/// feedback as Foundry's icon buttons without replacing native text editing or
/// menu behavior.
/// </summary>
internal sealed class FoundryToolbarField : Panel
{
    private readonly Control _input;
    private readonly Control _interactionControl;
    private readonly Panel _underline;
    private bool _hovered;
    private bool _focused;

    internal FoundryToolbarField(
        Control input,
        int width,
        Control? interactionControl = null)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _interactionControl = interactionControl ?? input;
        Width = width;
        Height = 28;
        BackgroundColor = Colors.Transparent;

        _underline = new Panel
        {
            Height = 2,
            BackgroundColor = Colors.Transparent,
        };
        Content = new StackLayout
        {
            Spacing = 0,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new StackLayoutItem(_input, expand: true),
                _underline,
            },
        };

        _input.MouseEnter += (_, _) =>
        {
            _hovered = true;
            UpdateUnderline();
        };
        _input.MouseLeave += (_, _) =>
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
        _underline.BackgroundColor = !_interactionControl.Enabled
            ? Colors.Transparent
            : _focused
                ? FoundryTheme.SelectionAccent
                : _hovered
                    ? FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 150)
                    : Colors.Transparent;
    }
}

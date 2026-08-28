using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

/// <summary>
/// Segmented capsule for mutually exclusive toolbar modes.
/// </summary>
internal sealed class FoundryToolbarButtonGroup : PixelLayout
{
    private const int PaddingSize = 4;
    private const int ButtonSpacing = 2;
    private readonly FoundryToolbarIconButton[] _buttons;
    private readonly CapsuleChrome _chrome = new();

    internal FoundryToolbarButtonGroup(params FoundryToolbarIconButton[] buttons)
    {
        _buttons = buttons ?? throw new ArgumentNullException(nameof(buttons));
        if (_buttons.Length == 0) throw new ArgumentException("A button group needs at least one button.", nameof(buttons));
        BackgroundColor = Colors.Transparent;
        Size = new Size(
            (PaddingSize * 2) + (_buttons.Length * 32) + ((_buttons.Length - 1) * ButtonSpacing),
            40);
        Add(_chrome, 0, 0);
        foreach (var button in _buttons)
        {
            button.IsGrouped = true;
            Add(button, 0, PaddingSize);
        }
        SizeChanged += (_, _) => LayoutChildren();
        for (var index = 0; index < _buttons.Length; index++)
        {
            var buttonIndex = index;
            _buttons[index].KeyDown += (_, eventArgs) =>
            {
                var target = eventArgs.Key switch
                {
                    Keys.Left => (buttonIndex - 1 + _buttons.Length) % _buttons.Length,
                    Keys.Right => (buttonIndex + 1) % _buttons.Length,
                    _ => -1,
                };
                if (target < 0) return;
                _buttons[target].Focus();
                eventArgs.Handled = true;
            };
        }
        LayoutChildren();
    }

    private void LayoutChildren()
    {
        _chrome.Size = ClientSize;
        Move(_chrome, 0, 0);
        for (var index = 0; index < _buttons.Length; index++)
            Move(_buttons[index], PaddingSize + (index * (32 + ButtonSpacing)), PaddingSize);
    }

    private sealed class CapsuleChrome : Drawable
    {
        internal CapsuleChrome() : base(false)
        {
            BackgroundColor = Colors.Transparent;
            Paint += (_, eventArgs) =>
            {
                var bounds = new RectangleF(0.5f, 0.5f, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
                using var outline = GraphicsPath.GetRoundRect(bounds, 10);
                eventArgs.Graphics.FillPath(FoundryTheme.ToolbarGroupBackground, outline);
                eventArgs.Graphics.DrawPath(
                    new Pen(FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 75), 1),
                    outline);
            };
        }
    }
}

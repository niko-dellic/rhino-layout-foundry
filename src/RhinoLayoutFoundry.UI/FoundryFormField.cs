using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

/// <summary>
/// Shared shadcn-style field shell. Native editors retain their editing and
/// accessibility behavior while the outer surface supplies Foundry chrome.
/// </summary>
internal sealed class FoundryFormField : PixelLayout
{
    private const int SingleLineHeight = 32;
    private readonly Control _input;
    private readonly Control _interactionControl;
    private readonly FieldChrome _chrome;
    private readonly int _horizontalInset;
    private readonly int _verticalInset;

    internal FoundryFormField(
        Control input,
        Control? interactionControl = null,
        int minimumHeight = SingleLineHeight,
        int? horizontalInset = null,
        float cornerRadius = 6)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _interactionControl = interactionControl ?? input;
        _horizontalInset = horizontalInset ?? (input is TextBox or TextArea ? 7 : 3);
        _verticalInset = input is TextArea ? 4 : 1;
        _chrome = new FieldChrome(cornerRadius);
        BackgroundColor = Colors.Transparent;

        PrepareNativeInput(input);
        var requestedHeight = input.Height > 0 ? input.Height + (_verticalInset * 2) : SingleLineHeight;
        Height = Math.Max(minimumHeight, requestedHeight);
        if (input.Width > 0) Width = input.Width;
        Add(_chrome, 0, 0);
        Add(_input, _horizontalInset, _verticalInset);
        SizeChanged += (_, _) => LayoutChildren();

        MouseEnter += (_, _) => SetHovered(true);
        MouseLeave += (_, _) => SetHovered(false);
        input.MouseEnter += (_, _) => SetHovered(true);
        input.MouseLeave += (_, _) => SetHovered(false);
        _interactionControl.GotFocus += (_, _) =>
        {
            _chrome.Focused = true;
            _chrome.Invalidate();
        };
        _interactionControl.LostFocus += (_, _) =>
        {
            _chrome.Focused = false;
            _chrome.Invalidate();
        };
        _interactionControl.EnabledChanged += (_, _) =>
        {
            _chrome.InputEnabled = _interactionControl.Enabled;
            _chrome.Invalidate();
        };
        LayoutChildren();
    }

    private void SetHovered(bool hovered)
    {
        _chrome.Hovered = hovered;
        _chrome.Invalidate();
    }

    private void LayoutChildren()
    {
        var size = ClientSize;
        if (size.Width <= 0 || size.Height <= 0) return;
        _chrome.Size = size;
        Move(_chrome, 0, 0);
        _input.Size = new Size(
            Math.Max(0, size.Width - (_horizontalInset * 2)),
            Math.Max(0, size.Height - (_verticalInset * 2)));
        Move(_input, _horizontalInset, _verticalInset);
    }

    private static void PrepareNativeInput(Control input)
    {
        input.BackgroundColor = Colors.Transparent;
        switch (input)
        {
            case TextBox textBox:
                textBox.ShowBorder = false;
                textBox.TextColor = FoundryTheme.PrimaryText;
                break;
            case DropDown dropDown:
                dropDown.ShowBorder = false;
                dropDown.TextColor = FoundryTheme.PrimaryText;
                break;
            case TextArea textArea:
                textArea.TextColor = FoundryTheme.PrimaryText;
                break;
            case NumericStepper numericStepper:
                numericStepper.TextColor = FoundryTheme.PrimaryText;
                break;
        }
    }

    private sealed class FieldChrome : Drawable
    {
        private readonly float _cornerRadius;

        internal FieldChrome(float cornerRadius) : base(false)
        {
            _cornerRadius = cornerRadius;
            BackgroundColor = Colors.Transparent;
            Paint += OnPaint;
        }

        internal bool Hovered { get; set; }
        internal bool Focused { get; set; }
        internal bool InputEnabled { get; set; } = true;

        private void OnPaint(object? sender, PaintEventArgs eventArgs)
        {
            var bounds = new RectangleF(0.5f, 0.5f, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            using var outline = GraphicsPath.GetRoundRect(bounds, _cornerRadius);
            eventArgs.Graphics.FillPath(
                InputEnabled
                    ? FoundryTheme.InputBackground
                    : FoundryTheme.WithAlpha(FoundryTheme.CanvasSubtleSurface, 85),
                outline);
            var border = !InputEnabled
                ? FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 75)
                : Focused
                    ? FoundryTheme.WithAlpha(FoundryTheme.PrimaryText, 185)
                    : Hovered
                        ? FoundryTheme.WithAlpha(FoundryTheme.SecondaryText, 190)
                        : FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 190);
            eventArgs.Graphics.DrawPath(new Pen(border, Focused ? 1.5f : 1), outline);

            if (!Focused || !InputEnabled) return;
            using var focus = GraphicsPath.GetRoundRect(
                new RectangleF(2.5f, 2.5f, Math.Max(0, Width - 5), Math.Max(0, Height - 5)),
                Math.Max(2, _cornerRadius - 2));
            eventArgs.Graphics.DrawPath(
                new Pen(FoundryTheme.WithAlpha(FoundryTheme.PrimaryText, 70), 1),
                focus);
        }
    }
}

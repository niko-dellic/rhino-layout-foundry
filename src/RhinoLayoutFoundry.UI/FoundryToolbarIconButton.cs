using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

/// <summary>
/// Shared, borderless icon button used by Foundry toolbars. Toggle buttons use
/// the Rhino selection accent as a compact active-state underline.
/// </summary>
internal sealed class FoundryToolbarIconButton : Drawable
{
    private readonly bool _isToggle;
    private Image _image;
    private bool _checked;
    private bool _hovered;
    private bool _pressed;
    private bool _showFocusRing;

    internal FoundryToolbarIconButton(
        Image image,
        string toolTip,
        bool isToggle = false)
        : base(true)
    {
        _image = image ?? throw new ArgumentNullException(nameof(image));
        _isToggle = isToggle;
        Size = new Size(28, 28);
        BackgroundColor = Colors.Transparent;
        CanFocus = true;
        ToolTip = toolTip;

        Paint += OnPaint;
        MouseEnter += (_, _) =>
        {
            if (!Enabled) return;
            _hovered = true;
            Invalidate();
        };
        MouseLeave += (_, _) =>
        {
            _hovered = false;
            _pressed = false;
            Invalidate();
        };
        MouseDown += (_, eventArgs) =>
        {
            if (!Enabled || !eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
            _pressed = true;
            _showFocusRing = false;
            Focus();
            eventArgs.Handled = true;
            Invalidate();
        };
        MouseUp += (_, eventArgs) =>
        {
            if (!Enabled || !_pressed) return;
            _pressed = false;
            Activate();
            eventArgs.Handled = true;
        };
        KeyDown += (_, eventArgs) =>
        {
            if (!Enabled || eventArgs.Key is not (Keys.Enter or Keys.Space)) return;
            Activate();
            eventArgs.Handled = true;
        };
        GotFocus += (_, _) =>
        {
            if (!_pressed) _showFocusRing = true;
            Invalidate();
        };
        LostFocus += (_, _) =>
        {
            _pressed = false;
            _showFocusRing = false;
            Invalidate();
        };
        EnabledChanged += (_, _) =>
        {
            if (!Enabled)
            {
                _hovered = false;
                _pressed = false;
                _showFocusRing = false;
            }

            Invalidate();
        };
    }

    internal event EventHandler? Click;

    internal Image Image
    {
        get => _image;
        set
        {
            _image = value ?? throw new ArgumentNullException(nameof(value));
            Invalidate();
        }
    }

    internal bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
        }
    }

    private void Activate()
    {
        if (_isToggle) Checked = !Checked;
        Click?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void OnPaint(object? sender, PaintEventArgs eventArgs)
    {
        if (Enabled && (_hovered || _pressed))
        {
            eventArgs.Graphics.FillRectangle(
                FoundryTheme.WithAlpha(FoundryTheme.CanvasSubtleSurface, _pressed ? 150 : 90),
                1,
                1,
                Math.Max(0, Width - 2),
                Math.Max(0, Height - 2));
        }

        var imageSize = _image.Size;
        var imageX = (Width - imageSize.Width) / 2f;
        var imageY = (Height - imageSize.Height) / 2f;
        eventArgs.Graphics.DrawImage(_image, imageX, imageY);

        if (!Enabled)
        {
            eventArgs.Graphics.FillRectangle(
                FoundryTheme.WithAlpha(FoundryTheme.PanelBackground, 155),
                imageX,
                imageY,
                imageSize.Width,
                imageSize.Height);
        }

        if (Checked)
        {
            eventArgs.Graphics.FillRectangle(
                FoundryTheme.SelectionAccent,
                5,
                Height - 2,
                Math.Max(0, Width - 10),
                2);
        }

        if (Enabled && HasFocus && _showFocusRing)
        {
            eventArgs.Graphics.DrawRectangle(
                new Pen(FoundryTheme.WithAlpha(FoundryTheme.SelectionAccent, 150), 1),
                1.5f,
                1.5f,
                Math.Max(0, Width - 3),
                Math.Max(0, Height - 3));
        }
    }
}

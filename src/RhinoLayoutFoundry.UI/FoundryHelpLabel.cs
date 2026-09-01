using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

/// <summary>
/// Compact contextual help attached to a field or section title. Hovering exposes
/// the explanation as a tooltip; clicking or using Enter/Space reveals it inline.
/// Consumers can handle HelpRequested to open richer help instead.
/// </summary>
internal sealed class FoundryHelpLabel : Panel
{
    private readonly Label _details;

    internal FoundryHelpLabel(
        string text,
        string helpText,
        bool emphasized = false)
    {
        var trigger = new HelpTitleDrawable(text, helpText, emphasized);
        _details = FoundryTheme.MutedLabel(helpText);
        _details.Wrap = WrapMode.Word;
        _details.Visible = false;

        trigger.Click += (_, _) =>
        {
            if (HelpRequested is not null)
            {
                HelpRequested.Invoke(this, EventArgs.Empty);
                return;
            }

            _details.Visible = !_details.Visible;
        };

        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space1,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items = { trigger, _details },
        };
    }

    internal event EventHandler? HelpRequested;

    private sealed class HelpTitleDrawable : Drawable
    {
        private readonly string _text;
        private readonly Font _font;
        private readonly Font _questionFont = SystemFonts.Bold(7);
        private bool _hovered;
        private bool _pressed;
        private bool _showFocusRing;

        internal HelpTitleDrawable(string text, string helpText, bool emphasized)
            : base(true)
        {
            _text = text ?? string.Empty;
            _font = emphasized ? SystemFonts.Bold(10) : SystemFonts.Default();
            Size = new Size(Math.Max(28, (int)Math.Ceiling(_text.Length * 7.6 + 18)), 22);
            MinimumSize = Size;
            BackgroundColor = Colors.Transparent;
            CanFocus = true;
            ToolTip = helpText;

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
                Click?.Invoke(this, EventArgs.Empty);
                eventArgs.Handled = true;
                Invalidate();
            };
            KeyDown += (_, eventArgs) =>
            {
                if (!Enabled || eventArgs.Key is not (Keys.Enter or Keys.Space)) return;
                Click?.Invoke(this, EventArgs.Empty);
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

        private void OnPaint(object? sender, PaintEventArgs eventArgs)
        {
            var graphics = eventArgs.Graphics;
            var textColor = Enabled ? FoundryTheme.PrimaryText : FoundryTheme.MutedText;
            var titleSize = graphics.MeasureString(_font, _text);
            var titleY = (Height - titleSize.Height) / 2f + 1;
            var questionX = 3 + titleSize.Width + 2;
            var contentWidth = Math.Min(Width - 1, questionX + 10);
            if (Enabled && _pressed)
            {
                using var pressed = GraphicsPath.GetRoundRect(
                    new RectangleF(0.5f, 0.5f, Math.Max(0, contentWidth), Height - 1),
                    4);
                graphics.FillPath(
                    FoundryTheme.WithAlpha(FoundryTheme.CanvasSubtleSurface, 135),
                    pressed);
            }

            graphics.DrawText(_font, textColor, 3, titleY, _text);

            var questionY = Math.Max(0, titleY - 3);
            var questionColor = Enabled && (_hovered || HasFocus)
                ? FoundryTheme.PrimaryText
                : FoundryTheme.MutedText;
            graphics.DrawText(_questionFont, questionColor, questionX, questionY, "?");

            if (Enabled && _hovered)
            {
                var questionSize = graphics.MeasureString(_questionFont, "?");
                graphics.DrawLine(
                    new Pen(FoundryTheme.WithAlpha(questionColor, 180), 1),
                    questionX,
                    questionY + questionSize.Height,
                    questionX + questionSize.Width,
                    questionY + questionSize.Height);
            }

            if (Enabled && HasFocus && _showFocusRing)
            {
                using var focus = GraphicsPath.GetRoundRect(
                    new RectangleF(0.5f, 0.5f, Math.Max(0, contentWidth), Height - 1),
                    4);
                graphics.DrawPath(
                    new Pen(FoundryTheme.WithAlpha(FoundryTheme.PrimaryText, 145), 1),
                    focus);
            }
        }
    }
}

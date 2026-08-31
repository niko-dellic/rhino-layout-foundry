using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

internal sealed class TemplateRolesOverlay : PixelLayout
{
    private static readonly (TemplateCapability Capability, string Label)[] Options =
    [
        (TemplateCapability.Layout, "Layout"),
        (TemplateCapability.TitleBlock, "Title block"),
        (TemplateCapability.LayerStates, "Layer states"),
        (TemplateCapability.ObjectDisplayModes, "Object display modes"),
    ];

    private readonly TemplateRoleMenu _card;
    private readonly Dictionary<TemplateCapability, bool> _changes = [];
    private IReadOnlyDictionary<OverviewNodeKey, TemplateCapability> _initialValues =
        new Dictionary<OverviewNodeKey, TemplateCapability>();

    internal TemplateRolesOverlay()
    {
        Visible = false;
        BackgroundColor = Colors.Transparent;
        MouseDown += (_, eventArgs) =>
        {
            if (!eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
            CommitAndDismiss();
            eventArgs.Handled = true;
        };
        KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key != Keys.Escape) return;
            Dismiss(commit: false);
            eventArgs.Handled = true;
        };

        _card = new TemplateRoleMenu(Options);
        _card.ValueChanged += (_, eventArgs) =>
            _changes[eventArgs.Capability] = eventArgs.Value;
        _card.CancelRequested += (_, _) => Dismiss(commit: false);
        Add(_card, 0, 0);
    }

    internal event EventHandler<TemplateRolesCommitEventArgs>? CommitRequested;

    internal void ShowPicker(
        IReadOnlyDictionary<OverviewNodeKey, TemplateCapability> initialValues,
        TemplateCapability allowed,
        Point anchor)
    {
        _initialValues = new Dictionary<OverviewNodeKey, TemplateCapability>(initialValues);
        _changes.Clear();
        var states = new Dictionary<TemplateCapability, bool?>();
        foreach (var option in Options)
        {
            var values = initialValues.Values
                .Select(value => value.HasFlag(option.Capability))
                .Distinct()
                .ToArray();
            states[option.Capability] = values.Length == 1 ? values[0] : null;
        }
        _card.Configure(states, allowed);

        const int margin = 8;
        var x = Math.Clamp(anchor.X, margin, Math.Max(margin, ClientSize.Width - _card.Width - margin));
        var below = anchor.Y + FoundryTheme.Space2;
        var y = below + _card.Height <= ClientSize.Height - margin
            ? below
            : Math.Max(margin, anchor.Y - _card.Height - FoundryTheme.Space2);
        Move(_card, x, y);
        Visible = true;
        Application.Instance.AsyncInvoke(() =>
        {
            if (Visible) _card.Focus();
        });
    }

    internal void Dismiss(bool commit)
    {
        if (!Visible) return;
        Visible = false;
        if (!commit || _changes.Count == 0) return;

        var resolved = _initialValues.ToDictionary(pair => pair.Key, pair =>
        {
            var value = pair.Value;
            foreach (var change in _changes)
                value = change.Value ? value | change.Key : value & ~change.Key;
            return value;
        });
        CommitRequested?.Invoke(this, new TemplateRolesCommitEventArgs(resolved));
    }

    private void CommitAndDismiss() => Dismiss(commit: true);

    private sealed class TemplateRoleMenu : Drawable
    {
        private const int RowHeight = 29;
        private const int OuterPadding = 5;
        private readonly (TemplateCapability Capability, string Label)[] _options;
        private readonly Font _font = SystemFonts.Default();
        private readonly Dictionary<TemplateCapability, bool?> _states = [];
        private TemplateCapability _allowed;
        private int _hoveredIndex = -1;
        private int _focusedIndex;

        internal TemplateRoleMenu((TemplateCapability Capability, string Label)[] options)
            : base(true)
        {
            _options = options;
            Size = new Size(232, OuterPadding * 2 + RowHeight * options.Length);
            BackgroundColor = Colors.Transparent;
            CanFocus = true;
            Paint += OnPaint;
            MouseMove += (_, eventArgs) =>
            {
                var index = HitIndex(eventArgs.Location);
                if (_hoveredIndex == index) return;
                _hoveredIndex = index;
                Invalidate();
            };
            MouseLeave += (_, _) =>
            {
                _hoveredIndex = -1;
                Invalidate();
            };
            MouseDown += (_, eventArgs) =>
            {
                if (!eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
                var index = HitIndex(eventArgs.Location);
                if (index >= 0) Activate(index);
                eventArgs.Handled = true;
            };
            KeyDown += OnKeyDown;
            GotFocus += (_, _) => Invalidate();
            LostFocus += (_, _) => Invalidate();
        }

        internal event EventHandler<TemplateRoleValueChangedEventArgs>? ValueChanged;
        internal event EventHandler? CancelRequested;

        internal void Configure(
            IReadOnlyDictionary<TemplateCapability, bool?> states,
            TemplateCapability allowed)
        {
            _states.Clear();
            foreach (var option in _options)
                _states[option.Capability] = states.GetValueOrDefault(option.Capability);
            _allowed = allowed;
            _focusedIndex = Array.FindIndex(_options, option => allowed.HasFlag(option.Capability));
            if (_focusedIndex < 0) _focusedIndex = 0;
            _hoveredIndex = -1;
            Invalidate();
        }

        private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
        {
            if (eventArgs.Key == Keys.Escape)
            {
                CancelRequested?.Invoke(this, EventArgs.Empty);
                eventArgs.Handled = true;
                return;
            }
            var next = eventArgs.Key switch
            {
                Keys.Up => NextEnabled(_focusedIndex, -1),
                Keys.Down => NextEnabled(_focusedIndex, 1),
                Keys.Home => NextEnabled(-1, 1),
                Keys.End => NextEnabled(_options.Length, -1),
                _ => _focusedIndex,
            };
            if (eventArgs.Key is Keys.Up or Keys.Down or Keys.Home or Keys.End)
            {
                _focusedIndex = next;
                Invalidate();
                eventArgs.Handled = true;
                return;
            }
            if (eventArgs.Key is not (Keys.Space or Keys.Enter)) return;
            Activate(_focusedIndex);
            eventArgs.Handled = true;
        }

        private int NextEnabled(int start, int delta)
        {
            for (var index = start + delta; index >= 0 && index < _options.Length; index += delta)
                if (_allowed.HasFlag(_options[index].Capability)) return index;
            return start >= 0 && start < _options.Length ? start : 0;
        }

        private int HitIndex(PointF location)
        {
            if (location.X < OuterPadding || location.X >= Width - OuterPadding ||
                location.Y < OuterPadding || location.Y >= Height - OuterPadding)
                return -1;
            return Math.Clamp((int)((location.Y - OuterPadding) / RowHeight), 0, _options.Length - 1);
        }

        private void Activate(int index)
        {
            if (index < 0 || index >= _options.Length) return;
            var option = _options[index];
            if (!_allowed.HasFlag(option.Capability)) return;
            _focusedIndex = index;
            var value = _states.GetValueOrDefault(option.Capability) != true;
            _states[option.Capability] = value;
            ValueChanged?.Invoke(this, new TemplateRoleValueChangedEventArgs(option.Capability, value));
            Invalidate();
        }

        private void OnPaint(object? sender, PaintEventArgs eventArgs)
        {
            eventArgs.Graphics.AntiAlias = true;
            var bounds = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var surface = GraphicsPath.GetRoundRect(bounds, 10))
            {
                eventArgs.Graphics.FillPath(
                    FoundryTheme.IsDarkMode ? FoundryTheme.ToolbarActiveBackground : FoundryTheme.CanvasSurface,
                    surface);
                eventArgs.Graphics.DrawPath(
                    new Pen(FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 150), 1),
                    surface);
            }

            for (var index = 0; index < _options.Length; index++)
            {
                var option = _options[index];
                var enabled = _allowed.HasFlag(option.Capability);
                var row = new RectangleF(
                    OuterPadding,
                    OuterPadding + index * RowHeight,
                    Width - OuterPadding * 2,
                    RowHeight);
                if (enabled && (_hoveredIndex == index || HasFocus && _focusedIndex == index))
                {
                    using var hover = GraphicsPath.GetRoundRect(
                        new RectangleF(row.X + 1, row.Y + 1, row.Width - 2, row.Height - 2),
                        5);
                    eventArgs.Graphics.FillPath(
                        FoundryTheme.WithAlpha(FoundryTheme.CanvasSubtleSurface,
                            _hoveredIndex == index ? 150 : 90),
                        hover);
                }

                var color = enabled ? FoundryTheme.PrimaryText : FoundryTheme.MutedText;
                var size = eventArgs.Graphics.MeasureString(_font, option.Label);
                eventArgs.Graphics.DrawText(
                    _font,
                    color,
                    row.X + 10,
                    row.Y + (row.Height - size.Height) / 2f,
                    option.Label);
                DrawState(eventArgs.Graphics, row, _states.GetValueOrDefault(option.Capability), color);
            }
        }

        private static void DrawState(Graphics graphics, RectangleF row, bool? state, Color color)
        {
            var centerX = row.Right - 14;
            var centerY = row.Y + row.Height / 2f;
            using var pen = new Pen(color, 1.7f);
            if (state == true)
            {
                graphics.DrawLine(pen, centerX - 6, centerY, centerX - 2, centerY + 4);
                graphics.DrawLine(pen, centerX - 2, centerY + 4, centerX + 6, centerY - 5);
            }
            else if (state is null)
            {
                graphics.DrawLine(pen, centerX - 5, centerY, centerX + 5, centerY);
            }
        }
    }

    private sealed class TemplateRoleValueChangedEventArgs(
        TemplateCapability capability,
        bool value) : EventArgs
    {
        internal TemplateCapability Capability { get; } = capability;
        internal bool Value { get; } = value;
    }
}

internal sealed class TemplateRolesCommitEventArgs(
    IReadOnlyDictionary<OverviewNodeKey, TemplateCapability> values) : EventArgs
{
    internal IReadOnlyDictionary<OverviewNodeKey, TemplateCapability> Values { get; } = values;
}

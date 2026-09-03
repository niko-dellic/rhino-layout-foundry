using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

internal enum CreateResourceKind
{
    Folder,
    Layout,
    AppearanceState,
}

internal sealed class CreateResourceMenuOverlay : PixelLayout
{
    private readonly CreateResourceMenu _menu = new();

    internal CreateResourceMenuOverlay()
    {
        Visible = false;
        BackgroundColor = Colors.Transparent;
        MouseDown += (_, eventArgs) =>
        {
            if (!eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
            Dismiss();
            eventArgs.Handled = true;
        };
        KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key != Keys.Escape) return;
            Dismiss();
            eventArgs.Handled = true;
        };
        _menu.ItemInvoked += (_, eventArgs) =>
        {
            Dismiss();
            ItemInvoked?.Invoke(this, eventArgs);
        };
        _menu.CancelRequested += (_, _) => Dismiss();
        Add(_menu, 0, 0);
    }

    internal event EventHandler<CreateResourceInvokedEventArgs>? ItemInvoked;

    internal void ShowMenu(
        Point anchor,
        IReadOnlyList<FoundryCreateMenuAction> contributedActions)
    {
        _menu.SetContributedActions(contributedActions);
        const int margin = 8;
        var x = Math.Clamp(anchor.X, margin, Math.Max(margin, ClientSize.Width - _menu.Width - margin));
        var below = anchor.Y;
        var y = below + _menu.Height <= ClientSize.Height - margin
            ? below
            : Math.Max(margin, anchor.Y - _menu.Height - 36);
        Move(_menu, x, y);
        Visible = true;
        Application.Instance.AsyncInvoke(() =>
        {
            if (Visible) _menu.Focus();
        });
    }

    internal void Dismiss() => Visible = false;

    private sealed class CreateResourceMenu : Drawable
    {
        private const int RowHeight = 38;
        private const int OuterPadding = 6;
        private readonly Font _font = SystemFonts.Default(14);
        private MenuItem[] _items =
        [
            new(CreateResourceKind.Folder, null, "Folder", FoundryViewIcons.Folder()),
            new(CreateResourceKind.Layout, null, "Sheet", FoundryViewIcons.Layout()),
            new(CreateResourceKind.AppearanceState, null, "Appearance State", FoundryViewIcons.AppearanceState()),
        ];
        private int _hovered = -1;
        private int _focused;

        internal CreateResourceMenu() : base(true)
        {
            Size = new Size(236, OuterPadding * 2 + RowHeight * _items.Length);
            BackgroundColor = Colors.Transparent;
            CanFocus = true;
            Paint += OnPaint;
            MouseMove += (_, eventArgs) =>
            {
                var next = HitIndex(eventArgs.Location);
                if (next == _hovered) return;
                _hovered = next;
                Invalidate();
            };
            MouseLeave += (_, _) =>
            {
                _hovered = -1;
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

        internal event EventHandler<CreateResourceInvokedEventArgs>? ItemInvoked;
        internal event EventHandler? CancelRequested;

        internal void SetContributedActions(
            IReadOnlyList<FoundryCreateMenuAction> contributedActions)
        {
            foreach (var item in _items)
                item.Icon.Dispose();

            var items = new List<MenuItem>
            {
                new(CreateResourceKind.Folder, null, "Folder", FoundryViewIcons.Folder()),
                new(CreateResourceKind.Layout, null, "Sheet", FoundryViewIcons.Layout()),
                new(CreateResourceKind.AppearanceState, null, "Appearance State", FoundryViewIcons.AppearanceState()),
            };
            foreach (var action in contributedActions)
            {
                var icon = action.CreateIcon()
                    ?? throw new InvalidOperationException(
                        $"The create-menu action '{action.Id}' did not provide an icon.");
                items.Add(new MenuItem(null, action.Id, action.Label, icon));
            }

            _items = items.ToArray();
            _hovered = -1;
            _focused = Math.Clamp(_focused, 0, _items.Length - 1);
            Height = OuterPadding * 2 + RowHeight * _items.Length;
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
            if (eventArgs.Key is Keys.Up or Keys.Down or Keys.Home or Keys.End)
            {
                _focused = eventArgs.Key switch
                {
                    Keys.Up => (_focused + _items.Length - 1) % _items.Length,
                    Keys.Down => (_focused + 1) % _items.Length,
                    Keys.Home => 0,
                    _ => _items.Length - 1,
                };
                Invalidate();
                eventArgs.Handled = true;
                return;
            }
            if (eventArgs.Key is not (Keys.Enter or Keys.Space)) return;
            Activate(_focused);
            eventArgs.Handled = true;
        }

        private int HitIndex(PointF location)
        {
            if (location.X < OuterPadding || location.X >= Width - OuterPadding ||
                location.Y < OuterPadding || location.Y >= Height - OuterPadding)
                return -1;
            return Math.Clamp((int)((location.Y - OuterPadding) / RowHeight), 0, _items.Length - 1);
        }

        private void Activate(int index)
        {
            if (index < 0 || index >= _items.Length) return;
            _focused = index;
            var item = _items[index];
            ItemInvoked?.Invoke(
                this,
                item.Kind is { } kind
                    ? new CreateResourceInvokedEventArgs(kind)
                    : new CreateResourceInvokedEventArgs(item.ActionId!));
        }

        private void OnPaint(object? sender, PaintEventArgs eventArgs)
        {
            eventArgs.Graphics.AntiAlias = true;
            using (var surface = GraphicsPath.GetRoundRect(
                       new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 10))
            {
                eventArgs.Graphics.FillPath(
                    FoundryTheme.IsDarkMode ? FoundryTheme.ToolbarActiveBackground : FoundryTheme.CanvasSurface,
                    surface);
                eventArgs.Graphics.DrawPath(
                    new Pen(FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 160), 1), surface);
            }

            for (var index = 0; index < _items.Length; index++)
            {
                var row = new RectangleF(
                    OuterPadding,
                    OuterPadding + index * RowHeight,
                    Width - OuterPadding * 2,
                    RowHeight);
                if (_hovered == index || HasFocus && _focused == index)
                {
                    using var hover = GraphicsPath.GetRoundRect(
                        new RectangleF(row.X + 1, row.Y + 1, row.Width - 2, row.Height - 2), 6);
                    eventArgs.Graphics.FillPath(
                        FoundryTheme.WithAlpha(FoundryTheme.CanvasSubtleSurface,
                            _hovered == index ? 155 : 90), hover);
                }
                var item = _items[index];
                eventArgs.Graphics.DrawImage(item.Icon, row.X + 10, row.Y + 11);
                var textSize = eventArgs.Graphics.MeasureString(_font, item.Label);
                eventArgs.Graphics.DrawText(
                    _font, FoundryTheme.PrimaryText,
                    row.X + 38, row.Y + (row.Height - textSize.Height) / 2f,
                    item.Label);
            }
        }

        private sealed record MenuItem(
            CreateResourceKind? Kind,
            string? ActionId,
            string Label,
            Image Icon);
    }
}

internal sealed class CreateResourceInvokedEventArgs : EventArgs
{
    internal CreateResourceInvokedEventArgs(CreateResourceKind kind)
    {
        Kind = kind;
    }

    internal CreateResourceInvokedEventArgs(string actionId)
    {
        ActionId = actionId;
    }

    internal CreateResourceKind? Kind { get; }

    internal string? ActionId { get; }
}

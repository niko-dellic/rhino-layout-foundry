using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

internal sealed record FoundryMultiSelectChoice(string Group, string Label);

/// <summary>
/// Searchable, extensible multi-select field for compact Foundry forms.
/// </summary>
internal sealed class FoundryMultiSelectField : Panel
{
    private readonly TextBox _search = new();
    private readonly FoundryToolbarIconButton _toggle;
    private readonly Panel _badges = new();
    private readonly Label _emptyState = FoundryTheme.MutedLabel("Nothing selected — type a custom drawing and press Enter");
    private readonly ListBox _results = new();
    private readonly List<FoundryMultiSelectChoice> _choices;
    private readonly List<string> _selected = [];
    private readonly List<FoundryRemovableBadge> _badgeControls = [];
    private Form? _popup;
    private bool _updating;
    private bool _layingOutBadges;
    private int _badgeLayoutWidth;

    internal FoundryMultiSelectField(
        IEnumerable<FoundryMultiSelectChoice> choices,
        string placeholder = "Search or add a drawing type…")
    {
        _choices = choices
            .GroupBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _search.PlaceholderText = placeholder;
        _toggle = new FoundryToolbarIconButton(FoundryViewIcons.ChevronDown(), "Show drawing types");
        _toggle.Size = new Size(32, 32);
        _search.TextChanged += (_, _) =>
        {
            if (!_updating) Filter(show: true);
        };
        _search.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key == Keys.Escape)
            {
                CloseResults();
                eventArgs.Handled = true;
            }
            else if (eventArgs.Key == Keys.Enter)
            {
                CommitSearch();
                eventArgs.Handled = true;
            }
            else if (eventArgs.Key == Keys.Down)
            {
                Filter(show: true);
                _results.Focus();
                if (_results.Items.Count > 0) _results.SelectedIndex = 0;
                eventArgs.Handled = true;
            }
        };
        _toggle.Click += (_, _) =>
        {
            if (_popup?.Visible == true) CloseResults();
            else Filter(show: true, showAll: true);
            _search.Focus();
        };
        _results.SelectedIndexChanged += (_, _) =>
        {
            if (_updating || _results.SelectedIndex < 0) return;
            var visible = VisibleChoices();
            if (_results.SelectedIndex >= visible.Count) return;
            Toggle(visible[_results.SelectedIndex].Label);
            _updating = true;
            _results.SelectedIndex = -1;
            _updating = false;
            Filter(show: true);
        };
        _results.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key == Keys.Escape)
            {
                CloseResults();
                _search.Focus();
                eventArgs.Handled = true;
            }
            else if (eventArgs.Key == Keys.Space && _results.SelectedIndex >= 0)
            {
                var visible = VisibleChoices();
                if (_results.SelectedIndex < visible.Count) Toggle(visible[_results.SelectedIndex].Label);
                Filter(show: true);
                eventArgs.Handled = true;
            }
        };

        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space1,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 0,
                    Items =
                    {
                        new StackLayoutItem(new FoundryFormField(_search, fixedHeight: 32), true),
                        _toggle,
                    },
                },
                _badges,
                _emptyState,
            },
        };
        SizeChanged += (_, _) =>
        {
            var width = ClientSize.Width;
            if (width > 0 && Math.Abs(width - _badgeLayoutWidth) > 2) UpdateBadges(width);
        };
        UpdateBadges();
        UnLoad += (_, _) => ClosePopup();
    }

    internal event EventHandler? ValueChanged;

    internal IReadOnlyList<string> Values => _selected.ToArray();

    internal void SetValues(IEnumerable<string> values)
    {
        _selected.Clear();
        _selected.AddRange(values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase));
        UpdateBadges();
        Filter(show: false);
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void AddValues(IEnumerable<string> values)
    {
        var changed = false;
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()))
        {
            if (_selected.Contains(value, StringComparer.OrdinalIgnoreCase)) continue;
            _selected.Add(value);
            changed = true;
        }
        if (!changed) return;
        UpdateBadges();
        Filter(show: _popup?.Visible == true);
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    public new bool Enabled
    {
        get => base.Enabled;
        set
        {
            base.Enabled = value;
            _search.Enabled = value;
            _toggle.Enabled = value;
            _results.Enabled = value;
            foreach (var badge in _badgeControls) badge.Enabled = value;
            if (!value) CloseResults();
        }
    }

    private void CommitSearch()
    {
        var query = _search.Text.Trim();
        if (query.Length == 0) return;
        var exact = _choices.FirstOrDefault(item =>
            string.Equals(item.Label, query, StringComparison.OrdinalIgnoreCase));
        Toggle(exact?.Label ?? query);
        _updating = true;
        _search.Text = string.Empty;
        _updating = false;
        Filter(show: true, showAll: true);
    }

    private void Toggle(string value)
    {
        var existing = _selected.FindIndex(item =>
            string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) _selected.RemoveAt(existing);
        else _selected.Add(value);
        UpdateBadges();
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private IReadOnlyList<FoundryMultiSelectChoice> VisibleChoices()
    {
        var query = _search.Text.Trim();
        return _choices
            .Where(item => query.Length == 0 ||
                           item.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           item.Group.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => query.Length > 0 && item.Label.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void Filter(bool show, bool showAll = false)
    {
        var visible = showAll
            ? _choices.OrderBy(item => item.Group).ThenBy(item => item.Label).ToArray()
            : VisibleChoices();
        _results.DataStore = visible.Select(item =>
            $"{(_selected.Contains(item.Label, StringComparer.OrdinalIgnoreCase) ? "✓" : "  ")}  {item.Label}   ·   {item.Group}")
            .ToArray();
        if (show && Enabled) ShowResults(Math.Max(1, visible.Count));
        else CloseResults();
    }

    private void UpdateBadges(int? requestedWidth = null)
    {
        if (_layingOutBadges) return;
        _layingOutBadges = true;
        try
        {
            var availableWidth = Math.Max(120, requestedWidth ?? (ClientSize.Width > 0 ? ClientSize.Width : 360));
            _badgeLayoutWidth = availableWidth;
            _badgeControls.Clear();
            _emptyState.Visible = _selected.Count == 0;
            _badges.Visible = _selected.Count > 0;
            if (_selected.Count == 0)
            {
                _badges.Content = null;
                return;
            }

            var rows = new StackLayout
            {
                Spacing = FoundryTheme.Space1,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            var row = BadgeRow();
            var usedWidth = 0;
            foreach (var value in _selected)
            {
                var badge = new FoundryRemovableBadge(value) { Enabled = Enabled };
                if (badge.Width > availableWidth) badge.Size = new Size(availableWidth, badge.Height);
                var nextWidth = usedWidth == 0 ? badge.Width : usedWidth + FoundryTheme.Space1 + badge.Width;
                if (usedWidth > 0 && nextWidth > availableWidth)
                {
                    row.Items.Add(new StackLayoutItem(null, true));
                    rows.Items.Add(row);
                    row = BadgeRow();
                    usedWidth = 0;
                }

                badge.Click += (_, _) => Remove(value);
                _badgeControls.Add(badge);
                row.Items.Add(badge);
                usedWidth = usedWidth == 0 ? badge.Width : usedWidth + FoundryTheme.Space1 + badge.Width;
            }

            row.Items.Add(new StackLayoutItem(null, true));
            rows.Items.Add(row);
            _badges.Content = rows;
        }
        finally
        {
            _layingOutBadges = false;
        }
    }

    private static StackLayout BadgeRow() => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = FoundryTheme.Space1,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    private void Remove(string value)
    {
        var existing = _selected.FindIndex(item =>
            string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        if (existing < 0) return;
        _selected.RemoveAt(existing);
        UpdateBadges();
        Filter(show: _popup?.Visible == true);
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowResults(int count)
    {
        var popup = EnsurePopup();
        var anchor = PointToScreen(new PointF(0, 32));
        var screen = Screen.Screens.FirstOrDefault(candidate => candidate.Bounds.Contains(anchor)) ?? Screen.PrimaryScreen;
        var width = Math.Min(Math.Max(360, Width), Math.Max(360, (int)screen.WorkingArea.Width - 32));
        var height = Math.Min(300, Math.Max(86, count * 28 + 2));
        var x = Math.Clamp((int)Math.Round(anchor.X), (int)screen.WorkingArea.Left + 8,
            (int)screen.WorkingArea.Right - width - 8);
        var y = (int)Math.Round(anchor.Y + FoundryTheme.Space1);
        if (y + height > screen.WorkingArea.Bottom - 8)
            y = (int)Math.Round(PointToScreen(PointF.Empty).Y - height - FoundryTheme.Space1);
        popup.Location = new Point(x, y);
        popup.Size = new Size(width, height);
        popup.Show();
        popup.BringToFront();
    }

    private Form EnsurePopup()
    {
        if (_popup is not null) return _popup;
        _popup = new Form
        {
            Owner = ParentWindow,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Resizable = false,
            Maximizable = false,
            Minimizable = false,
            Closeable = false,
            BackgroundColor = FoundryTheme.CanvasBorder,
            Padding = new Padding(1),
            Content = _results,
        };
        _popup.Closed += (_, _) => _popup = null;
        return _popup;
    }

    private void CloseResults()
    {
        if (_popup is not null) _popup.Visible = false;
    }

    private void ClosePopup()
    {
        var popup = _popup;
        _popup = null;
        popup?.Close();
    }
}

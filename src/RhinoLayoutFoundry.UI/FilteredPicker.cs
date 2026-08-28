using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

internal sealed class FilteredPicker : Panel
{
    private readonly string[] _allLabels;
    private readonly TextBox _textBox;
    private readonly FoundryToolbarIconButton _toggleButton;
    private readonly ListBox _results;
    private Form? _resultsPopup;
    private bool _settingValue;

    internal FilteredPicker(IEnumerable<string> labels, string placeholder)
    {
        _allLabels = labels.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _textBox = new TextBox { PlaceholderText = placeholder };
        _toggleButton = new FoundryToolbarIconButton(
            FoundryViewIcons.ChevronDown(),
            "Show matching choices");
        _results = new ListBox { DataStore = _allLabels, Height = 112 };
        _textBox.TextChanged += (_, _) =>
        {
            if (_settingValue) return;
            Filter(showResults: true);
            ValueChanged?.Invoke(this, EventArgs.Empty);
        };
        _toggleButton.Click += (_, _) =>
        {
            Filter(showResults: _resultsPopup?.Visible != true, showAllForExactValue: true);
            _textBox.Focus();
        };
        _textBox.LostFocus += (_, _) => Application.Instance.AsyncInvoke(() =>
        {
            if (_resultsPopup?.Visible != true) return;
            var mouse = Mouse.Position;
            var mousePoint = new Point((int)Math.Round(mouse.X), (int)Math.Round(mouse.Y));
            if (_resultsPopup.Bounds.Contains(mousePoint) || _results.HasFocus || _toggleButton.HasFocus) return;
            CloseResults();
        });
        _results.SelectedIndexChanged += (_, _) =>
        {
            if (_results.SelectedValue is not string selected) return;
            _settingValue = true;
            _textBox.Text = selected;
            _textBox.CaretIndex = selected.Length;
            _settingValue = false;
            CloseResults();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        };
        UnLoad += (_, _) => ClosePopup();
        Content = new StackLayout
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 0,
                    Items = { new StackLayoutItem(new FoundryFormField(_textBox), true), _toggleButton },
                },
            },
        };
    }

    internal event EventHandler? ValueChanged;
    internal event EventHandler? Opened;

    internal string Text
    {
        get => _textBox.Text;
        set
        {
            _settingValue = true;
            _textBox.Text = value ?? string.Empty;
            _settingValue = false;
            Filter(showResults: false);
        }
    }

    public new bool Enabled
    {
        get => base.Enabled;
        set
        {
            base.Enabled = value;
            _textBox.Enabled = value;
            _toggleButton.Enabled = value;
            _results.Enabled = value;
            if (!value) CloseResults();
        }
    }

    internal void CloseResults()
    {
        if (_resultsPopup is not null) _resultsPopup.Visible = false;
    }

    private void Filter(bool showResults, bool showAllForExactValue = false)
    {
        var current = _textBox.Text.Trim();
        var query = showAllForExactValue && _allLabels.Contains(current, StringComparer.OrdinalIgnoreCase)
            ? string.Empty
            : current;
        var matches = _allLabels
            .Where(label => query.Length == 0 || label.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(label => query.Length > 0 && label.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _results.DataStore = matches;
        if (showResults && Enabled && matches.Length > 0)
            ShowResults();
        else
            CloseResults();
    }

    private void ShowResults()
    {
        var popup = EnsurePopup();
        PositionPopup();
        popup.Show();
        popup.BringToFront();
        Opened?.Invoke(this, EventArgs.Empty);
    }

    private Form EnsurePopup()
    {
        if (_resultsPopup is not null) return _resultsPopup;
        var popup = new Form
        {
            Owner = ParentWindow,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Resizable = false,
            Maximizable = false,
            Minimizable = false,
            Closeable = false,
            AutoSize = false,
            BackgroundColor = FoundryTheme.CanvasBorder,
            Padding = new Padding(1),
            Content = _results,
        };
        popup.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key != Keys.Escape) return;
            CloseResults();
            _textBox.Focus();
            eventArgs.Handled = true;
        };
        popup.LostFocus += (_, _) => Application.Instance.AsyncInvoke(() =>
        {
            if (_resultsPopup != popup || !popup.Visible) return;
            var mouse = Mouse.Position;
            var mousePoint = new Point((int)Math.Round(mouse.X), (int)Math.Round(mouse.Y));
            if (popup.Bounds.Contains(mousePoint) || _results.HasFocus || _textBox.HasFocus) return;
            CloseResults();
        });
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_resultsPopup, popup)) _resultsPopup = null;
        };
        _resultsPopup = popup;
        return popup;
    }

    private void PositionPopup()
    {
        if (_resultsPopup is null) return;
        var anchor = PointToScreen(new PointF(0, Height));
        var screen = Screen.Screens.FirstOrDefault(candidate => candidate.Bounds.Contains(anchor)) ??
                     Screen.PrimaryScreen;
        var work = screen.WorkingArea;
        var width = Math.Max(240, Width);
        var height = 114;
        var left = (int)Math.Ceiling(work.Left);
        var top = (int)Math.Ceiling(work.Top);
        var right = (int)Math.Floor(work.Right);
        var bottom = (int)Math.Floor(work.Bottom);
        width = Math.Min(width, Math.Max(240, right - left - FoundryTheme.Space4 * 2));
        var x = Math.Clamp((int)Math.Round(anchor.X), left + FoundryTheme.Space2,
            right - width - FoundryTheme.Space2);
        var y = (int)Math.Round(anchor.Y + FoundryTheme.Space1);
        if (y + height > bottom - FoundryTheme.Space2)
            y = (int)Math.Round(PointToScreen(PointF.Empty).Y - height - FoundryTheme.Space1);
        y = Math.Clamp(y, top + FoundryTheme.Space2, bottom - height - FoundryTheme.Space2);
        _resultsPopup.Size = new Size(width, height);
        _resultsPopup.Location = new Point(x, y);
    }

    private void ClosePopup()
    {
        if (_resultsPopup is null) return;
        var popup = _resultsPopup;
        _resultsPopup = null;
        popup.Close();
    }
}

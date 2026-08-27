using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

internal sealed class FilteredPicker : Panel
{
    private readonly string[] _allLabels;
    private readonly TextBox _textBox;
    private readonly Button _toggleButton;
    private readonly ListBox _results;
    private bool _settingValue;

    internal FilteredPicker(IEnumerable<string> labels, string placeholder)
    {
        _allLabels = labels.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _textBox = new TextBox { PlaceholderText = placeholder };
        _toggleButton = new Button { Text = "⌄", Width = 30, ToolTip = "Show matching choices" };
        _results = new ListBox { DataStore = _allLabels, Height = 96, Visible = false };
        _textBox.TextChanged += (_, _) =>
        {
            if (_settingValue) return;
            Filter(showResults: true);
            ValueChanged?.Invoke(this, EventArgs.Empty);
        };
        _toggleButton.Click += (_, _) =>
        {
            Filter(showResults: !_results.Visible, showAllForExactValue: true);
            _textBox.Focus();
        };
        _results.SelectedIndexChanged += (_, _) =>
        {
            if (_results.SelectedValue is not string selected) return;
            _settingValue = true;
            _textBox.Text = selected;
            _textBox.CaretIndex = selected.Length;
            _settingValue = false;
            _results.Visible = false;
            ValueChanged?.Invoke(this, EventArgs.Empty);
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
                    Items = { new StackLayoutItem(_textBox, true), _toggleButton },
                },
                _results,
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
            if (!value) _results.Visible = false;
        }
    }

    internal void CloseResults() => _results.Visible = false;

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
        _results.Visible = showResults && Enabled;
        if (_results.Visible) Opened?.Invoke(this, EventArgs.Empty);
    }
}

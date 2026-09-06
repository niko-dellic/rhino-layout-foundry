using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

internal enum PaperOrientation
{
    Portrait,
    Landscape,
}

/// <summary>
/// Layout-specific composition for the hierarchy paper cell. Shared controls own
/// picker and segmented-button behavior; this type only coordinates paper choices.
/// </summary>
internal sealed class PaperSizeCellEditor : Panel
{
    private readonly FilteredPicker _picker;
    private readonly FoundryTextSegmentedControl _orientation;
    private bool _updating;

    internal PaperSizeCellEditor(
        IEnumerable<string> choices,
        string selectedChoice,
        PaperOrientation orientation)
    {
        _orientation = new FoundryTextSegmentedControl(
            ["Portrait", "Landscape"],
            selectedIndex: orientation == PaperOrientation.Landscape ? 1 : 0,
            segmentWidth: 70);
        _picker = new FilteredPicker(
            choices,
            "Search paper sizes",
            popupHeight: 360,
            controlHeight: FoundryTheme.TableRowHeight,
            popupFooter: _orientation)
        {
            Text = selectedChoice,
        };

        _picker.SelectionCommitted += (_, _) => ChoiceCommitted?.Invoke(this, EventArgs.Empty);
        _picker.DismissRequested += (_, _) => DismissRequested?.Invoke(this, EventArgs.Empty);
        _orientation.SelectedIndexChanged += (_, _) =>
        {
            if (!_updating) OrientationCommitted?.Invoke(this, EventArgs.Empty);
        };

        Content = _picker;
    }

    internal event EventHandler? ChoiceCommitted;

    internal event EventHandler? OrientationCommitted;

    internal event EventHandler? DismissRequested;

    internal string SelectedChoice => _picker.Text.Trim();

    internal PaperOrientation Orientation => _orientation.SelectedIndex == 1
        ? PaperOrientation.Landscape
        : PaperOrientation.Portrait;

    internal void SetValue(string selectedChoice, PaperOrientation orientation)
    {
        _updating = true;
        try
        {
            _picker.Text = selectedChoice;
            _orientation.SelectedIndex = orientation == PaperOrientation.Landscape ? 1 : 0;
        }
        finally
        {
            _updating = false;
        }
    }

    internal void OpenChoices() => _picker.OpenResults();

    internal void CloseChoices() => _picker.CloseResults();
}

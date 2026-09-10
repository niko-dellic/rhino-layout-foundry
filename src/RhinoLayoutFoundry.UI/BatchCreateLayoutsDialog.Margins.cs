using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.UI;

internal sealed partial class BatchCreateLayoutsDialog
{
    private readonly FoundryTextSegmentedControl _spacingMode = new(
        ["Equal Spacing", "Separate Spacing"], segmentWidth: 124);
    private readonly NumericStepper _sharedMargin = MarginStepper();
    private readonly NumericStepper _pageEdgeMargin = MarginStepper();
    private readonly NumericStepper _detailGap = MarginStepper();
    private readonly NumericStepper _titleBlockGap = MarginStepper();
    private readonly NumericStepper _titleBlockEdge = MarginStepper();
    private readonly List<Label> _marginUnitLabels = [];
    private readonly Label _marginHint = new() { Wrap = WrapMode.Word };
    private readonly List<Control> _sharedMarginControls = [];
    private readonly List<Control> _separateMarginControls = [];
    private readonly Panel _marginHintKey = new();
    private enum SpacingField { All, PageEdge, DetailGap, TitleBlockGap, TitleBlockEdge }
    private bool _loadingSpacing;
    private bool _spacingInitialized;
    private string _spacingUnit = "Millimeters";

    private static NumericStepper MarginStepper() => new()
    {
        MinValue = 0,
        MaxValue = 1000000,
        DecimalPlaces = 2,
        Value = 10,
    };

    private void InitializeSpacingEditor()
    {
        if (_isEditMode) return;
        _spacingInitialized = true;
        if (_drafts.Count > 0) LoadSpacingEditors(_drafts[0]);
        _spacingMode.SelectedIndexChanged += (_, _) =>
        {
            if (_loadingSpacing || _updatingEditors) return;
            if (_spacingMode.SelectedIndex == 1)
            {
                _loadingSpacing = true;
                _pageEdgeMargin.Value = _detailGap.Value = _titleBlockGap.Value = _titleBlockEdge.Value = _sharedMargin.Value;
                _loadingSpacing = false;
            }
            ApplySpacingToTargets();
        };
        BindMarginEditor(_sharedMargin, SpacingField.All);
        BindMarginEditor(_pageEdgeMargin, SpacingField.PageEdge);
        BindMarginEditor(_detailGap, SpacingField.DetailGap);
        BindMarginEditor(_titleBlockGap, SpacingField.TitleBlockGap);
        BindMarginEditor(_titleBlockEdge, SpacingField.TitleBlockEdge);
    }

    private void AddMarginRows(TableLayout table)
    {
        // Share the Layout table's label/value columns, including in separate mode.
        table.Rows.Add(new TableRow(new Label { Text = "Spacing", TextAlignment = TextAlignment.Right },
            new TableCell(_spacingMode, true)));
        AddMarginRow(table, "Shared margin", _sharedMargin, _sharedMarginControls);
        AddMarginRow(table, "Page edge to details", _pageEdgeMargin, _separateMarginControls);
        AddMarginRow(table, "Between details", _detailGap, _separateMarginControls);
        AddMarginRow(table, "Title block to details", _titleBlockGap, _separateMarginControls);
        AddMarginRow(table, "Page edge to title block", _titleBlockEdge, _separateMarginControls);
        table.Rows.Add(new TableRow(_marginHintKey, new TableCell(_marginHint, true)));
        UpdateSpacingAvailability();
    }

    private void AddMarginRow(TableLayout table, string text, NumericStepper input, List<Control> controls)
    {
        var key = new Label { Text = text, TextAlignment = TextAlignment.Right };
        var value = MarginField(input);
        controls.Add(key);
        controls.Add(value);
        table.Rows.Add(new TableRow(key, new TableCell(value, true)));
    }

    private Control MarginField(NumericStepper input)
    {
        var units = new Label { VerticalAlignment = VerticalAlignment.Center };
        _marginUnitLabels.Add(units);
        return new TableLayout
        {
            Spacing = new Size(FoundryTheme.Space1, 0),
            Rows = { new TableRow(new TableCell(new FoundryFormField(input), true), units) },
        };
    }

    private void BindMarginEditor(NumericStepper input, SpacingField field)
    {
        input.ValueChanged += (_, _) => ApplySpacingToTargets(field, renderNativePreview: false);
        input.LostFocus += (_, _) =>
        {
            if (!_loadingSpacing && !_updatingEditors) QueueDraftLayoutPreview();
        };
    }

    private bool MarginEditorHasFocus => _sharedMargin.HasFocus || _pageEdgeMargin.HasFocus ||
        _detailGap.HasFocus || _titleBlockGap.HasFocus || _titleBlockEdge.HasFocus;

    private double ReadSharedMargin(PaperRecipe paper) => !_spacingInitialized
        ? LayoutSpacing.Default(paper.UnitSystem).PageEdge
        : LayoutSpacing.Uniform(_sharedMargin.Value, _spacingUnit).InUnits(paper.UnitSystem).PageEdge;

    private LayoutSpacing ReadSpacing(PaperRecipe paper) => !_spacingInitialized
        ? LayoutSpacing.Default(paper.UnitSystem)
        : (_spacingMode.SelectedIndex == 1
            ? new LayoutSpacing(_pageEdgeMargin.Value, _detailGap.Value, _titleBlockGap.Value,
                _titleBlockEdge.Value, _spacingUnit)
            : LayoutSpacing.Uniform(_sharedMargin.Value, _spacingUnit)).InUnits(paper.UnitSystem);

    private void ApplySpacingToTargets(SpacingField field = SpacingField.All, bool renderNativePreview = true)
    {
        if (_isEditMode || _loadingSpacing || _updatingEditors) return;
        foreach (var index in TargetDraftIndices())
        {
            var draft = _drafts[index];
            if (draft.Layout.TemplateId is not null) continue;
            _drafts[index] = draft with
            {
                Spacing = UpdatedSpacing(draft, field),
                SeparateSpacing = _spacingMode.SelectedIndex == 1,
                SharedMargin = field == SpacingField.All ? ReadSharedMargin(draft.Paper) : draft.SharedMargin,
            };
        }
        // Rebinding the table or reattaching field hosts during native text editing
        // can end AppKit's field-editor session, including on Backspace.
        _layoutSelectorPreview.SetPagePreview(null, null);
        RefreshPreview(refreshDetailAssignments: false, refreshRows: false);
        if (renderNativePreview)
        {
            UpdateSpacingAvailability();
            QueueDraftLayoutPreview();
        }
    }

    private LayoutSpacing UpdatedSpacing(CreationDraft draft, SpacingField field)
    {
        var values = ReadSpacing(draft.Paper);
        var previous = (draft.Spacing ?? LayoutSpacing.Default(draft.Paper.UnitSystem)).InUnits(draft.Paper.UnitSystem);
        return field switch
        {
            SpacingField.PageEdge => previous with { PageEdge = values.PageEdge },
            SpacingField.DetailGap => previous with { DetailGap = values.DetailGap },
            SpacingField.TitleBlockGap => previous with { TitleBlockGap = values.TitleBlockGap },
            SpacingField.TitleBlockEdge => previous with { TitleBlockEdge = values.TitleBlockEdge },
            _ => values,
        };
    }

    private void LoadSpacingEditors(CreationDraft draft)
    {
        if (_isEditMode) return;
        _loadingSpacing = true;
        try
        {
            var spacing = (draft.Spacing ?? LayoutSpacing.Default(draft.Paper.UnitSystem)).InUnits(draft.Paper.UnitSystem);
            _spacingUnit = draft.Paper.UnitSystem;
            _sharedMargin.Value = draft.SharedMargin ?? spacing.PageEdge;
            _spacingMode.SelectedIndex = draft.SeparateSpacing ? 1 : 0;
            _pageEdgeMargin.Value = spacing.PageEdge;
            _detailGap.Value = spacing.DetailGap;
            _titleBlockGap.Value = spacing.TitleBlockGap;
            _titleBlockEdge.Value = spacing.TitleBlockEdge;
            UpdateSpacingAvailability();
        }
        finally { _loadingSpacing = false; }
    }

    private static void SetEnabled(Control control, bool enabled)
    {
        if (control.Enabled != enabled) control.Enabled = enabled;
    }

    private static void SetVisible(Control control, bool visible)
    {
        if (control.Visible != visible) control.Visible = visible;
    }

    private void UpdateSpacingAvailability()
    {
        if (_isEditMode || !_spacingInitialized) return;
        var generated = TargetDraftIndices().Select(index => _drafts[index])
            .Where(draft => draft.Layout.TemplateId is null).ToArray();
        var enabled = generated.Length > 0;
        var separate = _spacingMode.SelectedIndex == 1;
        var unitLabel = _spacingUnit switch
        {
            "Millimeters" => "mm",
            "Centimeters" => "cm",
            "Meters" => "m",
            "Inches" => "in",
            "Feet" => "ft",
            _ => _spacingUnit,
        };
        foreach (var label in _marginUnitLabels)
            if (label.Text != unitLabel) label.Text = unitLabel;
        _marginHint.Text = "Saved templates keep their geometry.";
        SetVisible(_marginHint, !enabled);
        SetVisible(_marginHintKey, !enabled);
        SetEnabled(_spacingMode, enabled);
        foreach (var control in _sharedMarginControls)
        {
            SetEnabled(control, enabled);
            SetVisible(control, !separate);
        }
        foreach (var control in _separateMarginControls)
        {
            SetEnabled(control, enabled);
            SetVisible(control, separate);
        }
        var titleBlockEnabled = enabled && generated.Any(draft => draft.TitleBlock.BuiltInKind is not null);
        SetEnabled(_titleBlockGap, titleBlockEnabled);
        SetEnabled(_titleBlockEdge, titleBlockEnabled);
    }
}

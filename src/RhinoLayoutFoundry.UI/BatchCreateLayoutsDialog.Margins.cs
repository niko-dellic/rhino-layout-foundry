using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.UI;

internal sealed partial class BatchCreateLayoutsDialog
{
    private readonly FoundryCheckBox _separateSpacing = new("Separate spacing");
    private readonly NumericStepper _sharedMargin = MarginStepper();
    private readonly NumericStepper _pageEdgeMargin = MarginStepper();
    private readonly NumericStepper _detailGap = MarginStepper();
    private readonly NumericStepper _titleBlockGap = MarginStepper();
    private readonly NumericStepper _titleBlockEdge = MarginStepper();
    private readonly Label _marginUnits = new();
    private readonly Label _marginHint = new() { Wrap = WrapMode.Word };
    private readonly Panel _sharedMarginHost = new();
    private readonly Panel _separateMarginsHost = new();
    private enum SpacingField { All, PageEdge, DetailGap, TitleBlockGap, TitleBlockEdge }
    private bool _loadingSpacing;
    private bool _spacingInitialized;
    private string _spacingUnit = "Millimeters";

    private static NumericStepper MarginStepper() => new()
    {
        MinValue = 0,
        MaxValue = 1000000,
        DecimalPlaces = 6,
        Value = 10,
    };

    private void InitializeSpacingEditor()
    {
        if (_isEditMode) return;
        _spacingInitialized = true;
        if (_drafts.Count > 0) LoadSpacingEditors(_drafts[0]);
        _separateSpacing.CheckedChanged += (_, _) =>
        {
            if (_loadingSpacing || _updatingEditors) return;
            if (_separateSpacing.Checked == true)
            {
                _loadingSpacing = true;
                _pageEdgeMargin.Value = _detailGap.Value = _titleBlockGap.Value = _titleBlockEdge.Value = _sharedMargin.Value;
                _loadingSpacing = false;
            }
            ApplySpacingToTargets();
        };
        _sharedMargin.ValueChanged += (_, _) => ApplySpacingToTargets();
        _pageEdgeMargin.ValueChanged += (_, _) => ApplySpacingToTargets(SpacingField.PageEdge);
        _detailGap.ValueChanged += (_, _) => ApplySpacingToTargets(SpacingField.DetailGap);
        _titleBlockGap.ValueChanged += (_, _) => ApplySpacingToTargets(SpacingField.TitleBlockGap);
        _titleBlockEdge.ValueChanged += (_, _) => ApplySpacingToTargets(SpacingField.TitleBlockEdge);
    }

    private Control CreateMarginsEditor()
    {
        _sharedMarginHost.Content = new TableLayout
        {
            Spacing = new Size(FoundryTheme.Space2, FoundryTheme.Space1),
            Rows = { new TableRow(new Label { Text = "Shared margin" }, new FoundryFormField(_sharedMargin)) },
        };
        _separateMarginsHost.Content = new TableLayout
        {
            Spacing = new Size(FoundryTheme.Space2, FoundryTheme.Space1),
            Rows =
            {
                new TableRow(new Label { Text = "Page edge to details" }, new FoundryFormField(_pageEdgeMargin)),
                new TableRow(new Label { Text = "Between details" }, new FoundryFormField(_detailGap)),
                new TableRow(new Label { Text = "Title block to details" }, new FoundryFormField(_titleBlockGap)),
                new TableRow(new Label { Text = "Page edge to title block" }, new FoundryFormField(_titleBlockEdge)),
            },
        };
        UpdateSpacingAvailability();
        return new StackLayout
        {
            Spacing = FoundryTheme.Space1,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items = { _marginUnits, _separateSpacing, _sharedMarginHost, _separateMarginsHost, _marginHint },
        };
    }

    private double ReadSharedMargin(PaperRecipe paper) => !_spacingInitialized
        ? LayoutSpacing.Default(paper.UnitSystem).PageEdge
        : LayoutSpacing.Uniform(_sharedMargin.Value, _spacingUnit).InUnits(paper.UnitSystem).PageEdge;

    private LayoutSpacing ReadSpacing(PaperRecipe paper) => !_spacingInitialized
        ? LayoutSpacing.Default(paper.UnitSystem)
        : (_separateSpacing.Checked == true
            ? new LayoutSpacing(_pageEdgeMargin.Value, _detailGap.Value, _titleBlockGap.Value,
                _titleBlockEdge.Value, _spacingUnit)
            : LayoutSpacing.Uniform(_sharedMargin.Value, _spacingUnit)).InUnits(paper.UnitSystem);

    private void ApplySpacingToTargets(SpacingField field = SpacingField.All)
    {
        if (_isEditMode || _loadingSpacing || _updatingEditors) return;
        ApplyToTargets(draft => draft.Layout.TemplateId is not null ? draft : draft with
        {
            Spacing = UpdatedSpacing(draft, field),
            SeparateSpacing = _separateSpacing.Checked == true,
            SharedMargin = field == SpacingField.All ? ReadSharedMargin(draft.Paper) : draft.SharedMargin,
        });
        QueueDraftLayoutPreview();
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
            _separateSpacing.Checked = draft.SeparateSpacing;
            _pageEdgeMargin.Value = spacing.PageEdge;
            _detailGap.Value = spacing.DetailGap;
            _titleBlockGap.Value = spacing.TitleBlockGap;
            _titleBlockEdge.Value = spacing.TitleBlockEdge;
            UpdateSpacingAvailability();
        }
        finally { _loadingSpacing = false; }
    }

    private void UpdateSpacingAvailability()
    {
        if (_isEditMode || !_spacingInitialized) return;
        var generated = TargetDraftIndices().Select(index => _drafts[index])
            .Where(draft => draft.Layout.TemplateId is null).ToArray();
        var enabled = generated.Length > 0;
        var separate = _separateSpacing.Checked == true;
        _marginUnits.Text = $"Paper units: {_spacingUnit.ToLowerInvariant()}";
        _marginHint.Text = enabled ? "Uniform on all sides. Saved templates keep their geometry."
            : "Saved templates keep their geometry.";
        _separateSpacing.Enabled = enabled;
        _sharedMarginHost.Enabled = enabled;
        _separateMarginsHost.Enabled = enabled;
        _sharedMarginHost.Visible = !separate;
        _separateMarginsHost.Visible = separate;
        _titleBlockGap.Enabled = _titleBlockEdge.Enabled = enabled && generated.Any(draft => draft.TitleBlock.BuiltInKind is not null);
    }
}

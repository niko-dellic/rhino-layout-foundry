using static RhinoLayoutFoundry.UI.BatchLayoutLabels;
using System.Linq.Expressions;
using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Naming;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

internal sealed class DetailAssignmentDialog : Dialog
{
    private readonly NamedViewChoice[] _namedViewChoices;
    private readonly bool _mixedNamedViews;
    private readonly bool _mixedDisplayModes;
    private readonly DetailNamedViewTray _namedViewTray;
    private readonly FilteredPicker _displayModePicker;
    private readonly DetailDisplayModeChoice[] _displayModeChoices;
    private readonly bool _mixedAppearanceStates;
    private readonly FilteredPicker _appearanceStatePicker;
    private readonly DetailAppearanceStateChoice[] _appearanceStateChoices;
    private readonly Label _displayModeError;
    private readonly NamedViewPreviewTray _previewSource;

    internal DetailAssignmentDialog(
        string detailLabel,
        int targetCount,
        NamedViewChoice[] namedViewChoices,
        NamedViewPreviewTray previewSource,
        IReadOnlyDictionary<Guid, string> displayModes,
        string? namedView,
        bool mixedNamedViews,
        Guid? displayModeId,
        bool mixedDisplayModes,
        IReadOnlyDictionary<string, Guid> appearanceStates,
        Guid? appearanceStateId,
        bool mixedAppearanceStates,
        Guid? initialPreviewDisplayModeId,
        Guid? inheritedDisplayModeId,
        PreviewAppearance? previewAppearance,
        Func<Guid?, PreviewAppearance?, Task> requestPreviews,
        bool canRevert)
    {
        _namedViewChoices = namedViewChoices;
        _mixedNamedViews = mixedNamedViews;
        _mixedDisplayModes = mixedDisplayModes;
        _mixedAppearanceStates = mixedAppearanceStates;
        _previewSource = previewSource;
        var namedViewIndex = Array.FindIndex(namedViewChoices, choice => string.Equals(
            choice.Name, namedView, StringComparison.OrdinalIgnoreCase));
        _namedViewTray = new DetailNamedViewTray(
            namedViewChoices,
            previewSource,
            mixedNamedViews,
            mixedNamedViews ? 0 : Math.Max(0, namedViewIndex),
            initialPreviewDisplayModeId,
            previewAppearance);

        var orderedDisplayModes = displayModes
            .OrderBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var displayModeChoices = new List<DetailDisplayModeChoice>();
        if (mixedDisplayModes)
        {
            displayModeChoices.Add(new DetailDisplayModeChoice(MixedDisplayMode, null));
        }
        displayModeChoices.Add(new DetailDisplayModeChoice(InheritPageDisplayMode, null));
        foreach (var mode in orderedDisplayModes)
        {
            displayModeChoices.Add(new DetailDisplayModeChoice(mode.Value, mode.Key));
        }
        _displayModeChoices = displayModeChoices.ToArray();
        var initialDisplayMode = mixedDisplayModes
            ? _displayModeChoices[0]
            : _displayModeChoices.FirstOrDefault(choice => choice.Id == displayModeId) ??
              _displayModeChoices.First(choice => choice.Label == InheritPageDisplayMode);
        _displayModePicker = new FilteredPicker(
            _displayModeChoices.Select(choice => choice.Label),
            "Search display modes");
        _displayModePicker.Width = 280;
        _displayModePicker.Text = initialDisplayMode.Label;
        _displayModeError = new Label
        {
            TextColor = FoundryTheme.DangerAccent,
            Wrap = WrapMode.Word,
            Visible = false,
        };
        _displayModePicker.ValueChanged += (_, _) =>
        {
            _displayModeError.Text = string.Empty;
            _displayModeError.Visible = false;
            var selected = _displayModeChoices.FirstOrDefault(choice => string.Equals(
                choice.Label,
                _displayModePicker.Text.Trim(),
                StringComparison.OrdinalIgnoreCase));
            if (selected is null) return;
            var effectiveModeId = selected.Id ?? inheritedDisplayModeId;
            _namedViewTray.SetPreviewContext(effectiveModeId, previewAppearance);
            _ = requestPreviews(effectiveModeId, previewAppearance);
        };

        var appearanceStateChoices = new List<DetailAppearanceStateChoice>();
        if (mixedAppearanceStates)
            appearanceStateChoices.Add(new DetailAppearanceStateChoice(MixedDisplayMode, null));
        appearanceStateChoices.Add(new DetailAppearanceStateChoice(InheritSheetAppearanceState, null));
        appearanceStateChoices.AddRange(appearanceStates.Select(pair =>
            new DetailAppearanceStateChoice(pair.Key, pair.Value)));
        _appearanceStateChoices = appearanceStateChoices.ToArray();
        var initialAppearanceState = mixedAppearanceStates
            ? _appearanceStateChoices[0]
            : _appearanceStateChoices.FirstOrDefault(choice => choice.Id == appearanceStateId) ??
              _appearanceStateChoices.First(choice => choice.Label == InheritSheetAppearanceState);
        _appearanceStatePicker = new FilteredPicker(
            _appearanceStateChoices.Select(choice => choice.Label),
            "Search appearance states");
        _appearanceStatePicker.Width = 280;
        _appearanceStatePicker.Text = initialAppearanceState.Label;

        Title = $"{detailLabel} settings";
        MinimumSize = new Size(610, 460);
        Resizable = true;
        Padding = new Padding(FoundryTheme.Space4);
        BackgroundColor = FoundryTheme.PanelBackground;

        var apply = new FoundryDialogButton("Apply", FoundryDialogButtonStyle.Primary);
        var cancel = new FoundryDialogButton("Cancel", FoundryDialogButtonStyle.Secondary);
        var revert = new FoundryDialogButton("Revert detail", FoundryDialogButtonStyle.Secondary)
        {
            Visible = canRevert,
            ToolTip = "Restore this detail's named view, display mode, and appearance state to their values when the editor opened.",
        };
        apply.Click += (_, _) =>
        {
            if (!_displayModePicker.ContainsChoice(_displayModePicker.Text))
            {
                _displayModeError.Text = "Choose an available display mode or use the page setting.";
                _displayModeError.Visible = true;
                _displayModePicker.Focus();
                return;
            }
            if (!_appearanceStatePicker.ContainsChoice(_appearanceStatePicker.Text))
            {
                _displayModeError.Text =
                    "Choose an available appearance state or use the sheet setting.";
                _displayModeError.Visible = true;
                _appearanceStatePicker.Focus();
                return;
            }

            Succeeded = true;
            Close();
        };
        cancel.Click += (_, _) => Close();
        revert.Click += (_, _) =>
        {
            RevertRequested = true;
            Succeeded = true;
            Close();
        };
        FoundryDialogActions.Bind(this, apply, cancel);
        _previewSource.PreviewsChanged += OnPreviewsChanged;
        Closed += (_, _) => _previewSource.PreviewsChanged -= OnPreviewsChanged;

        var namedViewScroll = new Scrollable
        {
            Border = BorderType.None,
            ExpandContentWidth = false,
            ExpandContentHeight = false,
            Height = 270,
            Content = _namedViewTray,
        };
        Shown += (_, _) => Application.Instance.AsyncInvoke(() =>
        {
            var maximum = Math.Max(0, _namedViewTray.ContentHeight - namedViewScroll.Height);
            var target = Math.Clamp(
                _namedViewTray.SelectedCenterY - namedViewScroll.Height / 2,
                0,
                maximum);
            namedViewScroll.ScrollPosition = new Point(0, target);
            _namedViewTray.Focus();
        });
        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space3,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new StackLayoutItem(namedViewScroll, true),
                new TableLayout
                {
                    Spacing = new Size(FoundryTheme.Space2, FoundryTheme.Space1),
                    Rows =
                    {
                        new TableRow(
                            new Label { Text = "Display mode" },
                            new TableCell(_displayModePicker, true)),
                        new TableRow(
                            new Label { Text = "Appearance state" },
                            new TableCell(_appearanceStatePicker, true)),
                    },
                },
                _displayModeError,
                new TableLayout
                {
                    Rows = { new TableRow(revert, new TableCell(null, true), cancel, apply) },
                    Spacing = new Size(FoundryTheme.Space2, 0),
                },
            },
        };
    }

    internal bool Succeeded { get; private set; }
    internal bool RevertRequested { get; private set; }
    internal bool ChangeNamedView => !_mixedNamedViews || _namedViewTray.SelectedIndex > 0;
    internal string? NamedView
    {
        get
        {
            var index = _namedViewTray.SelectedIndex - (_mixedNamedViews ? 1 : 0);
            return index >= 0 && index < _namedViewChoices.Length
                ? _namedViewChoices[index].Name
                : null;
        }
    }
    internal bool ChangeDisplayMode => !_mixedDisplayModes ||
                                       !string.Equals(
                                           _displayModePicker.Text.Trim(),
                                           MixedDisplayMode,
                                           StringComparison.OrdinalIgnoreCase);
    internal Guid? DisplayModeId => _displayModeChoices.FirstOrDefault(choice =>
        string.Equals(
            choice.Label,
            _displayModePicker.Text.Trim(),
            StringComparison.OrdinalIgnoreCase))?.Id;
    internal bool ChangeAppearanceState => !_mixedAppearanceStates ||
                                           !string.Equals(
                                               _appearanceStatePicker.Text.Trim(),
                                               MixedDisplayMode,
                                               StringComparison.OrdinalIgnoreCase);
    internal Guid? AppearanceStateId => _appearanceStateChoices.FirstOrDefault(choice =>
        string.Equals(
            choice.Label,
            _appearanceStatePicker.Text.Trim(),
            StringComparison.OrdinalIgnoreCase))?.Id;

    private void OnPreviewsChanged(object? sender, EventArgs eventArgs) => _namedViewTray.Invalidate();

    private sealed record DetailDisplayModeChoice(string Label, Guid? Id);
    private sealed record DetailAppearanceStateChoice(string Label, Guid? Id);
}


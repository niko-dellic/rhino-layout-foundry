using System.Linq.Expressions;
using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Naming;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

internal sealed record LayoutChoice(
    string Label,
    BuiltInLayoutKind BuiltInLayout,
    Guid? TemplateId,
    SheetTemplateRecipe? Template);
internal sealed record TitleBlockChoice(
    BuiltInTitleBlockKind? BuiltInKind,
    string Label);
internal sealed record NamedViewChoice(string? Name, string Label);
internal sealed record LayerChoice(Guid Id, string Label);
internal sealed record PaperPreset(string Label, double Width, double Height, string UnitSystem);
internal sealed record IndexModeChoice(string Label, NamingIndexMode Mode);
internal readonly record struct LayoutGroupKey(BuiltInLayoutKind? BuiltInLayout, Guid? TemplateId)
{
    internal static LayoutGroupKey For(LayoutChoice layout) => layout.TemplateId is { } templateId
        ? new LayoutGroupKey(null, templateId)
        : new LayoutGroupKey(layout.BuiltInLayout, null);
}
internal sealed record CreationDraft(
    Guid DraftId,
    Guid? ExistingPageViewId,
    LayoutChoice Layout,
    PaperRecipe Paper,
    Guid? PageDisplayModeId,
    bool UseDedicatedDetailLayer,
    Guid? DetailLayerId,
    TitleBlockChoice TitleBlock,
    IReadOnlyList<string?> NamedViewsByDetail,
    IReadOnlyList<Guid?> DetailDisplayModesByDetail,
    IReadOnlyList<Guid?> AppearanceStatesByDetail,
    Guid? AppearanceStateId,
    IReadOnlyList<string?> OriginalNamedViewsByDetail,
    IReadOnlyList<Guid?> OriginalDetailDisplayModesByDetail,
    IReadOnlyList<Guid?> OriginalAppearanceStatesByDetail)
{
    internal LayoutCreationSpec ToSpec() => new(
        Quantity: 1,
        Paper: Paper,
        BuiltInLayout: Layout.BuiltInLayout,
        TemplateId: Layout.TemplateId,
        DetailDisplayModeId: PageDisplayModeId,
        BuiltInTitleBlock: TitleBlock.BuiltInKind,
        UseDedicatedDetailLayer: UseDedicatedDetailLayer,
        NamedViewsByDetail: NamedViewsByDetail,
        DetailDisplayModesByDetail: DetailDisplayModesByDetail,
        DetailLayerId: DetailLayerId,
        AppearanceStateId: AppearanceStateId,
        AppearanceStatesByDetail: AppearanceStatesByDetail);
}

internal enum DetailLayerTargetMode
{
    Dedicated,
    Active,
    Other,
}

internal sealed record CreationPreviewRow(
    Guid DraftId,
    LayoutGroupKey GroupKey,
    string Index,
    string Name,
    string Destination,
    string LayoutType,
    string Paper,
    string Details,
    string DetailChanges,
    string DetailLayer,
    string DisplayMode,
    string TitleBlock,
    string AppearanceState,
    PreviewChangedProperty ChangedProperties = PreviewChangedProperty.None);

[Flags]
internal enum PreviewChangedProperty
{
    None = 0,
    Name = 1 << 0,
    Destination = 1 << 1,
    Paper = 1 << 2,
    DetailLayer = 1 << 3,
    DisplayMode = 1 << 4,
    TitleBlock = 1 << 5,
    AppearanceState = 1 << 6,
    DetailAssignments = 1 << 7,
}

internal sealed record DetailPreviewState(
    string Label,
    string NamedViewLabel,
    string DisplayModeLabel,
    Bitmap? NamedViewPreview,
    string? PreviewMessage,
    bool HasNamedView,
    bool HasDisplayMode,
    bool NamedViewIsMixed,
    bool DisplayModeIsMixed,
    bool Changed);

internal sealed record PreviewAppearance(
    EffectiveViewportAppearance Effective,
    Guid FolderId,
    Guid? AppearanceStateId,
    Guid DetailSlotId);

internal sealed class DetailActivatedEventArgs(int index) : EventArgs
{
    internal int Index { get; } = index;
}

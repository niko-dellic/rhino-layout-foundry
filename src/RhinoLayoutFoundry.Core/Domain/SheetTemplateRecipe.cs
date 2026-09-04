namespace RhinoLayoutFoundry.Core.Domain;
/// <summary>Snapshot-derived planning recipe. Never persisted as a standalone template library.</summary>
public sealed record SheetTemplateRecipe(
    Guid Id,
    string Name,
    PaperRecipe Paper,
    IReadOnlyList<DetailSlotRecipe> DetailSlots,
    TitleBlockTemplateRecipe? TitleBlock,
    IReadOnlyDictionary<string, string> DefaultMetadata,
    string DefaultNamingPattern)
{

    public Guid? SourcePageViewId { get; init; }
}

public sealed record PaperRecipe(
    double Width,
    double Height,
    string UnitSystem);

public sealed record DetailSlotRecipe(
    Guid Id,
    string Name,
    double Left,
    double Bottom,
    double Right,
    double Top,
    string Projection,
    double? PageToModelRatio,
    bool ProjectionLocked,
    Guid? DisplayModeId,
    string? DefaultNamedView,
    IReadOnlyList<double>? CameraLocation = null,
    IReadOnlyList<double>? CameraTarget = null,
    IReadOnlyList<double>? CameraUp = null)
{
    [System.Text.Json.Serialization.JsonRequired]
    public IReadOnlyList<LayerVisibilityRule> LayerRules { get; init; } = [];

    [System.Text.Json.Serialization.JsonRequired]
    public IReadOnlyList<ObjectDisplayRule> ObjectDisplayRules { get; init; } = [];
}

/// <summary>Built-in title-block intent; native definitions are created by the host.</summary>
public sealed record TitleBlockTemplateRecipe(
    BuiltInTitleBlockKind BuiltInKind);

namespace RhinoLayoutFoundry.Core.Domain;

public sealed record SheetTemplateRecipe(
    Guid Id,
    int RecipeVersion,
    string Name,
    PaperRecipe Paper,
    IReadOnlyList<DetailSlotRecipe> DetailSlots,
    TitleBlockTemplateRecipe? TitleBlock,
    IReadOnlyList<string> DefaultTags,
    IReadOnlyDictionary<string, string> DefaultMetadata,
    string DefaultNamingPattern)
{
    public const int CurrentRecipeVersion = 1;

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
    IReadOnlyList<double>? CameraUp = null);

public sealed record TitleBlockTemplateRecipe(
    Guid InstanceDefinitionId,
    string InstanceDefinitionName,
    IReadOnlyList<double> Transform,
    string AnchorName,
    IReadOnlyDictionary<string, string> FieldMappings,
    BuiltInTitleBlockKind? BuiltInKind = null);

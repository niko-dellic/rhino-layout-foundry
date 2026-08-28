using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Observer;

public sealed record ObserverSnapshot(
    uint DocumentRuntimeSerialNumber,
    long Revision,
    string DocumentName,
    Guid RootFolderId,
    IReadOnlyList<ObserverFolderSnapshot> Folders,
    IReadOnlyList<ObserverSheetSnapshot> Sheets,
    ObserverCanvasState CanvasState,
    IReadOnlyList<string>? NamedViewChoices = null)
{
    public static ObserverSnapshot NoDocument { get; } = new(
        0,
        0,
        "No active document",
        Guid.Empty,
        [],
        [],
        ObserverCanvasState.Empty);

    public bool HasDocument => DocumentRuntimeSerialNumber != 0;
    public IReadOnlyList<string> NamedViews => NamedViewChoices ?? [];
}

public sealed record ObserverFolderSnapshot(
    Guid Id,
    Guid? ParentId,
    string Name,
    int Order);

public sealed record ObserverSheetSnapshot(
    Guid PageViewId,
    Guid FolderId,
    string Name,
    int Order,
    double PaperWidthMillimeters,
    double PaperHeightMillimeters,
    string PageUnitSystem,
    IReadOnlyList<ObserverDetailSnapshot> Details,
    bool IncludeInPrintAll,
    long PreviewContentVersion,
    IReadOnlyList<OverviewIssue>? Diagnostics = null)
{
    public IReadOnlyList<OverviewIssue> Issues => Diagnostics ?? [];
}

public sealed record ObserverDetailSnapshot(
    Guid DetailViewportId,
    string Name,
    ObserverRect NormalizedBounds,
    Guid DisplayModeId,
    string DisplayModeName);

public static class ObserverDetailBounds
{
    public static ObserverRect FromPageCoordinates(
        double minimumX,
        double minimumY,
        double maximumX,
        double maximumY,
        double pageWidth,
        double pageHeight)
    {
        if (pageWidth <= 0 || pageHeight <= 0 ||
            !double.IsFinite(minimumX) || !double.IsFinite(minimumY) ||
            !double.IsFinite(maximumX) || !double.IsFinite(maximumY))
            return new ObserverRect(0.05, 0.05, 0.9, 0.9);

        var left = Math.Clamp(Math.Min(minimumX, maximumX) / pageWidth, 0, 1);
        var right = Math.Clamp(Math.Max(minimumX, maximumX) / pageWidth, 0, 1);
        var bottom = Math.Clamp(Math.Min(minimumY, maximumY) / pageHeight, 0, 1);
        var top = Math.Clamp(Math.Max(minimumY, maximumY) / pageHeight, 0, 1);
        return new ObserverRect(
            left,
            1 - top,
            Math.Max(0, right - left),
            Math.Max(0, top - bottom));
    }
}

public interface IDocumentObserverSnapshotProvider
{
    ObserverSnapshot Capture();
}

public static class PaperUnitConverter
{
    public static double ToMillimeters(double value, string? unitSystem)
    {
        var factor = (unitSystem ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "microns" => 0.001,
            "millimeters" or "millimetres" => 1,
            "centimeters" or "centimetres" => 10,
            "decimeters" or "decimetres" => 100,
            "meters" or "metres" => 1000,
            "kilometers" or "kilometres" => 1_000_000,
            "microinches" => 0.0000254,
            "mils" => 0.0254,
            "inches" => 25.4,
            "feet" => 304.8,
            "yards" => 914.4,
            "miles" => 1_609_344,
            _ => 1,
        };
        return value * factor;
    }
}

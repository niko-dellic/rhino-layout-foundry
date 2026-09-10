using System.Drawing;
using Rhino.Display;

namespace RhinoLayoutFoundry.Rhino;

/// <summary>A dedicated presentation for generated sheets; never edits the user's Pen mode.</summary>
internal static class FoundryDrawingDisplayMode
{
    private const string Name = "Foundry Drawing v1";

    internal static DisplayModeDescription? GetOrCreate()
    {
        var existing = DisplayModeDescription.FindByName(Name);
        if (existing is not null) return existing;
        var id = DisplayModeDescription.CopyDisplayMode(DisplayModeDescription.PenId, Name);
        if (id == Guid.Empty) return null;
        var mode = DisplayModeDescription.GetDisplayMode(id);
        if (mode is null) return null;
        var attributes = mode.DisplayAttributes;
        // Pen inherits model section/object colors by default; pale model colors
        // can otherwise make the cut walls, slabs and stair disappear on paper.
        attributes.UseSectionStyles = false;
        attributes.ShowClippingEdges = true;
        attributes.ClippingEdgeColorUsage = DisplayPipelineAttributes.ClippingEdgeColorUse.SolidColor;
        attributes.ClippingEdgeColor = Color.Black;
        attributes.ClippingEdgeThickness = 3;
        if (DisplayModeDescription.UpdateDisplayMode(mode)) return mode;
        DisplayModeDescription.DeleteDisplayMode(id);
        return null;
    }
}

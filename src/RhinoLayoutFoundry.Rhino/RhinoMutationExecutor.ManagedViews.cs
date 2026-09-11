using Rhino;
using Rhino.DocObjects;
namespace RhinoLayoutFoundry.Rhino;
internal sealed partial class RhinoMutationExecutor
{
    internal static string ReadableViewName(RhinoDoc document, string requested)
    {
        var used = document.NamedViews.Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = requested.Trim();
        for (var suffix = 2; used.Contains(name); suffix++) name = requested.Trim() + $" ({suffix})";
        return name;
    }
    private static int EnsureClippingLayer(RhinoDoc document)
    {
        var layer = document.Layers.FirstOrDefault(l => !l.IsDeleted && l.ParentLayerId == Guid.Empty && l.Name == ".clipping-planes");
        if (layer is not null) return layer.Index;
        var index = document.Layers.Add(new Layer { Name = ".clipping-planes", Color = System.Drawing.Color.Gray });
        if (index < 0) throw new InvalidOperationException("Could not create .clipping-planes layer.");
        return index;
    }
}

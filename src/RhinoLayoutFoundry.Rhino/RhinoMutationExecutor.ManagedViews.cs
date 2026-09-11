using Rhino;
using Rhino.DocObjects;
using System.Text.Json;
using RhinoLayoutFoundry.Core.Domain;
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
    internal static IReadOnlyDictionary<Guid, string> ResolveManagedAssignments(RhinoDoc document, DocumentState state, SheetRecord? sheet)
    {
        var assignments = sheet?.DetailNamedViews.ToDictionary(p => p.Key, p => p.Value) ?? new Dictionary<Guid, string>();
        foreach (var pair in state.Metadata.Where(p => p.Key.StartsWith("RhinoLayoutFoundry.ManagedView.", StringComparison.Ordinal)))
        {
            if (!Guid.TryParse(pair.Key["RhinoLayoutFoundry.ManagedView.".Length..], out var id)) continue;
            var view = document.NamedViews.FirstOrDefault(v => v.NamedViewId == id);
            if (view is null) continue;
            try
            {
                using var json = JsonDocument.Parse(pair.Value);
                if (json.RootElement.TryGetProperty("detail_id", out var detail) && detail.TryGetGuid(out var detailId) && assignments.ContainsKey(detailId))
                    assignments[detailId] = view.Name;
            }
            catch (JsonException) { /* Legacy metadata is left intact. */ }
        }
        return assignments;
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

using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Input.Custom;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Rhino;

internal sealed class RhinoModelObjectSelectionService : IModelObjectSelectionService
{
    public ModelObjectSelectionResult PickObjects()
    {
        if (RhinoDoc.ActiveDoc is null)
            return new ModelObjectSelectionResult(false, false, [], "No active Rhino document is available.");

        using var getter = new GetObject();
        getter.SetCommandPrompt("Select objects for the Appearance State; press Enter when done");
        getter.GeometryFilter = ObjectType.AnyObject;
        getter.SubObjectSelect = false;
        getter.GroupSelect = true;
        // Require an intentional viewport pick for this workflow. Reusing Rhino's
        // current preselection can make the editor disappear and immediately
        // return before the user has had a chance to choose anything.
        getter.EnablePreSelect(false, true);
        getter.GetMultiple(1, 0);

        if (getter.CommandResult() == Result.Cancel)
            return new ModelObjectSelectionResult(false, true, [], "Object selection cancelled.");
        if (getter.CommandResult() != Result.Success)
            return new ModelObjectSelectionResult(false, false, [], "Rhino could not complete object selection.");

        var objectIds = getter.Objects()
            .Select(reference => reference.ObjectId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        return objectIds.Length == 0
            ? new ModelObjectSelectionResult(false, true, [], "No objects were selected.")
            : new ModelObjectSelectionResult(
                true,
                false,
                objectIds,
                $"Selected {objectIds.Length} object{(objectIds.Length == 1 ? string.Empty : "s")}.");
    }
}

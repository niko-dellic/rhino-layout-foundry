using Rhino;
using Rhino.DocObjects;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Rhino;

internal sealed class RhinoTemplateCaptureContextProvider : ITemplateCaptureContextProvider
{
    public TemplateCaptureContext Capture(Guid sourcePageViewId)
    {
        var document = RhinoDoc.ActiveDoc
            ?? throw new InvalidOperationException("There is no active Rhino document.");
        if (document.Views.GetPageViews().All(page => page.MainViewport.Id != sourcePageViewId))
            throw new InvalidOperationException("The source layout no longer exists.");

        var candidates = document.Objects
            .OfType<InstanceObject>()
            .Where(instance => instance.Attributes.Space == ActiveSpace.PageSpace &&
                               instance.Attributes.ViewportId == sourcePageViewId)
            .Select(instance => new TitleBlockCandidate(
                instance.Id,
                instance.InstanceDefinition.Id,
                instance.InstanceDefinition.Name))
            .OrderBy(item => item.InstanceDefinitionName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new TemplateCaptureContext(sourcePageViewId, candidates);
    }
}

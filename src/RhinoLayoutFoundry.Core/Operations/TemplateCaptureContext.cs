namespace RhinoLayoutFoundry.Core.Operations;

public sealed record TitleBlockCandidate(
    Guid InstanceObjectId,
    Guid InstanceDefinitionId,
    string InstanceDefinitionName);

public sealed record TemplateCaptureContext(
    Guid SourcePageViewId,
    IReadOnlyList<TitleBlockCandidate> TitleBlockCandidates);

public interface ITemplateCaptureContextProvider
{
    TemplateCaptureContext Capture(Guid sourcePageViewId);
}

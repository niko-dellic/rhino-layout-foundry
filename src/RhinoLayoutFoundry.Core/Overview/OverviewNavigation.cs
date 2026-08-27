namespace RhinoLayoutFoundry.Core.Overview;

public sealed record OverviewNavigationResult(bool Succeeded, string Message = "");

public interface IDocumentOverviewNavigationService
{
    OverviewNavigationResult Navigate(OverviewNavigationTarget target);
}

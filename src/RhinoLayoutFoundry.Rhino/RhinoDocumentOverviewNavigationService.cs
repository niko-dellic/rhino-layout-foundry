using Rhino;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Rhino;

internal sealed class RhinoDocumentOverviewNavigationService : IDocumentOverviewNavigationService
{
    public OverviewNavigationResult Navigate(OverviewNavigationTarget target)
    {
        var document = RhinoDoc.ActiveDoc;
        if (document is null)
        {
            return new OverviewNavigationResult(false, "No active Rhino document.");
        }

        var page = document.Views.GetPageViews()
            .FirstOrDefault(candidate => candidate.MainViewport.Id == target.SheetPageViewId);
        if (page is null)
        {
            return new OverviewNavigationResult(false, "That layout sheet no longer exists.");
        }

        document.Views.ActiveView = page;
        if (target.DetailViewportId is { } detailId)
        {
            if (!page.SetActiveDetail(detailId))
            {
                return new OverviewNavigationResult(false, "That detail viewport no longer exists.");
            }
        }
        else
        {
            page.SetPageAsActive();
        }

        page.Redraw();
        return new OverviewNavigationResult(true);
    }
}

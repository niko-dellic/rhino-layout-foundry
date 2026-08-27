namespace RhinoLayoutFoundry.Core.Overview;

public sealed record OverviewNavigationResult(bool Succeeded, string Message = "");

public enum LayoutSheetCommand
{
    NewDetail,
    Print,
    Properties,
}

public interface IDocumentOverviewNavigationService
{
    OverviewNavigationResult Navigate(OverviewNavigationTarget target);

    OverviewNavigationResult DuplicateSheet(Guid sheetPageViewId);

    OverviewNavigationResult RenameSheet(Guid sheetPageViewId, string newName);

    OverviewNavigationResult DeleteSheet(Guid sheetPageViewId);

    OverviewNavigationResult RunSheetCommand(Guid sheetPageViewId, LayoutSheetCommand command);
}

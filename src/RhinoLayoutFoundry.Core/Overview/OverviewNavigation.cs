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

public sealed record LayoutPdfExportRequest(
    uint DocumentRuntimeSerialNumber,
    IReadOnlyList<Guid> SheetPageViewIds,
    string FilePath,
    double DotsPerInch = 300);

public sealed record LayoutPdfExportResult(
    bool Succeeded,
    int PageCount,
    string Message = "");

public interface ILayoutPdfExportService
{
    Task<LayoutPdfExportResult> ExportAsync(
        LayoutPdfExportRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record LayoutPrintDialogRequest(
    uint DocumentRuntimeSerialNumber,
    IReadOnlyList<Guid> SheetPageViewIds,
    string DialogTitle);

public interface ILayoutPrintDialogService
{
    OverviewNavigationResult Show(LayoutPrintDialogRequest request);
}

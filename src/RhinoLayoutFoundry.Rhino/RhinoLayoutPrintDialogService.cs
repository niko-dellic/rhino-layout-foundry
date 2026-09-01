using System.Reflection;
using Rhino;
using Rhino.Commands;
using Rhino.UI.Forms;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Rhino;

internal sealed class RhinoLayoutPrintDialogService : ILayoutPrintDialogService
{
    private const string PrintOptionsViewModelTypeName =
        "Rhino.UI.Forms.ViewModels.PrintOptionsViewModel";
    private const string SelectedPagesPropertyName = "LayoutsPanelSelectedPages";

    public OverviewNavigationResult Show(LayoutPrintDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!RhinoApp.InvokeRequired)
        {
            return ShowOnUiThread(request);
        }

        OverviewNavigationResult? result = null;
        RhinoApp.InvokeOnUiThread((Action)(() => result = ShowOnUiThread(request)));
        return result ?? new OverviewNavigationResult(
            false,
            "Rhino could not open the print dialog.");
    }

    private static OverviewNavigationResult ShowOnUiThread(LayoutPrintDialogRequest request)
    {
        if (request.SheetPageViewIds.Count == 0)
        {
            return new OverviewNavigationResult(false, "There are no layouts in this print scope.");
        }

        var document = RhinoDoc.FromRuntimeSerialNumber(request.DocumentRuntimeSerialNumber);
        if (document is null || RhinoDoc.ActiveDoc?.RuntimeSerialNumber != request.DocumentRuntimeSerialNumber)
        {
            return new OverviewNavigationResult(
                false,
                "The target Rhino document was closed or is no longer active.");
        }

        var pagesById = document.Views.GetPageViews()
            .ToDictionary(page => page.MainViewport.Id);
        var pages = new List<global::Rhino.Display.RhinoPageView>(request.SheetPageViewIds.Count);
        foreach (var pageViewId in request.SheetPageViewIds)
        {
            if (!pagesById.TryGetValue(pageViewId, out var page))
            {
                return new OverviewNavigationResult(
                    false,
                    "One or more layouts no longer exist. Refresh and try again.");
            }

            pages.Add(page);
        }

        var plugin = LayoutFoundryPlugin.Instance;
        if (plugin is null)
        {
            return new OverviewNavigationResult(false, "The Layout Foundry plug-in is unavailable.");
        }

        var selectionProperty = typeof(PrintDialogUi).Assembly
            .GetType(PrintOptionsViewModelTypeName, throwOnError: false)
            ?.GetProperty(
                SelectedPagesPropertyName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (selectionProperty?.CanRead != true || selectionProperty.CanWrite != true ||
            selectionProperty.PropertyType != typeof(List<uint>))
        {
            return new OverviewNavigationResult(
                false,
                "This Rhino version does not expose the native multi-layout print selection bridge.");
        }

        var previousSelection = (selectionProperty.GetValue(null) as List<uint>)?.ToList();
        var previousActiveView = document.Views.ActiveView;
        try
        {
            selectionProperty.SetValue(
                null,
                pages.Select(page => page.RuntimeSerialNumber).ToList());

            document.Views.ActiveView = pages[0];
            pages[0].SetPageAsActive();
            var result = PrintDialogUi.ShowPrintDialog(
                request.DialogTitle,
                request.DocumentRuntimeSerialNumber,
                plugin.Settings,
                selectedObjectsOnly: false,
                showPrinterDestinations: true);
            return result == Result.Failure
                ? new OverviewNavigationResult(false, "Rhino could not open the print dialog.")
                : new OverviewNavigationResult(true);
        }
        catch (Exception exception)
        {
            return new OverviewNavigationResult(
                false,
                $"Rhino could not configure the print dialog: {exception.Message}");
        }
        finally
        {
            try
            {
                selectionProperty.SetValue(null, previousSelection ?? []);
                if (previousActiveView is not null &&
                    RhinoDoc.ActiveDoc?.RuntimeSerialNumber == document.RuntimeSerialNumber)
                {
                    document.Views.ActiveView = previousActiveView;
                }
            }
            catch
            {
                // The print dialog already closed; failure to restore transient
                // Rhino UI state must not turn a completed print into an error.
            }
        }
    }
}

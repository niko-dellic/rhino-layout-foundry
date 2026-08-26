using Rhino;
using Rhino.Commands;
using Rhino.UI;
using RhinoLayoutFoundry.UI;

namespace RhinoLayoutFoundry.Rhino;

public sealed class LayoutFoundryCommand : Command
{
    public override string EnglishName => "LayoutFoundry";

    protected override Result RunCommand(RhinoDoc document, RunMode mode)
    {
        var panelId = typeof(LayoutFoundryPanel).GUID;

        try
        {
            Panels.OpenPanel(panelId, true);
            LayoutFoundryUiHost.NotifyOverviewChanged();

            var isVisible = Panels.IsPanelVisible(panelId);
            var panel = Panels.GetPanel(panelId, document);
            RhinoApp.WriteLine(
                "Layout Foundry panel {0}; Eto instance {1}.",
                isVisible ? "opened" : "did not become visible",
                panel is LayoutFoundryPanel ? "created" : "was not created");
            return isVisible && panel is LayoutFoundryPanel
                ? Result.Success
                : Result.Failure;
        }
        catch (Exception exception)
        {
            RhinoApp.WriteLine("Layout Foundry could not open: {0}", exception);
            return Result.Failure;
        }
    }
}

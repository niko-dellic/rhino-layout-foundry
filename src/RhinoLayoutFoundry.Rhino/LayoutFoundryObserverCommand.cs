using Rhino;
using Rhino.Commands;
using Rhino.UI;
using RhinoLayoutFoundry.UI;

namespace RhinoLayoutFoundry.Rhino;

public sealed class LayoutFoundryObserverCommand : Command
{
    public override string EnglishName => "LayoutFoundryObserver";

    protected override Result RunCommand(RhinoDoc document, RunMode mode)
    {
        var panelId = typeof(LayoutFoundryPanel).GUID;
        try
        {
            Panels.OpenPanel(panelId, true);
            LayoutFoundryUiHost.NotifyOverviewChanged();
            var visible = Panels.IsPanelVisible(panelId);
            var panel = Panels.GetPanel(panelId, document) as LayoutFoundryPanel;
            panel?.ShowCanvasView();
            RhinoApp.WriteLine(
                visible && panel is not null
                    ? "Layout Foundry opened in Canvas view."
                    : "Layout Foundry Canvas view did not become visible.");
            return visible && panel is not null ? Result.Success : Result.Failure;
        }
        catch (Exception exception)
        {
            RhinoApp.WriteLine("Layout Foundry Canvas view could not open: {0}", exception);
            return Result.Failure;
        }
    }
}

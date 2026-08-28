using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

internal static class FoundryDialogActions
{
    internal static void Bind(
        Dialog dialog,
        FoundryDialogButton? accept,
        FoundryDialogButton? cancel)
    {
        dialog.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Handled) return;
            if (eventArgs.Key == Keys.Escape && cancel is not null)
            {
                cancel.PerformClick();
                eventArgs.Handled = true;
                return;
            }
            if (eventArgs.Key == Keys.Enter && accept?.Enabled == true)
            {
                accept.PerformClick();
                eventArgs.Handled = true;
            }
        };
    }
}

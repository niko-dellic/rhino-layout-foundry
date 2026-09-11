using System.Text.Json;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Overview;
using RhinoLayoutFoundry.Extensibility;
namespace RhinoLayoutFoundry.UI;
internal sealed partial class LayoutFoundryWorkspace
{
    private async Task ReviewAnnotationsAsync()
    {
        var host = FoundryAutomation.Current; if (host is null) return;
        var snapshot = host.CaptureSnapshot(); var keys = SelectedKeys().ToArray();
        var ids = snapshot.Sheets.Values.Where(s => keys.Any(k => k.Kind == OverviewNodeKind.Sheet && k.Id == s.PageViewId ||
            k.Kind == OverviewNodeKind.Detail && s.DetailIds.Contains(k.Id))).Select(s => s.PageViewId).ToHashSet();
        using var inventory = JsonDocument.Parse(host.InspectDrawingGeometry([Guid.Empty]));
        if (!inventory.RootElement.TryGetProperty("annotations", out var items)) return;
        var rows = items.EnumerateArray().Where(a => ids.Contains(a.GetProperty("sheet_id").GetGuid())).Select(a => a.Clone()).ToArray();
        if (rows.Length == 0) { _statusLabel.Text = "No annotations on the selected sheets."; return; }
        var list = new ListBox { Height = 240, DataStore = rows.Select(a =>
            (a.GetProperty("warning").GetString() ?? "Linked / editable annotation") + " · " + a.GetProperty("id").GetGuid().ToString()[..8]).ToArray(), SelectedIndex = 0 };
        var dialog = new Dialog { Title = "Sheet annotations", Padding = FoundryTheme.Space4, Resizable = true };
        var select = new FoundryDialogButton("Select", FoundryDialogButtonStyle.Secondary, 80);
        var repair = new FoundryDialogButton("Repair…", FoundryDialogButtonStyle.Secondary, 90);
        var remove = new FoundryDialogButton("Remove…", FoundryDialogButtonStyle.Secondary, 90);
        var close = new FoundryDialogButton("Close", FoundryDialogButtonStyle.Secondary, 80);
        string? action = null;
        select.Click += (_, _) => { action = "select"; dialog.Close(); };
        repair.Click += (_, _) => { action = "repair"; dialog.Close(); };
        remove.Click += (_, _) => { action = "remove"; dialog.Close(); };
        close.Click += (_, _) => dialog.Close();
        dialog.Content = new StackLayout { Spacing = FoundryTheme.Space2, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items = { FoundryTheme.MutedLabel("Unresolved annotations retain their last value until repaired or removed."), list,
                new StackLayout { Orientation = Orientation.Horizontal, Spacing = FoundryTheme.Space2, Items = { select, repair, remove, close } } } };
        dialog.ShowModal(this);
        if (action is null || list.SelectedIndex < 0) return;
        var row = rows[list.SelectedIndex]; var id = row.GetProperty("id").GetGuid(); var sheetId = row.GetProperty("sheet_id").GetGuid();
        try
        {
            if (action == "select") { host.SelectAnnotation(id); return; }
            SheetAnnotationSpecification[] annotations = [];
            if (action == "repair")
            {
                var replacement = host.PickAnnotationReferences(id); if (replacement is null) return;
                annotations = [JsonSerializer.Deserialize<SheetAnnotationSpecification>(replacement, SheetUpdatePlanner.Json)!];
            }
            snapshot = host.CaptureSnapshot();
            var spec = new SheetUpdateSpecification(Guid.NewGuid(), snapshot.DocumentRuntimeSerialNumber, snapshot.Revision,
                [new(sheetId, null, null, [], annotations, action == "remove" ? [id] : [])], []);
            var plan = SheetUpdatePlanner.Plan(spec, snapshot);
            if (!plan.CanApply) { _statusLabel.Text = string.Join(" ", plan.Diagnostics.Select(d => d.Message)); return; }
            if (MessageBox.Show(this, action == "remove" ? "Remove the selected annotation?" : "Apply the selected replacement geometry references?",
                "Review annotation change", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            var staged = host.StagePlan(plan);
            var result = await host.ApplyApprovedPlanAsync(host.ApprovePlan(staged.PlanId), CancellationToken.None);
            _statusLabel.Text = string.Join(" ", result.Diagnostics.Select(d => d.Message)); RefreshOverview();
        }
        catch (Exception exception) { _statusLabel.Text = exception.Message; }
    }
}

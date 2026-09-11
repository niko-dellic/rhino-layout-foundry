using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;
using RhinoLayoutFoundry.Extensibility;

namespace RhinoLayoutFoundry.UI;

internal sealed partial class LayoutFoundryWorkspace
{
    private async Task EditDetailCaptionsAsync()
    {
        var keys = SelectedKeys().ToArray();
        var host = FoundryAutomation.Current;
        if (keys.Length != 1 || host is null) return;
        var key = keys[0];
        var kind = key.Kind switch { OverviewNodeKind.Folder => HierarchyScopeKind.Folder,
            OverviewNodeKind.Sheet => HierarchyScopeKind.Sheet, OverviewNodeKind.Detail => HierarchyScopeKind.Detail,
            _ => (HierarchyScopeKind?)null };
        if (kind is null) return;
        var snapshot = host.CaptureSnapshot();
        var scope = new HierarchyScope(kind.Value, key.Id);
        var sheet = kind == HierarchyScopeKind.Detail ? snapshot.Sheets.Values.FirstOrDefault(s => s.Details.Any(d => d.DetailViewportId == key.Id))
            : kind == HierarchyScopeKind.Sheet ? snapshot.Sheets.GetValueOrDefault(key.Id) : null;
        var folder = kind == HierarchyScopeKind.Folder ? key.Id : sheet?.FolderId ?? snapshot.RootFolderId;
        var settings = DetailCaptions.Read(snapshot.Metadata, scope);
        var inherited = DetailCaptions.Resolve(DetailCaptions.Write(snapshot.Metadata, scope, new()), snapshot.Folders,
            folder, sheet?.PageViewId, kind == HierarchyScopeKind.Detail ? key.Id : null);
        var parallel = kind != HierarchyScopeKind.Detail || sheet?.Details.FirstOrDefault(d => d.DetailViewportId == key.Id)?.IsParallelProjection == true;
        var dialog = new DetailCaptionsDialog(settings, inherited, parallel);
        dialog.ShowModal(this);
        if (!dialog.Accepted) return;
        var plan = new SetDetailCaptionsPlanner().Plan(new(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision, scope, dialog.Settings), snapshot);
        var staged = host.StagePlan(plan);
        var result = await host.ApplyApprovedPlanAsync(host.ApprovePlan(staged.PlanId), CancellationToken.None);
        _statusLabel.Text = result.Succeeded ? "Detail captions updated." : DiagnosticMessage(result);
        if (result.Succeeded && host.CaptureSnapshot().Sheets.Values.SelectMany(s => s.Details).Any(d => d.CaptionWarning is not null))
            _statusLabel.Text += " Some captions exceed their detail width or page boundary.";
        RefreshOverview();
    }
}

using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

public static class LayoutFoundryUiHost
{
    private static IDocumentOverviewProvider? _overviewProvider;
    private static IDocumentSnapshotProvider? _snapshotProvider;
    private static IDocumentMutationService? _mutationService;
    private static EventHandler? _overviewChanged;

    public static event EventHandler OverviewChanged
    {
        add => _overviewChanged += value;
        remove => _overviewChanged -= value;
    }

    public static void Configure(
        IDocumentOverviewProvider overviewProvider,
        IDocumentSnapshotProvider snapshotProvider,
        IDocumentMutationService mutationService)
    {
        _overviewProvider = overviewProvider ?? throw new ArgumentNullException(nameof(overviewProvider));
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
        _mutationService = mutationService ?? throw new ArgumentNullException(nameof(mutationService));
        NotifyOverviewChanged();
    }

    public static DocumentOverview CaptureOverview()
    {
        return _overviewProvider?.Capture() ?? DocumentOverview.NoDocument;
    }

    public static void NotifyOverviewChanged()
    {
        _overviewChanged?.Invoke(null, EventArgs.Empty);
    }

    public static async Task<OperationResult> RenameSheetAsync(
        Guid pageViewId,
        string expectedName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        if (_snapshotProvider is null || _mutationService is null)
        {
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");
        }

        try
        {
            var snapshot = _snapshotProvider.Capture();
            var request = new RenameSheetRequest(
                snapshot.DocumentRuntimeSerialNumber,
                snapshot.Revision,
                pageViewId,
                expectedName,
                newName);
            var plan = new RenameSheetPlanner().Plan(request, snapshot);
            if (!plan.CanApply)
            {
                return new OperationResult(false, plan.Diagnostics);
            }

            var result = await _mutationService.ApplyAsync(plan, cancellationToken);
            if (result.Succeeded)
            {
                NotifyOverviewChanged();
            }

            return result;
        }
        catch (InvalidOperationException exception)
        {
            return UnavailableResult(exception.Message);
        }
    }

    public static void Reset()
    {
        _overviewProvider = null;
        _snapshotProvider = null;
        _mutationService = null;
        _overviewChanged = null;
    }

    private static OperationResult UnavailableResult(string message)
    {
        return new OperationResult(
            false,
            [new Diagnostic("ui.unavailable", DiagnosticSeverity.Error, message)]);
    }
}

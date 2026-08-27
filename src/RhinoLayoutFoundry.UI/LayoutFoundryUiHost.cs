using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

public static class LayoutFoundryUiHost
{
    private static IDocumentOverviewProvider? _overviewProvider;
    private static IDocumentSnapshotProvider? _snapshotProvider;
    private static IDocumentMutationService? _mutationService;
    private static IDocumentOverviewNavigationService? _navigationService;
    private static EventHandler? _overviewChanged;

    public static event EventHandler OverviewChanged
    {
        add => _overviewChanged += value;
        remove => _overviewChanged -= value;
    }

    public static void Configure(
        IDocumentOverviewProvider overviewProvider,
        IDocumentSnapshotProvider snapshotProvider,
        IDocumentMutationService mutationService,
        IDocumentOverviewNavigationService navigationService)
    {
        _overviewProvider = overviewProvider ?? throw new ArgumentNullException(nameof(overviewProvider));
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
        _mutationService = mutationService ?? throw new ArgumentNullException(nameof(mutationService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        NotifyOverviewChanged();
    }

    public static DocumentOverview CaptureOverview()
    {
        return _overviewProvider?.Capture() ?? DocumentOverview.NoDocument;
    }

    public static DocumentOverviewIdentity CaptureOverviewIdentity()
    {
        return _overviewProvider?.CaptureIdentity() ?? new DocumentOverviewIdentity(null, 0);
    }

    public static void NotifyOverviewChanged()
    {
        _overviewChanged?.Invoke(null, EventArgs.Empty);
    }

    public static OverviewNavigationResult Navigate(OverviewNavigationTarget target)
    {
        return _navigationService?.Navigate(target) ??
               new OverviewNavigationResult(false, "Foundry is not connected to an active Rhino plug-in.");
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
        _navigationService = null;
        _overviewChanged = null;
    }

    private static OperationResult UnavailableResult(string message)
    {
        return new OperationResult(
            false,
            [new Diagnostic("ui.unavailable", DiagnosticSeverity.Error, message)]);
    }
}

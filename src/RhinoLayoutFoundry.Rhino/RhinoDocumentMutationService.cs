using Rhino;
using Rhino.Commands;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Observer;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Rhino;

internal sealed class RhinoDocumentMutationService : IDocumentMutationService
{
    private readonly RhinoMutationExecutor _executor;
    private readonly DocumentRevisionTracker _revisionTracker;
    private readonly DocumentStateStore _stateStore;

    public RhinoDocumentMutationService(
        DocumentRevisionTracker revisionTracker,
        DocumentStateStore stateStore,
        Action<OverviewInvalidation> overviewChanged)
    {
        _executor = new RhinoMutationExecutor(revisionTracker, stateStore, overviewChanged);
        _revisionTracker = revisionTracker ?? throw new ArgumentNullException(nameof(revisionTracker));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        ArgumentNullException.ThrowIfNull(overviewChanged);
    }

    public Task<OperationResult> ApplyAsync(
        OperationPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!RhinoApp.InvokeRequired)
        {
            return Task.FromResult(ApplyOnUiThread(plan, cancellationToken));
        }

        var completion = new TaskCompletionSource<OperationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        RhinoApp.InvokeOnUiThread((Action)(() =>
        {
            try
            {
                completion.SetResult(ApplyOnUiThread(plan, cancellationToken));
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }));
        return completion.Task;
    }

    private OperationResult ApplyOnUiThread(
        OperationPlan plan,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Failure("operation.cancelled", "The operation was cancelled before it changed Rhino.");
        }

        if (!plan.CanApply)
        {
            return new OperationResult(false, plan.Diagnostics);
        }

        var document = RhinoDoc.FromRuntimeSerialNumber(plan.DocumentRuntimeSerialNumber);
        if (document is null || RhinoDoc.ActiveDoc?.RuntimeSerialNumber != plan.DocumentRuntimeSerialNumber)
        {
            return Failure(
                "operation.document_unavailable",
                "The target Rhino document was closed or is no longer active.");
        }

        if (!_stateStore.CanWrite(document))
            return Failure("metadata.protected", _stateStore.Diagnostic(document)!);

        if (_revisionTracker.Current(document) != plan.SourceRevision)
        {
            return Failure(
                "operation.stale_revision",
                "The Rhino document changed before Apply. Refresh and try again.");
        }

        return _executor.Apply(document, plan);
    }

    private static OperationResult Failure(string code, string message) =>
        new(false, [new Diagnostic(code, DiagnosticSeverity.Error, message)]);
}

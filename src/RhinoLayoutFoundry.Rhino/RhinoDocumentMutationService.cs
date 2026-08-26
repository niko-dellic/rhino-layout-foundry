using Rhino;
using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Rhino;

internal sealed class RhinoDocumentMutationService : IDocumentMutationService
{
    private readonly DocumentRevisionTracker _revisionTracker;

    public RhinoDocumentMutationService(DocumentRevisionTracker revisionTracker)
    {
        _revisionTracker = revisionTracker ?? throw new ArgumentNullException(nameof(revisionTracker));
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

        if (plan.Changes is not [RenameSheetChange rename])
        {
            return Failure("operation.unsupported_plan", "The operation plan is not supported by this build.");
        }

        var document = RhinoDoc.FromRuntimeSerialNumber(plan.DocumentRuntimeSerialNumber);
        if (document is null || RhinoDoc.ActiveDoc?.RuntimeSerialNumber != plan.DocumentRuntimeSerialNumber)
        {
            return Failure(
                "operation.document_unavailable",
                "The target Rhino document was closed or is no longer active.");
        }

        if (_revisionTracker.Current(document) != plan.SourceRevision)
        {
            return Failure(
                "operation.stale_revision",
                "The Rhino document changed before Apply. Refresh and try again.");
        }

        var pages = document.Views.GetPageViews();
        var page = pages.FirstOrDefault(candidate => candidate.MainViewport.Id == rename.PageViewId);
        if (page is null)
        {
            return Failure("operation.sheet_missing", "The target layout sheet no longer exists.");
        }

        if (!string.Equals(page.PageName, rename.ExpectedName, StringComparison.Ordinal))
        {
            return Failure(
                "operation.before_value_changed",
                $"The layout is now named '{page.PageName}', so the staged rename was not applied.");
        }

        if (pages.Any(candidate =>
                candidate.MainViewport.Id != rename.PageViewId &&
                string.Equals(candidate.PageName, rename.NewName, StringComparison.OrdinalIgnoreCase)))
        {
            return Failure(
                "operation.duplicate_name",
                $"Another layout is already named '{rename.NewName}'.");
        }

        var undoRecord = document.BeginUndoRecord(plan.UndoDescription);
        if (undoRecord == 0)
        {
            return Failure(
                "operation.undo_unavailable",
                "Rhino could not start a dedicated undo record, so no change was made.");
        }

        var beforeName = page.PageName;
        try
        {
            page.PageName = rename.NewName;
            if (!string.Equals(page.PageName, rename.NewName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Rhino did not retain the requested layout name.");
            }

            _revisionTracker.Bump(document);
            document.Views.Redraw();
            return new OperationResult(true, plan.Diagnostics);
        }
        catch (Exception exception)
        {
            if (!string.Equals(page.PageName, beforeName, StringComparison.Ordinal))
            {
                page.PageName = beforeName;
            }

            return Failure(
                "operation.apply_failed",
                $"The rename failed and the original name was restored: {exception.Message}");
        }
        finally
        {
            document.EndUndoRecord(undoRecord);
        }
    }

    private static OperationResult Failure(string code, string message)
    {
        return new OperationResult(
            false,
            [new Diagnostic(code, DiagnosticSeverity.Error, message)]);
    }
}

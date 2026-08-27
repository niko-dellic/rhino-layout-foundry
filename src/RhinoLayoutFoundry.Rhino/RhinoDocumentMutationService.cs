using Rhino;
using Rhino.Commands;
using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Rhino;

internal sealed class RhinoDocumentMutationService : IDocumentMutationService
{
    private readonly DocumentRevisionTracker _revisionTracker;
    private readonly DocumentStateStore _stateStore;
    private readonly Action<OverviewInvalidation> _overviewChanged;

    public RhinoDocumentMutationService(
        DocumentRevisionTracker revisionTracker,
        DocumentStateStore stateStore,
        Action<OverviewInvalidation> overviewChanged)
    {
        _revisionTracker = revisionTracker ?? throw new ArgumentNullException(nameof(revisionTracker));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _overviewChanged = overviewChanged ?? throw new ArgumentNullException(nameof(overviewChanged));
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

        if (_revisionTracker.Current(document) != plan.SourceRevision)
        {
            return Failure(
                "operation.stale_revision",
                "The Rhino document changed before Apply. Refresh and try again.");
        }

        return plan.Changes switch
        {
            [RenameSheetChange rename] => ApplyRename(document, plan, rename),
            [CreateSheetChange create] => ApplyCreateSheet(document, plan, create),
            _ when plan.Changes.All(IsDocumentStateChange) => ApplyDocumentStateChanges(document, plan),
            _ => Failure("operation.unsupported_plan", "The operation plan is not supported by this build."),
        };
    }

    private static bool IsDocumentStateChange(OperationChange change)
    {
        return change is AddFolderChange or RenameFolderChange or DeleteFolderChange or
            MoveSheetChange or MoveFolderChange;
    }

    private OperationResult ApplyRename(
        RhinoDoc document,
        OperationPlan plan,
        RenameSheetChange rename)
    {
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

    private OperationResult ApplyDocumentStateChanges(
        RhinoDoc document,
        OperationPlan plan)
    {
        var beforeState = _stateStore.Get(document);
        var folders = beforeState.Folders.ToList();
        var sheets = beforeState.Sheets.ToDictionary(pair => pair.Key, pair => pair.Value);
        var pageIds = document.Views.GetPageViews()
            .Select(page => page.MainViewport.Id)
            .ToHashSet();

        foreach (var change in plan.Changes)
        {
            var failure = change switch
            {
                AddFolderChange addFolder => ApplyAddFolder(folders, addFolder),
                RenameFolderChange renameFolder => ApplyRenameFolder(folders, renameFolder),
                DeleteFolderChange deleteFolder => ApplyDeleteFolder(folders, sheets, deleteFolder),
                MoveSheetChange moveSheet => ApplyMoveSheet(
                    beforeState.RootFolderId,
                    folders,
                    sheets,
                    pageIds,
                    moveSheet),
                MoveFolderChange moveFolder => ApplyMoveFolder(
                    beforeState.RootFolderId,
                    folders,
                    moveFolder),
                _ => Failure("operation.unsupported_plan", "The hierarchy operation is not supported."),
            };
            if (failure is not null)
            {
                return failure;
            }
        }

        var afterState = beforeState with
        {
            Folders = folders.ToArray(),
            Sheets = sheets,
        };
        var undoRecord = document.BeginUndoRecord(plan.UndoDescription);
        if (undoRecord == 0)
        {
            return Failure(
                "operation.undo_unavailable",
                "Rhino could not start a dedicated undo record, so no hierarchy changes were made.");
        }

        try
        {
            var undoEvent = document.AddCustomUndoEvent(
                plan.UndoDescription,
                OnUndoDocumentState,
                new DocumentStateUndoTag(plan.UndoDescription, beforeState));
            if (!undoEvent)
            {
                return Failure(
                    "operation.undo_unavailable",
                    "Rhino could not register hierarchy metadata with Undo, so no change was made.");
            }

            _stateStore.Set(document, afterState);
            document.Modified = true;
            _revisionTracker.Bump(document);
            return new OperationResult(true, plan.Diagnostics);
        }
        catch (Exception exception)
        {
            _stateStore.Set(document, beforeState);
            return Failure(
                "operation.apply_failed",
                $"The hierarchy change failed and the previous state was restored: {exception.Message}");
        }
        finally
        {
            document.EndUndoRecord(undoRecord);
        }
    }

    private static OperationResult? ApplyAddFolder(
        List<FolderRecord> folders,
        AddFolderChange addFolder)
    {
        if (folders.Any(folder => folder.Id == addFolder.FolderId))
        {
            return Failure("folder.already_exists", "The folder already exists.");
        }

        if (folders.All(folder => folder.Id != addFolder.ParentFolderId))
        {
            return Failure("folder.parent_missing", "The destination folder no longer exists.");
        }

        if (folders.Any(folder =>
                folder.ParentId == addFolder.ParentFolderId &&
                string.Equals(folder.Name, addFolder.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return Failure(
                "folder.duplicate_name",
                $"A folder named '{addFolder.Name}' already exists in this location.");
        }

        folders.Add(new FolderRecord(
            addFolder.FolderId,
            addFolder.ParentFolderId,
            addFolder.Name,
            addFolder.Order));
        return null;
    }

    private static OperationResult? ApplyRenameFolder(
        List<FolderRecord> folders,
        RenameFolderChange renameFolder)
    {
        var index = folders.FindIndex(folder => folder.Id == renameFolder.FolderId);
        if (index < 0)
        {
            return Failure("folder.missing", "The folder no longer exists.");
        }

        var current = folders[index];
        if (current.ParentId != renameFolder.ParentFolderId ||
            !string.Equals(current.Name, renameFolder.ExpectedName, StringComparison.Ordinal))
        {
            return Failure("folder.before_value_changed", "The folder changed before the rename was applied.");
        }

        if (folders.Any(folder =>
                folder.Id != current.Id &&
                folder.ParentId == current.ParentId &&
                string.Equals(folder.Name, renameFolder.NewName, StringComparison.OrdinalIgnoreCase)))
        {
            return Failure(
                "folder.duplicate_name",
                $"A folder named '{renameFolder.NewName}' already exists in this location.");
        }

        folders[index] = current with { Name = renameFolder.NewName };
        return null;
    }

    private static OperationResult? ApplyDeleteFolder(
        List<FolderRecord> folders,
        IReadOnlyDictionary<Guid, SheetRecord> sheets,
        DeleteFolderChange deleteFolder)
    {
        var current = folders.FirstOrDefault(folder => folder.Id == deleteFolder.FolderId);
        if (current is null)
        {
            return Failure("folder.missing", "The folder no longer exists.");
        }

        if (current.ParentId != deleteFolder.ParentFolderId ||
            !string.Equals(current.Name, deleteFolder.ExpectedName, StringComparison.Ordinal))
        {
            return Failure("folder.before_value_changed", "The folder changed before deletion was applied.");
        }

        if (folders.Any(folder => folder.ParentId == deleteFolder.FolderId) ||
            sheets.Values.Any(sheet => sheet.FolderId == deleteFolder.FolderId))
        {
            return Failure("folder.not_empty", "Only an empty folder can be deleted.");
        }

        folders.Remove(current);
        return null;
    }

    private static OperationResult? ApplyMoveSheet(
        Guid rootFolderId,
        IReadOnlyList<FolderRecord> folders,
        IDictionary<Guid, SheetRecord> sheets,
        IReadOnlySet<Guid> pageIds,
        MoveSheetChange moveSheet)
    {
        if (!pageIds.Contains(moveSheet.PageViewId))
        {
            return Failure("move.sheet_missing", "A selected layout sheet no longer exists.");
        }

        if (folders.All(folder => folder.Id != moveSheet.DestinationFolderId))
        {
            return Failure("move.destination_missing", "The destination folder no longer exists.");
        }

        sheets.TryGetValue(moveSheet.PageViewId, out var current);
        var currentFolderId = current is not null && folders.Any(folder => folder.Id == current.FolderId)
            ? current.FolderId
            : rootFolderId;
        if (currentFolderId != moveSheet.ExpectedFolderId)
        {
            return Failure("move.before_value_changed", "A selected sheet moved before this operation was applied.");
        }

        sheets[moveSheet.PageViewId] = current is null
            ? new SheetRecord(
                moveSheet.PageViewId,
                moveSheet.DestinationFolderId,
                moveSheet.Order,
                [],
                new Dictionary<string, string>(StringComparer.Ordinal),
                null)
            : current with
            {
                FolderId = moveSheet.DestinationFolderId,
                Order = moveSheet.Order,
            };
        return null;
    }

    private static OperationResult? ApplyMoveFolder(
        Guid rootFolderId,
        List<FolderRecord> folders,
        MoveFolderChange moveFolder)
    {
        var index = folders.FindIndex(folder => folder.Id == moveFolder.FolderId);
        if (index < 0 || moveFolder.FolderId == rootFolderId)
        {
            return Failure("move.folder_missing", "A selected folder no longer exists.");
        }

        if (folders.All(folder => folder.Id != moveFolder.DestinationFolderId))
        {
            return Failure("move.destination_missing", "The destination folder no longer exists.");
        }

        var current = folders[index];
        if (current.ParentId != moveFolder.ExpectedParentFolderId)
        {
            return Failure("move.before_value_changed", "A selected folder moved before this operation was applied.");
        }

        var descendants = new HashSet<Guid> { current.Id };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var child in folders.Where(folder =>
                         folder.ParentId is { } parentId && descendants.Contains(parentId)))
            {
                changed |= descendants.Add(child.Id);
            }
        }

        if (descendants.Contains(moveFolder.DestinationFolderId))
        {
            return Failure("move.folder_cycle", "A folder cannot be moved inside itself or one of its descendants.");
        }

        folders[index] = current with
        {
            ParentId = moveFolder.DestinationFolderId,
            Order = moveFolder.Order,
        };
        return null;
    }

    private OperationResult ApplyCreateSheet(
        RhinoDoc document,
        OperationPlan plan,
        CreateSheetChange create)
    {
        var beforeState = _stateStore.Get(document);
        if (beforeState.Folders.All(folder => folder.Id != create.DestinationFolderId))
        {
            return Failure("sheet.destination_missing", "The destination folder no longer exists.");
        }

        if (document.Views.GetPageViews().Any(page =>
                string.Equals(page.PageName, create.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return Failure("sheet.duplicate_name", $"A layout named '{create.Name}' already exists.");
        }

        global::Rhino.Display.RhinoPageView? page = null;
        try
        {
            page = document.Views.AddPageView(create.Name);
            if (page is null)
            {
                throw new InvalidOperationException("Rhino did not create the layout.");
            }

            var sheets = beforeState.Sheets.ToDictionary(pair => pair.Key, pair => pair.Value);
            sheets[page.MainViewport.Id] = new SheetRecord(
                page.MainViewport.Id,
                create.DestinationFolderId,
                create.Order,
                [],
                new Dictionary<string, string>(StringComparer.Ordinal),
                null);
            var afterState = beforeState with { Sheets = sheets };
            _stateStore.Set(document, afterState);
            document.Modified = true;
            _revisionTracker.Bump(document);
            document.Views.Redraw();
            return new OperationResult(true, plan.Diagnostics);
        }
        catch (Exception exception)
        {
            _stateStore.Set(document, beforeState);
            page?.Close();
            return Failure(
                "operation.apply_failed",
                $"The layout creation failed and its metadata was restored: {exception.Message}");
        }
    }

    private void OnUndoDocumentState(object? sender, CustomUndoEventArgs eventArgs)
    {
        if (eventArgs.Tag is not DocumentStateUndoTag tag)
        {
            return;
        }

        var document = eventArgs.Document;
        var currentState = _stateStore.Get(document);
        document.AddCustomUndoEvent(
            tag.Description,
            OnUndoDocumentState,
            new DocumentStateUndoTag(tag.Description, currentState));
        _stateStore.Set(document, tag.State);
        _revisionTracker.Bump(document);
        _overviewChanged(new OverviewInvalidation(
            document.RuntimeSerialNumber,
            OverviewInvalidationKind.Hierarchy |
            OverviewInvalidationKind.Metadata |
            OverviewInvalidationKind.Diagnostics));
    }

    private static OperationResult Failure(string code, string message)
    {
        return new OperationResult(
            false,
            [new Diagnostic(code, DiagnosticSeverity.Error, message)]);
    }

    private sealed record DocumentStateUndoTag(string Description, DocumentState State);
}

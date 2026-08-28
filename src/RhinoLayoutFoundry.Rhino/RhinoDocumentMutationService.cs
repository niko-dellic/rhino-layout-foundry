using Rhino;
using Rhino.Commands;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Rhino;

internal sealed class RhinoDocumentMutationService : IDocumentMutationService
{
    private const string DedicatedDetailLayerName = ".details";
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
            [BatchUpdateSheetsChange update] => ApplyBatchUpdate(document, plan, update),
            [UpdateDetailDisplayModesChange updateDetails] => ApplyDetailDisplayModes(document, plan, updateDetails),
            [AssignNamedViewToDetailsChange assignNamedView] => ApplyNamedView(document, plan, assignNamedView),
            [CaptureSheetTemplateChange capture] => ApplyCaptureTemplate(document, plan, capture),
            _ when plan.Changes.Count > 0 && plan.Changes.All(change => change is DeleteSheetTemplateChange) =>
                ApplyDeleteTemplates(document, plan, plan.Changes.Cast<DeleteSheetTemplateChange>().ToArray()),
            _ when plan.Changes.All(change => change is DeleteFolderChange or DeleteSheetChange) =>
                ApplyDeleteHierarchySelection(document, plan),
            _ when plan.Changes.All(change => change is DuplicateFolderChange or DuplicateSheetChange) =>
                ApplyDuplicateHierarchySelection(document, plan),
            _ when plan.Changes.All(change => change is CreateSheetFromTemplateChange) =>
                ApplyTemplateBatch(document, plan, plan.Changes.Cast<CreateSheetFromTemplateChange>().ToArray()),
            _ when plan.Changes.All(IsDocumentStateChange) => ApplyDocumentStateChanges(document, plan),
            _ => Failure("operation.unsupported_plan", "The operation plan is not supported by this build."),
        };
    }

    private OperationResult ApplyNamedView(
        RhinoDoc document,
        OperationPlan plan,
        AssignNamedViewToDetailsChange change)
    {
        var namedViewIndex = document.NamedViews.FindByName(change.NamedViewName);
        if (namedViewIndex < 0)
            return Failure("named_view.missing", "The selected Rhino named view no longer exists.");
        var details = document.Views.GetPageViews()
            .SelectMany(page => page.GetDetailViews())
            .Where(detail => change.DetailViewportIds.Contains(detail.Viewport.Id))
            .ToArray();
        if (details.Length != change.DetailViewportIds.Distinct().Count())
            return Failure("named_view.detail_missing", "A targeted detail viewport no longer exists.");

        var before = details.ToDictionary(
            detail => detail.Viewport.Id,
            detail => new ViewportInfo(detail.Viewport));
        var undoRecord = document.BeginUndoRecord(plan.UndoDescription);
        if (undoRecord == 0)
            return Failure("operation.undo_unavailable", "Rhino could not start a dedicated undo record.");
        try
        {
            foreach (var detail in details)
            {
                document.NamedViews.Restore(namedViewIndex, detail.Viewport);
                detail.CommitViewportChanges();
            }

            _revisionTracker.Bump(document);
            document.Views.Redraw();
            return new OperationResult(true, plan.Diagnostics);
        }
        catch (Exception exception)
        {
            foreach (var detail in details)
            {
                if (before.TryGetValue(detail.Viewport.Id, out var viewport))
                {
                    detail.Viewport.SetViewProjection(viewport, true);
                    detail.CommitViewportChanges();
                }
            }

            return Failure(
                "named_view.apply_failed",
                $"Named-view assignment failed and the original cameras were restored: {exception.Message}");
        }
        finally
        {
            foreach (var viewport in before.Values) viewport.Dispose();
            document.EndUndoRecord(undoRecord);
        }
    }

    private static bool IsDocumentStateChange(OperationChange change)
    {
        return change is AddFolderChange or RenameFolderChange or
            MoveSheetChange or MoveFolderChange or SetPrintInclusionChange or
            SetObserverCanvasStateChange or ReorderSheetsChange;
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
        var storedBeforeState = _stateStore.Get(document);
        var beforeState = plan.Changes.Any(change => change is SetPrintInclusionChange or ReorderSheetsChange)
            ? WithCurrentPageRecords(document, storedBeforeState)
            : storedBeforeState;
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
                SetPrintInclusionChange print => ApplyPrintInclusion(sheets, print),
                SetObserverCanvasStateChange canvas => ApplyObserverCanvasState(beforeState, canvas),
                ReorderSheetsChange reorder => ApplyReorderSheets(sheets, reorder),
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
            ObserverCanvas = plan.Changes
                .OfType<SetObserverCanvasStateChange>()
                .Select(change => change.NewState)
                .LastOrDefault() ?? beforeState.Canvas,
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
                new DocumentStateUndoTag(plan.UndoDescription, storedBeforeState));
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
            _stateStore.Set(document, storedBeforeState);
            return Failure(
                "operation.apply_failed",
                $"The hierarchy change failed and the previous state was restored: {exception.Message}");
        }
        finally
        {
            document.EndUndoRecord(undoRecord);
        }
    }

    private static OperationResult? ApplyObserverCanvasState(
        DocumentState currentState,
        SetObserverCanvasStateChange change)
    {
        return ObserverCanvasStateComparer.ContentEquals(currentState.Canvas, change.ExpectedState)
            ? null
            : Failure(
                "observer.before_value_changed",
                "The observer board changed before this edit was applied. Refresh and try again.");
    }

    private static OperationResult? ApplyReorderSheets(
        IDictionary<Guid, SheetRecord> sheets,
        ReorderSheetsChange change)
    {
        foreach (var expected in change.ExpectedOrders)
        {
            if (!sheets.TryGetValue(expected.Key, out var sheet) ||
                sheet.FolderId != change.FolderId ||
                sheet.Order != expected.Value)
            {
                return Failure("reorder.before_value_changed",
                    "Layout order changed before this edit was applied.");
            }
        }

        foreach (var next in change.NewOrders)
        {
            sheets[next.Key] = sheets[next.Key] with { Order = next.Value };
        }

        return null;
    }

    private static OperationResult? ApplyPrintInclusion(
        IDictionary<Guid, SheetRecord> sheets,
        SetPrintInclusionChange change)
    {
        foreach (var expected in change.ExpectedValues)
        {
            if (!sheets.TryGetValue(expected.Key, out var sheet))
            {
                return Failure("print.sheet_missing", "A targeted layout no longer exists.");
            }

            if (sheet.IncludeInPrintAll != expected.Value)
            {
                return Failure("print.before_value_changed",
                    "Print inclusion changed before this edit was applied.");
            }

            sheets[expected.Key] = sheet with { IncludeInPrintAll = change.IncludeInPrintAll };
        }

        return null;
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

    private OperationResult ApplyDeleteFolderTree(
        RhinoDoc document,
        OperationPlan plan,
        DeleteFolderChange delete)
    {
        var beforeState = _stateStore.Get(document);
        var root = beforeState.Folders.FirstOrDefault(folder => folder.Id == delete.FolderId);
        if (root is null || root.ParentId != delete.ParentFolderId ||
            !string.Equals(root.Name, delete.ExpectedName, StringComparison.Ordinal))
            return Failure("folder.before_value_changed", "The folder changed before deletion.");
        var folderIds = new HashSet<Guid>(delete.DescendantFolderIds ?? []) { delete.FolderId };
        var currentDescendants = FolderDescendants(delete.FolderId, beforeState.Folders);
        if (!currentDescendants.SetEquals(folderIds))
            return Failure("folder.before_value_changed", "The folder contents changed before deletion.");
        var sheetIds = beforeState.Sheets.Values.Where(sheet => folderIds.Contains(sheet.FolderId))
            .Select(sheet => sheet.PageViewId).ToHashSet();
        if (!sheetIds.SetEquals(delete.SheetPageViewIds ?? []))
            return Failure("folder.before_value_changed", "The layouts inside this folder changed before deletion.");
        var pages = document.Views.GetPageViews()
            .Where(page => sheetIds.Contains(page.MainViewport.Id)).ToArray();
        if (pages.Length != sheetIds.Count)
            return Failure("folder.sheet_missing", "A layout inside this folder no longer exists.");

        var afterState = beforeState with
        {
            Folders = beforeState.Folders.Where(folder => !folderIds.Contains(folder.Id)).ToArray(),
            Sheets = beforeState.Sheets.Where(pair => !sheetIds.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value),
        };
        if (pages.Length == 0)
            return ApplyStateOnlyChange(document, plan, beforeState, afterState);

        foreach (var page in pages.Reverse())
        {
            if (!page.Close())
                return Failure("folder.delete_failed",
                    "Rhino could not delete every layout. Review the folder contents before trying again.");
        }
        _stateStore.Set(document, afterState);
        document.Modified = true;
        _revisionTracker.Bump(document);
        document.Views.Redraw();
        return new OperationResult(true, plan.Diagnostics);
    }

    private OperationResult ApplyDeleteHierarchySelection(
        RhinoDoc document,
        OperationPlan plan)
    {
        var beforeState = WithCurrentPageRecords(document, _stateStore.Get(document));
        var folderChanges = plan.Changes.OfType<DeleteFolderChange>().ToArray();
        var sheetChanges = plan.Changes.OfType<DeleteSheetChange>().ToArray();
        var folderIds = new HashSet<Guid>();
        var sheetIds = new HashSet<Guid>();

        foreach (var delete in folderChanges)
        {
            var root = beforeState.Folders.FirstOrDefault(folder => folder.Id == delete.FolderId);
            if (root is null || root.ParentId != delete.ParentFolderId ||
                !string.Equals(root.Name, delete.ExpectedName, StringComparison.Ordinal))
                return Failure("folder.before_value_changed", $"The folder '{delete.ExpectedName}' changed before deletion.");

            var plannedFolders = new HashSet<Guid>(delete.DescendantFolderIds ?? []) { delete.FolderId };
            var currentFolders = FolderDescendants(delete.FolderId, beforeState.Folders);
            if (!currentFolders.SetEquals(plannedFolders) || plannedFolders.Any(id => !folderIds.Add(id)))
                return Failure("selection.changed", "The selected folder hierarchy changed before deletion.");

            var currentSheets = beforeState.Sheets.Values
                .Where(sheet => plannedFolders.Contains(sheet.FolderId))
                .Select(sheet => sheet.PageViewId)
                .ToHashSet();
            if (!currentSheets.SetEquals(delete.SheetPageViewIds ?? []))
                return Failure("selection.changed", $"The layouts inside '{delete.ExpectedName}' changed before deletion.");
            foreach (var pageViewId in currentSheets) sheetIds.Add(pageViewId);
        }

        foreach (var delete in sheetChanges)
        {
            if (!beforeState.Sheets.TryGetValue(delete.PageViewId, out var record) ||
                record.FolderId != delete.ExpectedFolderId || folderIds.Contains(record.FolderId))
                return Failure("selection.changed", $"The layout '{delete.ExpectedName}' moved before deletion.");
            if (!sheetIds.Add(delete.PageViewId))
                return Failure("selection.duplicate_target", "The same layout was included more than once.");
        }

        var pagesById = document.Views.GetPageViews().ToDictionary(page => page.MainViewport.Id);
        var pages = new List<RhinoPageView>();
        foreach (var pageViewId in sheetIds)
        {
            if (!pagesById.TryGetValue(pageViewId, out var page))
                return Failure("selection.sheet_missing", "A selected layout no longer exists.");
            var expectedName = sheetChanges.FirstOrDefault(change => change.PageViewId == pageViewId)?.ExpectedName;
            if (expectedName is not null && !string.Equals(page.PageName, expectedName, StringComparison.Ordinal))
                return Failure("selection.changed", $"The layout '{expectedName}' was renamed before deletion.");
            pages.Add(page);
        }

        var afterState = beforeState with
        {
            Folders = beforeState.Folders.Where(folder => !folderIds.Contains(folder.Id)).ToArray(),
            Sheets = beforeState.Sheets.Where(pair => !sheetIds.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value),
        };
        if (pages.Count == 0)
            return ApplyStateOnlyChange(document, plan, beforeState, afterState);

        foreach (var page in pages.AsEnumerable().Reverse())
        {
            if (!page.Close())
                return Failure("selection.delete_failed",
                    "Rhino could not delete every selected layout. Review the document before trying again.");
        }

        _stateStore.Set(document, afterState);
        document.Modified = true;
        _revisionTracker.Bump(document);
        document.Views.Redraw();
        return new OperationResult(true, plan.Diagnostics);
    }

    private OperationResult ApplyDuplicateFolder(
        RhinoDoc document,
        OperationPlan plan,
        DuplicateFolderChange duplicate)
    {
        var beforeState = _stateStore.Get(document);
        var source = beforeState.Folders.FirstOrDefault(folder => folder.Id == duplicate.SourceFolderId);
        if (source is null || source.ParentId != duplicate.DestinationParentFolderId ||
            !string.Equals(source.Name, duplicate.ExpectedName, StringComparison.Ordinal))
            return Failure("folder.before_value_changed", "The folder changed before duplication.");
        var sourceIds = FolderDescendants(source.Id, beforeState.Folders);
        if (!sourceIds.SetEquals(duplicate.FolderIdMap.Keys) ||
            duplicate.FolderIdMap.Values.Any(id => id == Guid.Empty || beforeState.Folders.Any(folder => folder.Id == id)))
            return Failure("folder.duplicate_plan_invalid", "The folder duplication plan is invalid.");
        if (beforeState.Folders.Any(folder => folder.ParentId == source.ParentId &&
                string.Equals(folder.Name, duplicate.NewName, StringComparison.OrdinalIgnoreCase)))
            return Failure("folder.duplicate_name", $"A folder named '{duplicate.NewName}' already exists.");

        var createdPages = new List<RhinoPageView>();
        try
        {
            var folders = beforeState.Folders.ToList();
            var nextRootOrder = folders.Where(folder => folder.ParentId == source.ParentId)
                .Select(folder => folder.Order).DefaultIfEmpty(-1).Max() + 1;
            foreach (var oldFolder in beforeState.Folders.Where(folder => sourceIds.Contains(folder.Id)))
            {
                folders.Add(new FolderRecord(
                    duplicate.FolderIdMap[oldFolder.Id],
                    oldFolder.Id == source.Id
                        ? source.ParentId
                        : duplicate.FolderIdMap[oldFolder.ParentId!.Value],
                    oldFolder.Id == source.Id ? duplicate.NewName : oldFolder.Name,
                    oldFolder.Id == source.Id ? nextRootOrder : oldFolder.Order));
            }

            var sheets = beforeState.Sheets.ToDictionary(pair => pair.Key, pair => pair.Value);
            var pages = document.Views.GetPageViews().ToDictionary(page => page.MainViewport.Id);
            foreach (var sourceSheet in beforeState.Sheets.Values
                         .Where(sheet => sourceIds.Contains(sheet.FolderId))
                         .OrderBy(sheet => sheet.FolderId).ThenBy(sheet => sheet.Order))
            {
                if (!pages.TryGetValue(sourceSheet.PageViewId, out var sourcePage))
                    throw new InvalidOperationException("A layout inside the source folder no longer exists.");
                var copy = sourcePage.Duplicate(duplicatePageGeometry: true)
                    ?? throw new InvalidOperationException($"Rhino could not duplicate '{sourcePage.PageName}'.");
                createdPages.Add(copy);
                var duplicateTitleBlock = sourceSheet.TitleBlock is { } sourceTitleBlock
                    ? document.Objects.OfType<InstanceObject>().FirstOrDefault(instance =>
                        instance.Attributes.Space == ActiveSpace.PageSpace &&
                        instance.Attributes.ViewportId == copy.MainViewport.Id &&
                        instance.InstanceDefinition.Id == sourceTitleBlock.InstanceDefinitionId)
                    : null;
                sheets[copy.MainViewport.Id] = sourceSheet with
                {
                    PageViewId = copy.MainViewport.Id,
                    FolderId = duplicate.FolderIdMap[sourceSheet.FolderId],
                    TitleBlock = duplicateTitleBlock is null || sourceSheet.TitleBlock is null
                        ? null
                        : sourceSheet.TitleBlock with { InstanceObjectId = duplicateTitleBlock.Id },
                };
            }

            _stateStore.Set(document, beforeState with { Folders = folders, Sheets = sheets });
            document.Modified = true;
            _revisionTracker.Bump(document);
            document.Views.Redraw();
            return new OperationResult(true, plan.Diagnostics);
        }
        catch (Exception exception)
        {
            foreach (var page in createdPages.AsEnumerable().Reverse()) page.Close();
            _stateStore.Set(document, beforeState);
            return Failure("folder.duplicate_failed",
                $"Folder duplication failed and the incomplete copy was removed: {exception.Message}");
        }
    }

    private OperationResult ApplyDuplicateHierarchySelection(
        RhinoDoc document,
        OperationPlan plan)
    {
        var storedState = _stateStore.Get(document);
        var beforeState = WithCurrentPageRecords(document, storedState);
        var folderChanges = plan.Changes.OfType<DuplicateFolderChange>().ToArray();
        var sheetChanges = plan.Changes.OfType<DuplicateSheetChange>().ToArray();
        var sourceFolderIds = new HashSet<Guid>();
        var newFolderIds = folderChanges.SelectMany(change => change.FolderIdMap.Values).ToArray();
        if (newFolderIds.Any(id => id == Guid.Empty) || newFolderIds.Distinct().Count() != newFolderIds.Length ||
            newFolderIds.Any(id => beforeState.Folders.Any(folder => folder.Id == id)))
            return Failure("selection.duplicate_plan_invalid", "The folder duplication plan contains invalid identifiers.");

        var folders = beforeState.Folders.ToList();
        foreach (var duplicate in folderChanges)
        {
            var source = beforeState.Folders.FirstOrDefault(folder => folder.Id == duplicate.SourceFolderId);
            if (source is null || source.ParentId != duplicate.DestinationParentFolderId ||
                !string.Equals(source.Name, duplicate.ExpectedName, StringComparison.Ordinal))
                return Failure("folder.before_value_changed", $"The folder '{duplicate.ExpectedName}' changed before duplication.");
            var sourceIds = FolderDescendants(source.Id, beforeState.Folders);
            if (!sourceIds.SetEquals(duplicate.FolderIdMap.Keys) || sourceIds.Any(id => !sourceFolderIds.Add(id)))
                return Failure("selection.duplicate_plan_invalid", "The selected folder hierarchy changed before duplication.");
            if (folders.Any(folder => folder.ParentId == source.ParentId &&
                    string.Equals(folder.Name, duplicate.NewName, StringComparison.OrdinalIgnoreCase)))
                return Failure("folder.duplicate_name", $"A folder named '{duplicate.NewName}' already exists.");

            var nextRootOrder = folders.Where(folder => folder.ParentId == source.ParentId)
                .Select(folder => folder.Order).DefaultIfEmpty(-1).Max() + 1;
            foreach (var oldFolder in beforeState.Folders.Where(folder => sourceIds.Contains(folder.Id)))
            {
                folders.Add(new FolderRecord(
                    duplicate.FolderIdMap[oldFolder.Id],
                    oldFolder.Id == source.Id
                        ? source.ParentId
                        : duplicate.FolderIdMap[oldFolder.ParentId!.Value],
                    oldFolder.Id == source.Id ? duplicate.NewName : oldFolder.Name,
                    oldFolder.Id == source.Id ? nextRootOrder : oldFolder.Order));
            }
        }

        foreach (var duplicate in sheetChanges)
        {
            if (!beforeState.Sheets.TryGetValue(duplicate.PageViewId, out var record) ||
                record.FolderId != duplicate.ExpectedFolderId || sourceFolderIds.Contains(record.FolderId))
                return Failure("selection.changed", $"The layout '{duplicate.ExpectedName}' moved before duplication.");
        }

        var createdPages = new List<RhinoPageView>();
        try
        {
            var sheets = beforeState.Sheets.ToDictionary(pair => pair.Key, pair => pair.Value);
            var pages = document.Views.GetPageViews().ToDictionary(page => page.MainViewport.Id);

            foreach (var duplicate in folderChanges)
            {
                var sourceIds = duplicate.FolderIdMap.Keys.ToHashSet();
                foreach (var sourceSheet in beforeState.Sheets.Values
                             .Where(sheet => sourceIds.Contains(sheet.FolderId))
                             .OrderBy(sheet => sheet.FolderId).ThenBy(sheet => sheet.Order))
                {
                    var copy = DuplicatePage(document, pages, sourceSheet, createdPages);
                    sheets[copy.MainViewport.Id] = DuplicateSheetRecord(
                        document,
                        sourceSheet,
                        copy,
                        duplicate.FolderIdMap[sourceSheet.FolderId],
                        sourceSheet.Order);
                }
            }

            foreach (var duplicate in sheetChanges)
            {
                var sourceSheet = beforeState.Sheets[duplicate.PageViewId];
                if (!pages.TryGetValue(sourceSheet.PageViewId, out var sourcePage) ||
                    !string.Equals(sourcePage.PageName, duplicate.ExpectedName, StringComparison.Ordinal))
                    throw new InvalidOperationException($"The layout '{duplicate.ExpectedName}' changed before duplication.");
                var copy = DuplicatePage(document, pages, sourceSheet, createdPages);
                var nextOrder = sheets.Values.Where(sheet => sheet.FolderId == sourceSheet.FolderId)
                    .Select(sheet => sheet.Order).DefaultIfEmpty(-1).Max() + 1;
                sheets[copy.MainViewport.Id] = DuplicateSheetRecord(
                    document,
                    sourceSheet,
                    copy,
                    sourceSheet.FolderId,
                    nextOrder);
            }

            _stateStore.Set(document, beforeState with { Folders = folders, Sheets = sheets });
            document.Modified = true;
            _revisionTracker.Bump(document);
            document.Views.Redraw();
            return new OperationResult(true, plan.Diagnostics);
        }
        catch (Exception exception)
        {
            foreach (var page in createdPages.AsEnumerable().Reverse()) page.Close();
            _stateStore.Set(document, storedState);
            return Failure("selection.duplicate_failed",
                $"Duplication failed and every incomplete copy was removed: {exception.Message}");
        }
    }

    private static DocumentState WithCurrentPageRecords(RhinoDoc document, DocumentState state)
    {
        var sheets = state.Sheets.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var entry in document.Views.GetPageViews().Select((page, index) => (page, index)))
        {
            if (!sheets.ContainsKey(entry.page.MainViewport.Id))
                sheets[entry.page.MainViewport.Id] = new SheetRecord(
                    entry.page.MainViewport.Id,
                    state.RootFolderId,
                    entry.index,
                    [],
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    null);
        }
        return state with { Sheets = sheets };
    }

    private static RhinoPageView DuplicatePage(
        RhinoDoc document,
        IReadOnlyDictionary<Guid, RhinoPageView> pages,
        SheetRecord sourceSheet,
        ICollection<RhinoPageView> createdPages)
    {
        if (!pages.TryGetValue(sourceSheet.PageViewId, out var sourcePage))
            throw new InvalidOperationException("A selected layout no longer exists.");
        var copy = sourcePage.Duplicate(duplicatePageGeometry: true)
            ?? throw new InvalidOperationException($"Rhino could not duplicate '{sourcePage.PageName}'.");
        createdPages.Add(copy);
        return copy;
    }

    private static SheetRecord DuplicateSheetRecord(
        RhinoDoc document,
        SheetRecord sourceSheet,
        RhinoPageView copy,
        Guid folderId,
        int order)
    {
        var duplicateTitleBlock = sourceSheet.TitleBlock is { } sourceTitleBlock
            ? document.Objects.OfType<InstanceObject>().FirstOrDefault(instance =>
                instance.Attributes.Space == ActiveSpace.PageSpace &&
                instance.Attributes.ViewportId == copy.MainViewport.Id &&
                instance.InstanceDefinition.Id == sourceTitleBlock.InstanceDefinitionId)
            : null;
        return sourceSheet with
        {
            PageViewId = copy.MainViewport.Id,
            FolderId = folderId,
            Order = order,
            TitleBlock = duplicateTitleBlock is null || sourceSheet.TitleBlock is null
                ? null
                : sourceSheet.TitleBlock with { InstanceObjectId = duplicateTitleBlock.Id },
        };
    }

    private OperationResult ApplyBatchUpdate(
        RhinoDoc document,
        OperationPlan plan,
        BatchUpdateSheetsChange update)
    {
        var pages = document.Views.GetPageViews()
            .Where(page => update.SheetPageViewIds.Contains(page.MainViewport.Id)).ToArray();
        if (pages.Length != update.SheetPageViewIds.Distinct().Count())
            return Failure("batch.sheet_missing", "An included layout no longer exists.");
        var before = pages.Select(page => new PagePropertiesBefore(
            page,
            page.PageName,
            page.PageWidth,
            page.PageHeight,
            page.GetDetailViews().Select(detail =>
                new DetailModeBefore(detail, detail.Viewport.DisplayMode.Id)).ToArray())).ToArray();
        var stateBefore = WithCurrentPageRecords(document, _stateStore.Get(document));
        var titleBlocksBefore = pages.Select(page => CaptureTitleBlockBefore(
            document,
            page.MainViewport.Id,
            stateBefore.Sheets[page.MainViewport.Id].TitleBlock)).ToArray();
        var createdTitleBlockIds = new List<Guid>();
        try
        {
            var pageScale = update.PaperUnitSystem is { } unit
                ? RhinoMath.UnitScale(ParseUnitSystem(unit), document.PageUnitSystem)
                : 1.0;
            DisplayModeDescription? displayMode = null;
            if (update.DetailDisplayModeId is { } modeId)
                displayMode = DisplayModeDescription.GetDisplayMode(modeId)
                    ?? throw new InvalidOperationException("The selected display mode is unavailable.");
            using (displayMode)
            {
                foreach (var page in pages)
                {
                    if (update.NewNames.TryGetValue(page.MainViewport.Id, out var name)) page.PageName = name;
                    if (update.PaperWidth is { } width) page.PageWidth = width * pageScale;
                    if (update.PaperHeight is { } height) page.PageHeight = height * pageScale;
                    if (displayMode is not null)
                    {
                        foreach (var detail in page.GetDetailViews())
                        {
                            detail.Viewport.DisplayMode = displayMode;
                            if (!detail.CommitViewportChanges())
                                throw new InvalidOperationException($"Rhino did not update a detail on '{page.PageName}'.");
                        }
                    }
                }
            }
            if (update.ChangeTitleBlock)
                ApplyBatchTitleBlocks(
                    document,
                    pages,
                    stateBefore,
                    update.TitleBlockSourceInstanceObjectId,
                    createdTitleBlockIds);
            document.Modified = true;
            _revisionTracker.Bump(document);
            document.Views.Redraw();
            return new OperationResult(true, plan.Diagnostics);
        }
        catch (Exception exception)
        {
            foreach (var id in createdTitleBlockIds.AsEnumerable().Reverse())
                document.Objects.Delete(id, true);
            foreach (var item in before)
            {
                item.Page.PageName = item.Name;
                item.Page.PageWidth = item.Width;
                item.Page.PageHeight = item.Height;
                foreach (var detailBefore in item.DetailModes)
                {
                    using var mode = DisplayModeDescription.GetDisplayMode(detailBefore.DisplayModeId);
                    if (mode is null) continue;
                    detailBefore.Detail.Viewport.DisplayMode = mode;
                    detailBefore.Detail.CommitViewportChanges();
                }
            }
            if (update.ChangeTitleBlock)
                RestoreTitleBlocks(document, stateBefore, titleBlocksBefore);
            document.Views.Redraw();
            return Failure("batch.apply_failed",
                $"Batch Apply failed and every available before-value was restored: {exception.Message}");
        }
    }

    private OperationResult ApplyDetailDisplayModes(
        RhinoDoc document,
        OperationPlan plan,
        UpdateDetailDisplayModesChange update)
    {
        var details = document.Views.GetPageViews()
            .SelectMany(page => page.GetDetailViews())
            .Where(detail => update.DetailViewportIds.Contains(detail.Viewport.Id))
            .ToArray();
        if (details.Length != update.DetailViewportIds.Distinct().Count())
            return Failure("inline.detail_missing", "A targeted detail viewport no longer exists.");

        var before = details.Select(detail => new DetailModeBefore(
            detail,
            detail.Viewport.DisplayMode.Id)).ToArray();
        try
        {
            using var mode = DisplayModeDescription.GetDisplayMode(update.DisplayModeId)
                ?? throw new InvalidOperationException("The selected display mode is unavailable.");
            foreach (var detail in details)
            {
                detail.Viewport.DisplayMode = mode;
                if (!detail.CommitViewportChanges())
                    throw new InvalidOperationException("Rhino did not commit a detail display-mode change.");
            }

            document.Modified = true;
            _revisionTracker.Bump(document);
            document.Views.Redraw();
            return new OperationResult(true, plan.Diagnostics);
        }
        catch (Exception exception)
        {
            foreach (var item in before)
            {
                using var mode = DisplayModeDescription.GetDisplayMode(item.DisplayModeId);
                if (mode is null) continue;
                item.Detail.Viewport.DisplayMode = mode;
                item.Detail.CommitViewportChanges();
            }

            document.Views.Redraw();
            return Failure("inline.apply_failed",
                $"The display-mode edit failed and every available before-value was restored: {exception.Message}");
        }
    }

    private void ApplyBatchTitleBlocks(
        RhinoDoc document,
        IReadOnlyList<RhinoPageView> pages,
        DocumentState stateBefore,
        Guid? sourceInstanceObjectId,
        ICollection<Guid> createdTitleBlockIds)
    {
        InstanceObject? source = null;
        string anchorName = "Template";
        if (sourceInstanceObjectId is { } sourceId)
        {
            source = document.Objects.FindId(sourceId) as InstanceObject;
            if (source is null || source.Attributes.Space != ActiveSpace.PageSpace)
                throw new InvalidOperationException("The selected title-block instance is no longer available.");
            anchorName = stateBefore.Sheets.Values
                .Select(sheet => sheet.TitleBlock)
                .FirstOrDefault(role => role?.InstanceObjectId == sourceId)?.AnchorName ?? "Template";
        }

        var sheets = stateBefore.Sheets.ToDictionary(pair => pair.Key, pair => pair.Value);
        var newRoles = new Dictionary<Guid, TitleBlockRole?>();
        foreach (var page in pages)
        {
            TitleBlockRole? role = null;
            if (source is not null)
            {
                Guid instanceId;
                if (source.Attributes.ViewportId == page.MainViewport.Id)
                {
                    instanceId = source.Id;
                }
                else
                {
                    var attributes = source.Attributes.Duplicate();
                    attributes.Space = ActiveSpace.PageSpace;
                    attributes.ViewportId = page.MainViewport.Id;
                    instanceId = document.Objects.AddInstanceObject(
                        source.InstanceDefinition.Index,
                        source.InstanceXform,
                        attributes);
                    if (instanceId == Guid.Empty)
                        throw new InvalidOperationException($"Rhino could not place the title block on '{page.PageName}'.");
                    createdTitleBlockIds.Add(instanceId);
                }
                role = new TitleBlockRole(instanceId, source.InstanceDefinition.Id, anchorName);
            }
            newRoles[page.MainViewport.Id] = role;
        }

        foreach (var page in pages)
        {
            var oldRole = sheets[page.MainViewport.Id].TitleBlock;
            var newRole = newRoles[page.MainViewport.Id];
            if (oldRole is not null && oldRole.InstanceObjectId != newRole?.InstanceObjectId &&
                document.Objects.FindId(oldRole.InstanceObjectId) is not null &&
                !document.Objects.Delete(oldRole.InstanceObjectId, true))
                throw new InvalidOperationException($"Rhino could not replace the title block on '{page.PageName}'.");
            sheets[page.MainViewport.Id] = sheets[page.MainViewport.Id] with { TitleBlock = newRole };
        }

        _stateStore.Set(document, stateBefore with { Sheets = sheets });
    }

    private static TitleBlockBefore CaptureTitleBlockBefore(
        RhinoDoc document,
        Guid pageViewId,
        TitleBlockRole? role)
    {
        var instance = role is null ? null : document.Objects.FindId(role.InstanceObjectId) as InstanceObject;
        return new TitleBlockBefore(
            pageViewId,
            role,
            instance?.InstanceDefinition.Index,
            instance?.InstanceXform ?? Transform.Identity,
            instance?.Attributes.Duplicate());
    }

    private void RestoreTitleBlocks(
        RhinoDoc document,
        DocumentState stateBefore,
        IReadOnlyList<TitleBlockBefore> before)
    {
        var sheets = stateBefore.Sheets.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var item in before)
        {
            var role = item.Role;
            if (role is not null && document.Objects.FindId(role.InstanceObjectId) is null &&
                item.DefinitionIndex is { } definitionIndex && item.Attributes is not null)
            {
                var restoredId = document.Objects.AddInstanceObject(definitionIndex, item.Transform, item.Attributes);
                role = restoredId == Guid.Empty ? null : role with { InstanceObjectId = restoredId };
            }
            sheets[item.PageViewId] = sheets[item.PageViewId] with { TitleBlock = role };
        }
        _stateStore.Set(document, stateBefore with { Sheets = sheets });
    }

    private static HashSet<Guid> FolderDescendants(Guid rootId, IReadOnlyList<FolderRecord> folders)
    {
        var result = new HashSet<Guid> { rootId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var folder in folders.Where(folder =>
                         folder.ParentId is { } parent && result.Contains(parent)))
                changed |= result.Add(folder.Id);
        }
        return result;
    }

    private OperationResult ApplyCaptureTemplate(
        RhinoDoc document,
        OperationPlan plan,
        CaptureSheetTemplateChange capture)
    {
        var beforeState = _stateStore.Get(document);
        var page = document.Views.GetPageViews()
            .FirstOrDefault(candidate => candidate.MainViewport.Id == capture.SourcePageViewId);
        if (page is null)
            return Failure("template.source_missing", "The source layout no longer exists.");
        if (beforeState.Templates.Any(item => item.Id == capture.TemplateId ||
                string.Equals(item.Name, capture.Name, StringComparison.OrdinalIgnoreCase)))
            return Failure("template.duplicate_name", $"A template named '{capture.Name}' already exists.");

        try
        {
            var details = page.GetDetailViews().Select(detail => CaptureDetail(detail)).ToArray();
            TitleBlockTemplateRecipe? titleBlock = null;
            if (capture.TitleBlockInstanceObjectId is { } blockId)
            {
                if (document.Objects.FindId(blockId) is not InstanceObject instance ||
                    instance.Attributes.Space != ActiveSpace.PageSpace ||
                    instance.Attributes.ViewportId != page.MainViewport.Id)
                    return Failure("template.title_block_invalid",
                        "The designated title block is not a block instance on the source layout.");
                titleBlock = new TitleBlockTemplateRecipe(
                    instance.InstanceDefinition.Id,
                    instance.InstanceDefinition.Name,
                    TransformValues(instance.InstanceXform),
                    "Captured",
                    new Dictionary<string, string>(StringComparer.Ordinal));
            }

            var sourceRecord = beforeState.Sheets.GetValueOrDefault(page.MainViewport.Id);
            var recipe = new SheetTemplateRecipe(
                capture.TemplateId,
                SheetTemplateRecipe.CurrentRecipeVersion,
                capture.Name,
                new PaperRecipe(page.PageWidth, page.PageHeight, document.PageUnitSystem.ToString()),
                details,
                titleBlock,
                sourceRecord?.Tags.ToArray() ?? [],
                sourceRecord?.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value) ??
                    new Dictionary<string, string>(StringComparer.Ordinal),
                capture.DefaultNamingPattern)
            {
                SourcePageViewId = capture.SourcePageViewId,
            };
            var afterState = beforeState with
            {
                SchemaVersion = DocumentState.CurrentSchemaVersion,
                SheetTemplates = beforeState.Templates.Append(recipe).ToArray(),
            };
            return ApplyStateOnlyChange(document, plan, beforeState, afterState);
        }
        catch (Exception exception)
        {
            return Failure("template.capture_failed", $"The template could not be captured: {exception.Message}");
        }
    }

    private OperationResult ApplyDeleteTemplates(
        RhinoDoc document,
        OperationPlan plan,
        IReadOnlyList<DeleteSheetTemplateChange> deletes)
    {
        var beforeState = _stateStore.Get(document);
        foreach (var delete in deletes)
        {
            var template = beforeState.Templates.FirstOrDefault(item => item.Id == delete.TemplateId);
            if (template is null || !string.Equals(template.Name, delete.ExpectedName, StringComparison.Ordinal))
                return Failure("template.before_value_changed", "A template changed before it could be unregistered.");
        }

        var deletedIds = deletes.Select(delete => delete.TemplateId).ToHashSet();
        var afterState = beforeState with
        {
            SheetTemplates = beforeState.Templates.Where(item => !deletedIds.Contains(item.Id)).ToArray(),
        };
        return ApplyStateOnlyChange(document, plan, beforeState, afterState);
    }

    private OperationResult ApplyTemplateBatch(
        RhinoDoc document,
        OperationPlan plan,
        IReadOnlyList<CreateSheetFromTemplateChange> creates)
    {
        var beforeState = _stateStore.Get(document);
        var existingNames = document.Views.GetPageViews().Select(page => page.PageName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (creates.Any(item => !beforeState.Folders.Any(folder => folder.Id == item.DestinationFolderId)))
            return Failure("batch.destination_missing", "The destination folder no longer exists.");
        if (creates.Any(item => !existingNames.Add(item.Name)))
            return Failure("batch.duplicate_name", "One or more layout names now conflict with the document.");

        var createdPages = new List<RhinoPageView>();
        DetailLayerResolution? detailLayer = null;
        try
        {
            if (creates.Any(create => create.UseDedicatedDetailLayer && create.Template.DetailSlots.Count > 0))
                detailLayer = ResolveDedicatedDetailLayer(document, beforeState.DedicatedDetailLayerId);

            var sheets = beforeState.Sheets.ToDictionary(pair => pair.Key, pair => pair.Value);
            foreach (var create in creates)
            {
                var recipeUnit = ParseUnitSystem(create.Template.Paper.UnitSystem);
                var pageScale = RhinoMath.UnitScale(recipeUnit, document.PageUnitSystem);
                var page = document.Views.AddPageView(
                    create.Name,
                    create.Template.Paper.Width * pageScale,
                    create.Template.Paper.Height * pageScale)
                    ?? throw new InvalidOperationException($"Rhino did not create '{create.Name}'.");
                createdPages.Add(page);

                foreach (var slot in create.Template.DetailSlots)
                    CreateDetail(document, page, slot, recipeUnit, pageScale,
                        create.NamedViewAssignments.GetValueOrDefault(slot.Id),
                        create.UseDedicatedDetailLayer ? detailLayer?.LayerIndex : null);

                Guid? titleBlockId = null;
                if (create.Template.TitleBlock is { } titleBlock)
                    titleBlockId = CreateTitleBlock(document, page, titleBlock);

                sheets[page.MainViewport.Id] = new SheetRecord(
                    page.MainViewport.Id,
                    create.DestinationFolderId,
                    create.Order,
                    create.Template.DefaultTags.ToArray(),
                    create.Template.DefaultMetadata.ToDictionary(pair => pair.Key, pair => pair.Value),
                    titleBlockId is { } instanceId && create.Template.TitleBlock is { } block
                        ? new TitleBlockRole(instanceId, block.InstanceDefinitionId, block.AnchorName)
                        : null);
            }

            _stateStore.Set(document, beforeState with
            {
                Sheets = sheets,
                DedicatedDetailLayerId = detailLayer?.LayerId ?? beforeState.DedicatedDetailLayerId,
            });
            document.Modified = true;
            _revisionTracker.Bump(document);
            document.Views.Redraw();
            return new OperationResult(true, plan.Diagnostics);
        }
        catch (Exception exception)
        {
            _stateStore.Set(document, beforeState);
            foreach (var page in createdPages.AsEnumerable().Reverse())
                page.Close();
            if (detailLayer is { Created: true })
                document.Layers.Delete(detailLayer.LayerId, quiet: true);
            return Failure("batch.apply_failed",
                $"Batch creation failed; every layout created by this batch was removed: {exception.Message}");
        }
    }

    private static DetailLayerResolution ResolveDedicatedDetailLayer(
        RhinoDoc document,
        Guid? trackedLayerId)
    {
        if (trackedLayerId is { } layerId)
        {
            var tracked = document.Layers.FirstOrDefault(layer =>
                layer.Id == layerId && !layer.IsDeleted && !layer.IsReference);
            if (tracked is not null)
                return new DetailLayerResolution(tracked.Index, tracked.Id, false);
        }

        var existing = document.Layers.FirstOrDefault(layer =>
            !layer.IsDeleted &&
            !layer.IsReference &&
            layer.ParentLayerId == Guid.Empty &&
            string.Equals(layer.Name, DedicatedDetailLayerName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return new DetailLayerResolution(existing.Index, existing.Id, false);

        var definition = new Layer { Name = DedicatedDetailLayerName };
        var index = document.Layers.Add(definition);
        if (index < 0)
            throw new InvalidOperationException($"Rhino did not create the '{DedicatedDetailLayerName}' layer.");
        var created = document.Layers[index];
        return new DetailLayerResolution(created.Index, created.Id, true);
    }

    private static DetailSlotRecipe CaptureDetail(DetailViewObject detail)
    {
        var bounds = detail.DetailGeometry.GetBoundingBox(true);
        var viewport = detail.Viewport;
        return new DetailSlotRecipe(
            Guid.NewGuid(),
            string.IsNullOrWhiteSpace(detail.Attributes.Name) ? viewport.Name : detail.Attributes.Name,
            bounds.Min.X,
            bounds.Min.Y,
            bounds.Max.X,
            bounds.Max.Y,
            viewport.IsPerspectiveProjection ? "Perspective" : "Top",
            detail.DetailGeometry.IsParallelProjection ? detail.DetailGeometry.PageToModelRatio : null,
            detail.DetailGeometry.IsProjectionLocked,
            viewport.DisplayMode.Id,
            null,
            [viewport.CameraLocation.X, viewport.CameraLocation.Y, viewport.CameraLocation.Z],
            [viewport.CameraTarget.X, viewport.CameraTarget.Y, viewport.CameraTarget.Z],
            [viewport.CameraUp.X, viewport.CameraUp.Y, viewport.CameraUp.Z]);
    }

    private static void CreateDetail(
        RhinoDoc document,
        RhinoPageView page,
        DetailSlotRecipe slot,
        UnitSystem recipePageUnit,
        double pageScale,
        string? assignedNamedView,
        int? detailLayerIndex)
    {
        var projection = Enum.TryParse<DefinedViewportProjection>(slot.Projection, true, out var parsed)
            ? parsed
            : DefinedViewportProjection.Top;
        var detail = page.AddDetailView(
            slot.Name,
            new Point2d(slot.Left * pageScale, slot.Bottom * pageScale),
            new Point2d(slot.Right * pageScale, slot.Top * pageScale),
            projection) ?? throw new InvalidOperationException($"Rhino did not create detail '{slot.Name}'.");

        var objectChanged = false;
        if (slot.PageToModelRatio is { } ratio && ratio > 0 && detail.DetailGeometry.IsParallelProjection)
        {
            if (!detail.DetailGeometry.SetScale(ratio, recipePageUnit, 1, document.ModelUnitSystem))
                throw new InvalidOperationException($"Rhino did not set the scale for detail '{slot.Name}'.");
            objectChanged = true;
        }
        if (detail.DetailGeometry.IsProjectionLocked != slot.ProjectionLocked)
        {
            detail.DetailGeometry.IsProjectionLocked = slot.ProjectionLocked;
            objectChanged = true;
        }
        if (detailLayerIndex is { } layerIndex && detail.Attributes.LayerIndex != layerIndex)
        {
            detail.Attributes.LayerIndex = layerIndex;
            objectChanged = true;
        }
        if (objectChanged)
        {
            if (!detail.CommitChanges())
                throw new InvalidOperationException($"Rhino did not commit detail properties for '{slot.Name}'.");
            detail = document.Objects.FindId(detail.Id) as DetailViewObject
                ?? throw new InvalidOperationException($"Rhino could not find detail '{slot.Name}' after committing it.");
        }

        var viewportChanged = false;
        var namedView = assignedNamedView ?? slot.DefaultNamedView;
        if (!string.IsNullOrWhiteSpace(namedView))
        {
            var index = document.NamedViews.FindByName(namedView);
            if (index >= 0)
            {
                document.NamedViews.Restore(index, detail.Viewport);
                viewportChanged = true;
            }
        }
        else if (slot.CameraLocation is [var lx, var ly, var lz] &&
                 slot.CameraTarget is [var tx, var ty, var tz])
        {
            detail.Viewport.SetCameraLocations(new Point3d(tx, ty, tz), new Point3d(lx, ly, lz));
            viewportChanged = true;
        }
        if (slot.DisplayModeId is { } modeId)
        {
            using var mode = DisplayModeDescription.GetDisplayMode(modeId);
            if (mode is not null)
            {
                detail.Viewport.DisplayMode = mode;
                viewportChanged = true;
            }
        }
        if (viewportChanged && !detail.CommitViewportChanges())
            throw new InvalidOperationException($"Rhino did not commit viewport settings for detail '{slot.Name}'.");
    }

    private static Guid? CreateTitleBlock(
        RhinoDoc document,
        RhinoPageView page,
        TitleBlockTemplateRecipe titleBlock)
    {
        var definition = document.InstanceDefinitions.Find(titleBlock.InstanceDefinitionId, true)
            ?? document.InstanceDefinitions.Find(titleBlock.InstanceDefinitionName);
        if (definition is null)
            return null;
        var attributes = new ObjectAttributes
        {
            Space = ActiveSpace.PageSpace,
            ViewportId = page.MainViewport.Id,
        };
        var id = document.Objects.AddInstanceObject(definition.Index, RestoreTransform(titleBlock.Transform), attributes);
        if (id == Guid.Empty)
            throw new InvalidOperationException($"Rhino did not place title block '{titleBlock.InstanceDefinitionName}'.");
        return id;
    }

    private OperationResult ApplyStateOnlyChange(
        RhinoDoc document,
        OperationPlan plan,
        DocumentState beforeState,
        DocumentState afterState)
    {
        var undoRecord = document.BeginUndoRecord(plan.UndoDescription);
        if (undoRecord == 0)
            return Failure("operation.undo_unavailable", "Rhino could not start an undo record.");
        try
        {
            if (!document.AddCustomUndoEvent(plan.UndoDescription, OnUndoDocumentState,
                    new DocumentStateUndoTag(plan.UndoDescription, beforeState)))
                return Failure("operation.undo_unavailable", "Rhino could not register template metadata with Undo.");
            _stateStore.Set(document, afterState);
            document.Modified = true;
            _revisionTracker.Bump(document);
            return new OperationResult(true, plan.Diagnostics);
        }
        catch (Exception exception)
        {
            _stateStore.Set(document, beforeState);
            return Failure("operation.apply_failed", $"The template change failed and was restored: {exception.Message}");
        }
        finally
        {
            document.EndUndoRecord(undoRecord);
        }
    }

    private static UnitSystem ParseUnitSystem(string value) =>
        Enum.TryParse<UnitSystem>(value, true, out var result)
            ? result
            : throw new InvalidOperationException($"Page unit system '{value}' is not supported.");

    private static double[] TransformValues(Transform transform) =>
    [
        transform.M00, transform.M01, transform.M02, transform.M03,
        transform.M10, transform.M11, transform.M12, transform.M13,
        transform.M20, transform.M21, transform.M22, transform.M23,
        transform.M30, transform.M31, transform.M32, transform.M33,
    ];

    private static Transform RestoreTransform(IReadOnlyList<double> values)
    {
        if (values.Count != 16)
            throw new InvalidOperationException("The title-block transform is invalid.");
        var transform = new Transform
        {
            M00 = values[0], M01 = values[1], M02 = values[2], M03 = values[3],
            M10 = values[4], M11 = values[5], M12 = values[6], M13 = values[7],
            M20 = values[8], M21 = values[9], M22 = values[10], M23 = values[11],
            M30 = values[12], M31 = values[13], M32 = values[14], M33 = values[15],
        };
        return transform;
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

    private sealed record DetailLayerResolution(int LayerIndex, Guid LayerId, bool Created);

    private sealed record PagePropertiesBefore(
        RhinoPageView Page,
        string Name,
        double Width,
        double Height,
        IReadOnlyList<DetailModeBefore> DetailModes);

    private sealed record DetailModeBefore(DetailViewObject Detail, Guid DisplayModeId);

    private sealed record TitleBlockBefore(
        Guid PageViewId,
        TitleBlockRole? Role,
        int? DefinitionIndex,
        Transform Transform,
        ObjectAttributes? Attributes);
}

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

internal sealed partial class RhinoMutationExecutor
{
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

    private static OperationResult? ApplyReorganizeHierarchy(
        Guid rootFolderId,
        IList<FolderRecord> folders,
        IDictionary<Guid, SheetRecord> sheets,
        ReorganizeHierarchyChange change)
    {
        var folderById = folders.ToDictionary(folder => folder.Id);
        if (change.ExpectedFolders.Select(item => item.FolderId).Distinct().Count() != change.ExpectedFolders.Count ||
            change.NewFolders.Select(item => item.FolderId).Distinct().Count() != change.NewFolders.Count ||
            change.ExpectedSheets.Select(item => item.PageViewId).Distinct().Count() != change.ExpectedSheets.Count ||
            change.NewSheets.Select(item => item.PageViewId).Distinct().Count() != change.NewSheets.Count)
            return Failure("hierarchy.invalid_change", "The hierarchy change contains duplicate items.");

        if (!change.ExpectedFolders.Select(item => item.FolderId)
                .ToHashSet().SetEquals(change.NewFolders.Select(item => item.FolderId)) ||
            !change.ExpectedSheets.Select(item => item.PageViewId)
                .ToHashSet().SetEquals(change.NewSheets.Select(item => item.PageViewId)))
            return Failure("hierarchy.invalid_change", "The hierarchy change has mismatched before and after items.");

        foreach (var expected in change.ExpectedFolders)
        {
            if (!folderById.TryGetValue(expected.FolderId, out var folder))
                return Failure("hierarchy.folder_missing", "A reorganized folder no longer exists.");
            if (folder.ParentId != expected.ParentFolderId || folder.Order != expected.Order)
                return Failure("hierarchy.before_value_changed",
                    "Folder placement changed before this edit was applied.");
        }

        foreach (var expected in change.ExpectedSheets)
        {
            if (!sheets.TryGetValue(expected.PageViewId, out var sheet))
                return Failure("hierarchy.sheet_missing", "A reorganized layout no longer exists.");
            if (sheet.FolderId != expected.FolderId || sheet.Order != expected.Order)
                return Failure("hierarchy.before_value_changed",
                    "Layout placement changed before this edit was applied.");
        }

        foreach (var next in change.NewFolders)
        {
            if (next.FolderId == rootFolderId)
                return Failure("hierarchy.root_move", "The document root cannot be moved.");
            if (next.ParentFolderId is not { } parentId || !folderById.ContainsKey(parentId))
                return Failure("hierarchy.destination_missing", "A destination folder no longer exists.");
            folderById[next.FolderId] = folderById[next.FolderId] with
            {
                ParentId = parentId,
                Order = next.Order,
            };
        }

        foreach (var next in change.NewSheets)
        {
            if (!folderById.ContainsKey(next.FolderId))
                return Failure("hierarchy.destination_missing", "A destination folder no longer exists.");
            sheets[next.PageViewId] = sheets[next.PageViewId] with
            {
                FolderId = next.FolderId,
                Order = next.Order,
            };
        }

        foreach (var folder in folderById.Values.Where(folder => folder.Id != rootFolderId))
        {
            var visited = new HashSet<Guid>();
            var current = folder.Id;
            while (current != rootFolderId)
            {
                if (!visited.Add(current) || !folderById.TryGetValue(current, out var ancestor) ||
                    ancestor.ParentId is not { } parentId)
                    return Failure("hierarchy.folder_cycle", "The hierarchy change would create a folder cycle.");
                current = parentId;
            }
        }

        folders.Clear();
        foreach (var folder in folderById.Values) folders.Add(folder);
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

    private static OperationResult? ApplyHierarchyNotes(
        IList<FolderRecord> folders,
        IDictionary<Guid, SheetRecord> sheets,
        UpdateHierarchyNotesChange change)
    {
        foreach (var expected in change.ExpectedFolderNotes)
        {
            var index = folders.ToList().FindIndex(folder => folder.Id == expected.Key);
            if (index < 0 ||
                !string.Equals(folders[index].Notes ?? string.Empty, expected.Value, StringComparison.Ordinal))
                return Failure("notes.before_value_changed", "A folder's notes changed before the edit was applied.");
            if (!change.NewFolderNotes.TryGetValue(expected.Key, out var next))
                return Failure("notes.plan_invalid", "The folder notes edit is incomplete.");
            folders[index] = folders[index] with { Notes = next ?? string.Empty };
        }

        foreach (var expected in change.ExpectedSheetNotes)
        {
            if (!sheets.TryGetValue(expected.Key, out var sheet) ||
                !string.Equals(sheet.Notes ?? string.Empty, expected.Value, StringComparison.Ordinal))
                return Failure("notes.before_value_changed", "A layout's notes changed before the edit was applied.");
            if (!change.NewSheetNotes.TryGetValue(expected.Key, out var next))
                return Failure("notes.plan_invalid", "The layout notes edit is incomplete.");
            sheets[expected.Key] = sheet with { Notes = next ?? string.Empty };
        }

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

        var deletedDetailIds = pages.SelectMany(page => page.GetDetailViews())
            .Select(detail => detail.Viewport.Id).ToHashSet();
        var afterState = FreezeCapabilitiesForDeletedScopes(beforeState, beforeState with
        {
            Folders = beforeState.Folders.Where(folder => !folderIds.Contains(folder.Id)).ToArray(),
            Sheets = beforeState.Sheets.Where(pair => !sheetIds.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value),
        }, folderIds, sheetIds, deletedDetailIds);
        if (pages.Length == 0)
            return ApplyStateOnlyChange(document, plan, beforeState, afterState);

        foreach (var page in pages.AsEnumerable().Reverse())
        {
            if (!page.Close())
                return Failure("folder.delete_failed",
                    "Rhino could not delete every layout. Review the folder contents before trying again.");
        }
        _stateStore.Set(document, _stateStore.Reconcile(document, afterState));
        document.Modified = true;
        _revisionTracker.Bump(document);
        document.Views.Redraw();
        return new OperationResult(true, plan.Diagnostics);
    }

    private static DocumentState FreezeCapabilitiesForDeletedScopes(
        DocumentState before,
        DocumentState after,
        IReadOnlySet<Guid> deletedFolderIds,
        IReadOnlySet<Guid> deletedSheetIds,
        IReadOnlySet<Guid> deletedDetailIds)
    {
        bool Deleted(HierarchyScope scope) => scope.Kind switch
        {
            HierarchyScopeKind.Folder => deletedFolderIds.Contains(scope.Id),
            HierarchyScopeKind.Sheet => deletedSheetIds.Contains(scope.Id),
            HierarchyScopeKind.Detail => deletedDetailIds.Contains(scope.Id),
            _ => false,
        };

        var rules = before.AppearanceRules.Where(item => !Deleted(item.Scope)).ToArray();
        var registrations = before.TemplateRegistrations.Where(item => !Deleted(item.Source)).ToArray();
        var registrationIds = registrations.Select(item => item.Id).ToHashSet();
        var appearanceStates = before.AppearanceStates
            .Where(item => !deletedFolderIds.Contains(item.FolderId)).ToArray();
        var appearanceStateIds = appearanceStates.Select(item => item.Id).ToHashSet();
        var statePlacements = before.Canvas.StatePlacements
            .Where(pair => appearanceStateIds.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        return after with
        {
            SheetTemplates = before.Templates.Where(template =>
                template.SourcePageViewId is not { } source || !deletedSheetIds.Contains(source)).ToArray(),
            ViewportRuleSets = rules,
            CapabilityTemplates = registrations,
            CapabilityLinks = before.TemplateLinks.Where(link =>
                !Deleted(link.Target) && registrationIds.Contains(link.SourceRegistrationId)).ToArray(),
            AppearanceStateResources = appearanceStates,
            AppearanceStateAssignments = before.StateAssignments.Where(assignment =>
                !Deleted(assignment.Target) && appearanceStateIds.Contains(assignment.StateId)).ToArray(),
            ObserverCanvas = after.Canvas with
            {
                AppearanceStatePlacements = statePlacements,
            },
        };
    }

    private OperationResult ApplyDeleteHierarchySelection(
        RhinoDoc document,
        OperationPlan plan)
    {
        var beforeState = WithCurrentPageRecords(document, _stateStore.Get(document));
        var folderChanges = plan.Changes.OfType<DeleteFolderChange>().ToArray();
        var sheetChanges = plan.Changes.OfType<DeleteSheetChange>().ToArray();
        var stateChanges = plan.Changes.OfType<SetAppearanceStateResourceChange>().ToArray();
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

        var standaloneStateIds = new HashSet<Guid>();
        foreach (var change in stateChanges)
        {
            if (change.NewState is not null || change.ExpectedState is null ||
                !beforeState.AppearanceStates.Contains(change.ExpectedState) ||
                folderIds.Contains(change.ExpectedState.FolderId))
                return Failure("appearance_state.before_value_changed",
                    "An appearance state changed before deletion.");
            standaloneStateIds.Add(change.StateId);
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

        var deletedDetailIds = pages.SelectMany(page => page.GetDetailViews())
            .Select(detail => detail.Viewport.Id).ToHashSet();
        var afterState = FreezeCapabilitiesForDeletedScopes(beforeState, beforeState with
        {
            Folders = beforeState.Folders.Where(folder => !folderIds.Contains(folder.Id)).ToArray(),
            Sheets = beforeState.Sheets.Where(pair => !sheetIds.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value),
        }, folderIds, sheetIds, deletedDetailIds);
        if (standaloneStateIds.Count > 0)
        {
            afterState = afterState with
            {
                AppearanceStateResources = afterState.AppearanceStates
                    .Where(state => !standaloneStateIds.Contains(state.Id)).ToArray(),
                AppearanceStateAssignments = afterState.StateAssignments
                    .Where(assignment => !standaloneStateIds.Contains(assignment.StateId)).ToArray(),
            };
        }
        if (pages.Count == 0)
        {
            var rootScope = new HierarchyScope(HierarchyScopeKind.Folder, beforeState.RootFolderId);
            var rootRules = beforeState.AppearanceRules.LastOrDefault(item => item.Scope == rootScope);
            return ApplyViewportAppearanceRules(
                document,
                plan,
                new SetHierarchyViewportRulesChange(rootScope, rootRules, rootRules),
                afterState.TemplateLinks,
                afterState.TemplateRegistrations,
                afterState.AppearanceStates,
                afterState.StateAssignments,
                afterState);
        }

        foreach (var page in pages.AsEnumerable().Reverse())
        {
            if (!page.Close())
                return Failure("selection.delete_failed",
                    "Rhino could not delete every selected layout. Review the document before trying again.");
        }

        var remainingRootScope = new HierarchyScope(HierarchyScopeKind.Folder, beforeState.RootFolderId);
        var remainingRootRules = beforeState.AppearanceRules.LastOrDefault(item => item.Scope == remainingRootScope);
        return ApplyViewportAppearanceRules(
            document,
            plan,
            new SetHierarchyViewportRulesChange(remainingRootScope, remainingRootRules, remainingRootRules),
            afterState.TemplateLinks,
            afterState.TemplateRegistrations,
            afterState.AppearanceStates,
            afterState.StateAssignments,
            afterState);
    }

    private OperationResult ApplyDuplicateFolder(
        RhinoDoc document,
        OperationPlan plan,
        DuplicateFolderChange duplicate)
    {
        var beforeState = _stateStore.Get(document);
        var source = beforeState.Folders.FirstOrDefault(folder => folder.Id == duplicate.SourceFolderId);
        if (source is null || source.ParentId != duplicate.ExpectedParentFolderId ||
            !string.Equals(source.Name, duplicate.ExpectedName, StringComparison.Ordinal))
            return Failure("folder.before_value_changed", "The folder changed before duplication.");
        var sourceIds = FolderDescendants(source.Id, beforeState.Folders);
        if (!sourceIds.SetEquals(duplicate.FolderIdMap.Keys) ||
            duplicate.FolderIdMap.Values.Any(id => id == Guid.Empty || beforeState.Folders.Any(folder => folder.Id == id)))
            return Failure("folder.duplicate_plan_invalid", "The folder duplication plan is invalid.");
        if (beforeState.Folders.All(folder => folder.Id != duplicate.DestinationParentFolderId))
            return Failure("folder.destination_missing", "The destination folder no longer exists.");
        if (beforeState.Folders.Any(folder => folder.ParentId == duplicate.DestinationParentFolderId &&
                string.Equals(folder.Name, duplicate.NewName, StringComparison.OrdinalIgnoreCase)))
            return Failure("folder.duplicate_name", $"A folder named '{duplicate.NewName}' already exists.");

        var createdPages = new List<RhinoPageView>();
        try
        {
            var folders = beforeState.Folders.ToList();
            var nextRootOrder = folders.Where(folder => folder.ParentId == duplicate.DestinationParentFolderId)
                .Select(folder => folder.Order).DefaultIfEmpty(-1).Max() + 1;
            foreach (var oldFolder in beforeState.Folders.Where(folder => sourceIds.Contains(folder.Id)))
            {
                folders.Add(new FolderRecord(
                    duplicate.FolderIdMap[oldFolder.Id],
                    oldFolder.Id == source.Id
                        ? duplicate.DestinationParentFolderId
                        : duplicate.FolderIdMap[oldFolder.ParentId!.Value],
                    oldFolder.Id == source.Id ? duplicate.NewName : oldFolder.Name,
                    oldFolder.Id == source.Id ? nextRootOrder : oldFolder.Order,
                    oldFolder.Notes ?? string.Empty));
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
                    NamingBinding = null,
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
        var placement = plan.Changes.OfType<PlacePastedHierarchyOnCanvasChange>().SingleOrDefault();
        var sourceFolderIds = new HashSet<Guid>();
        var newFolderIds = folderChanges.SelectMany(change => change.FolderIdMap.Values).ToArray();
        if (newFolderIds.Any(id => id == Guid.Empty) || newFolderIds.Distinct().Count() != newFolderIds.Length ||
            newFolderIds.Any(id => beforeState.Folders.Any(folder => folder.Id == id)))
            return Failure("selection.duplicate_plan_invalid", "The folder duplication plan contains invalid identifiers.");

        var folders = beforeState.Folders.ToList();
        foreach (var duplicate in folderChanges)
        {
            var source = beforeState.Folders.FirstOrDefault(folder => folder.Id == duplicate.SourceFolderId);
            if (source is null || source.ParentId != duplicate.ExpectedParentFolderId ||
                !string.Equals(source.Name, duplicate.ExpectedName, StringComparison.Ordinal))
                return Failure("folder.before_value_changed", $"The folder '{duplicate.ExpectedName}' changed before duplication.");
            if (folders.All(folder => folder.Id != duplicate.DestinationParentFolderId))
                return Failure("folder.destination_missing", "The paste destination no longer exists.");
            var sourceIds = FolderDescendants(source.Id, beforeState.Folders);
            if (!sourceIds.SetEquals(duplicate.FolderIdMap.Keys) || sourceIds.Any(id => !sourceFolderIds.Add(id)))
                return Failure("selection.duplicate_plan_invalid", "The selected folder hierarchy changed before duplication.");
            if (folders.Any(folder => folder.ParentId == duplicate.DestinationParentFolderId &&
                    string.Equals(folder.Name, duplicate.NewName, StringComparison.OrdinalIgnoreCase)))
                return Failure("folder.duplicate_name", $"A folder named '{duplicate.NewName}' already exists.");

            var nextRootOrder = folders.Where(folder => folder.ParentId == duplicate.DestinationParentFolderId)
                .Select(folder => folder.Order).DefaultIfEmpty(-1).Max() + 1;
            foreach (var oldFolder in beforeState.Folders.Where(folder => sourceIds.Contains(folder.Id)))
            {
                folders.Add(new FolderRecord(
                    duplicate.FolderIdMap[oldFolder.Id],
                    oldFolder.Id == source.Id
                        ? duplicate.DestinationParentFolderId
                        : duplicate.FolderIdMap[oldFolder.ParentId!.Value],
                    oldFolder.Id == source.Id ? duplicate.NewName : oldFolder.Name,
                    oldFolder.Id == source.Id ? nextRootOrder : oldFolder.Order,
                    oldFolder.Notes ?? string.Empty));
            }
        }

        foreach (var duplicate in sheetChanges)
        {
            if (!beforeState.Sheets.TryGetValue(duplicate.PageViewId, out var record) ||
                record.FolderId != duplicate.ExpectedFolderId || sourceFolderIds.Contains(record.FolderId))
                return Failure("selection.changed", $"The layout '{duplicate.ExpectedName}' moved before duplication.");
            if (folders.All(folder => folder.Id != duplicate.DestinationFolderId))
                return Failure("folder.destination_missing", "The paste destination no longer exists.");
        }

        var createdPages = new List<RhinoPageView>();
        try
        {
            var sheets = beforeState.Sheets.ToDictionary(pair => pair.Key, pair => pair.Value);
            var pages = document.Views.GetPageViews().ToDictionary(page => page.MainViewport.Id);
            var duplicatedSheetIds = new Dictionary<Guid, Guid>();

            foreach (var duplicate in folderChanges)
            {
                var sourceIds = duplicate.FolderIdMap.Keys.ToHashSet();
                foreach (var sourceSheet in beforeState.Sheets.Values
                             .Where(sheet => sourceIds.Contains(sheet.FolderId))
                             .OrderBy(sheet => sheet.FolderId).ThenBy(sheet => sheet.Order))
                {
                    var copy = DuplicatePage(document, pages, sourceSheet, createdPages);
                    duplicatedSheetIds[sourceSheet.PageViewId] = copy.MainViewport.Id;
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
                duplicatedSheetIds[sourceSheet.PageViewId] = copy.MainViewport.Id;
                var nextOrder = sheets.Values.Where(sheet => sheet.FolderId == duplicate.DestinationFolderId)
                    .Select(sheet => sheet.Order).DefaultIfEmpty(-1).Max() + 1;
                sheets[copy.MainViewport.Id] = DuplicateSheetRecord(
                    document,
                    sourceSheet,
                    copy,
                    duplicate.DestinationFolderId,
                    nextOrder);
            }

            var afterState = beforeState with { Folders = folders, Sheets = sheets };
            if (placement is not null)
            {
                afterState = afterState with
                {
                    ObserverCanvas = PlacePastedHierarchy(
                        document,
                        afterState,
                        folderChanges,
                        duplicatedSheetIds,
                        placement.TargetOrigin),
                };
            }

            _stateStore.Set(document, afterState);
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

    private ObserverCanvasState PlacePastedHierarchy(
        RhinoDoc document,
        DocumentState state,
        IReadOnlyList<DuplicateFolderChange> folderChanges,
        IReadOnlyDictionary<Guid, Guid> duplicatedSheetIds,
        ObserverPointRecord targetOrigin)
    {
        var origins = state.Canvas.FolderOrigins.ToDictionary(pair => pair.Key, pair => pair.Value);
        var placements = state.Canvas.SheetPlacements.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var folderMap in folderChanges.Select(change => change.FolderIdMap))
        {
            foreach (var pair in folderMap)
                if (state.Canvas.FolderOrigins.TryGetValue(pair.Key, out var origin))
                    origins[pair.Value] = origin;
        }
        foreach (var pair in duplicatedSheetIds)
            if (state.Canvas.SheetPlacements.TryGetValue(pair.Key, out var sheetPlacement))
                placements[pair.Value] = sheetPlacement;

        var canvas = state.Canvas with { FolderOrigins = origins, SheetPlacements = placements };
        var tentative = state with { ObserverCanvas = canvas };
        var snapshot = RhinoDocumentObserverSnapshotProvider.Capture(
            document,
            tentative,
            _revisionTracker.Current(document));
        var topFolderIds = folderChanges
            .Select(change => change.FolderIdMap[change.SourceFolderId])
            .ToArray();
        var coveredSourceSheetIds = folderChanges
            .SelectMany(change => change.FolderIdMap.Keys)
            .SelectMany(folderId => state.Sheets.Values
                .Where(sheet => sheet.FolderId == folderId)
                .Select(sheet => sheet.PageViewId))
            .ToHashSet();
        var standaloneSheetIds = duplicatedSheetIds
            .Where(pair => !coveredSourceSheetIds.Contains(pair.Key))
            .Select(pair => pair.Value)
            .ToArray();
        return new PasteCanvasPlacementPlanner().Place(
            snapshot,
            topFolderIds,
            standaloneSheetIds,
            targetOrigin);
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
            NamingBinding = null,
            DetailNamedViewAssignments = null,
        };
    }

}

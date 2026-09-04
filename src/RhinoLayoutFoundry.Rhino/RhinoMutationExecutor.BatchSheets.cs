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
    private OperationResult ApplyBatchUpdate(
        RhinoDoc document,
        OperationPlan plan,
        BatchUpdateSheetsChange update)
    {
        var pages = document.Views.GetPageViews()
            .Where(page => update.SheetPageViewIds.Contains(page.MainViewport.Id)).ToArray();
        if (pages.Length != update.SheetPageViewIds.Distinct().Count())
            return Failure("batch.sheet_missing", "An included layout no longer exists.");
        var detailsById = pages.SelectMany(page => page.GetDetailViews())
            .ToDictionary(detail => detail.Viewport.Id, detail => detail.Id);
        if ((update.DetailUpdates ?? []).Any(item => !detailsById.ContainsKey(item.DetailViewportId)))
            return Failure("batch.detail_missing", "A detail selected for editing no longer exists.");
        var before = pages.Select(page => new PagePropertiesBefore(
            page,
            page.PageName,
            page.PageWidth,
            page.PageHeight,
            page.GetDetailViews().Select(detail =>
                new DetailModeBefore(
                    detail.Id,
                    detail.Viewport.DisplayMode.Id,
                    detail.Attributes.LayerIndex,
                    new ViewportInfo(detail.Viewport))).ToArray())).ToArray();
        var stateBefore = WithCurrentPageRecords(document, _stateStore.Get(document));
        var titleBlocksBefore = pages.Select(page => CaptureTitleBlockBefore(
            document,
            page.MainViewport.Id,
            stateBefore.Sheets[page.MainViewport.Id].TitleBlock)).ToArray();
        var createdTitleBlockIds = new List<Guid>();
        DetailLayerResolution? dedicatedDetailLayer = null;
        var changesPaper = update.PaperWidth is not null || update.PaperHeight is not null;
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
                foreach (var page in pages.Where(page => update.NewNames.ContainsKey(page.MainViewport.Id)))
                    page.PageName = $"__FoundryBatch_{page.MainViewport.Id:N}";
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
            foreach (var detailUpdate in update.DetailUpdates ?? [])
            {
                // Committing a viewport replaces Rhino's native detail object. Never reuse
                // a wrapper captured before the sheet-level display-mode commit.
                var detail = FindCurrentDetail(document, detailsById[detailUpdate.DetailViewportId]);
                var assignedDisplayMode = detailUpdate.ChangeDisplayMode
                    ? detailUpdate.DisplayModeId
                    : detail.Viewport.DisplayMode.Id;
                var viewportChanged = false;
                if (detailUpdate.ChangeNamedView && !string.IsNullOrWhiteSpace(detailUpdate.NamedViewName))
                {
                    var namedViewName = detailUpdate.NamedViewName.Trim();
                    var namedViewIndex = document.NamedViews.FindByName(namedViewName);
                    if (namedViewIndex < 0 ||
                        !RestoreNamedViewForDetail(document, namedViewIndex, detail))
                        throw new InvalidOperationException(
                            $"Rhino could not apply named view '{namedViewName}' to a detail.");
                    viewportChanged = true;
                }
                if ((detailUpdate.ChangeDisplayMode || detailUpdate.ChangeNamedView) &&
                    assignedDisplayMode is { } detailDisplayModeId)
                {
                    using var detailDisplayMode = DisplayModeDescription.GetDisplayMode(detailDisplayModeId)
                        ?? throw new InvalidOperationException(
                            "A display mode selected for a detail is unavailable.");
                    detail.Viewport.DisplayMode = detailDisplayMode;
                    viewportChanged = true;
                }
                if (viewportChanged && !detail.CommitViewportChanges())
                    throw new InvalidOperationException("Rhino did not commit a detail assignment change.");
            }
            if (update.ChangeDetailLayer)
            {
                var details = pages.SelectMany(page => page.GetDetailViews()).ToArray();
                if (details.Length > 0)
                {
                    var detailLayerIndex = update.UseDedicatedDetailLayer
                        ? (dedicatedDetailLayer = ResolveDedicatedDetailLayer(
                            document, stateBefore.DedicatedDetailLayerId)).LayerIndex
                        : update.DetailLayerId is { } requestedLayerId
                            ? document.Layers.FindId(requestedLayerId)?.Index ??
                              throw new InvalidOperationException("The selected detail layer is unavailable.")
                            : document.Layers.CurrentLayerIndex;
                    foreach (var detail in details)
                    {
                        if (detail.Attributes.LayerIndex == detailLayerIndex) continue;
                        detail.Attributes.LayerIndex = detailLayerIndex;
                        if (!detail.CommitChanges())
                            throw new InvalidOperationException("Rhino did not commit a detail-layer change.");
                    }
                }
            }
            if (update.ChangeTitleBlock)
                ApplyBatchTitleBlocks(
                    document,
                    pages,
                    stateBefore,
                    update.BuiltInTitleBlock,
                    createdTitleBlockIds);
            else if (changesPaper)
                RebuildManagedTitleBlocks(document, pages, stateBefore, createdTitleBlockIds);
            var currentState = WithCurrentPageRecords(document, _stateStore.Get(document));
            if (update.NamingBindings is { Count: > 0 })
            {
                var sheets = currentState.Sheets.ToDictionary(pair => pair.Key, pair => pair.Value);
                foreach (var pair in update.NamingBindings)
                {
                    if (!sheets.TryGetValue(pair.Key, out var sheet))
                        throw new InvalidOperationException("A renamed layout record is unavailable.");
                    sheets[pair.Key] = sheet with { NamingBinding = pair.Value };
                }
                currentState = currentState with { Sheets = sheets };
            }
            if (update.NamingBindingRemovals is { Count: > 0 })
            {
                var sheets = currentState.Sheets.ToDictionary(pair => pair.Key, pair => pair.Value);
                foreach (var pageViewId in update.NamingBindingRemovals)
                    if (sheets.TryGetValue(pageViewId, out var sheet))
                        sheets[pageViewId] = sheet with { NamingBinding = null };
                currentState = currentState with { Sheets = sheets };
            }
            if (update.DetailUpdates is { Count: > 0 } &&
                update.DetailUpdates.Any(item => item.ChangeNamedView))
            {
                var pageByDetail = pages.SelectMany(page => page.GetDetailViews()
                        .Select(detail => (
                            DetailId: detail.Viewport.Id,
                            PageViewId: page.MainViewport.Id)))
                    .ToDictionary(item => item.DetailId, item => item.PageViewId);
                var sheets = currentState.Sheets.ToDictionary(pair => pair.Key, pair => pair.Value);
                foreach (var detailUpdate in update.DetailUpdates.Where(item => item.ChangeNamedView))
                {
                    var pageViewId = pageByDetail[detailUpdate.DetailViewportId];
                    if (!sheets.TryGetValue(pageViewId, out var sheet)) continue;
                    var assignments = sheet.DetailNamedViews
                        .ToDictionary(pair => pair.Key, pair => pair.Value);
                    if (string.IsNullOrWhiteSpace(detailUpdate.NamedViewName))
                        assignments.Remove(detailUpdate.DetailViewportId);
                    else
                        assignments[detailUpdate.DetailViewportId] = detailUpdate.NamedViewName.Trim();
                    SheetNamingBinding? namingBinding = sheet.NamingBinding;
                    if (namingBinding is not null)
                    {
                        var linkedAssignments = namingBinding.NamedViewAssignments.ToDictionary(pair => pair.Key, pair => pair.Value);
                        if (string.IsNullOrWhiteSpace(detailUpdate.NamedViewName))
                            linkedAssignments.Remove(detailUpdate.DetailViewportId);
                        else
                            linkedAssignments[detailUpdate.DetailViewportId] = detailUpdate.NamedViewName.Trim();
                        namingBinding = namingBinding with { NamedViewAssignments = linkedAssignments };
                    }
                    sheets[pageViewId] = sheet with
                    {
                        NamingBinding = namingBinding,
                        DetailNamedViews = assignments,
                    };
                }
                currentState = currentState with { Sheets = sheets };
            }
            if (update.ReplaceRevisionSchedule is not null || update.AppendRevision is not null)
            {
                var sheets = currentState.Sheets.ToDictionary(pair => pair.Key, pair => pair.Value);
                foreach (var page in pages)
                {
                    var record = sheets[page.MainViewport.Id];
                    var existingData = record.TitleBlockData ?? new SheetTitleBlockData(string.Empty, []);
                    var revisions = update.ReplaceRevisionSchedule?.ToArray() ??
                        existingData.Revisions.Concat(new[] { update.AppendRevision! }).ToArray();
                    sheets[page.MainViewport.Id] = record with
                    {
                        TitleBlockData = existingData with { Revisions = revisions },
                    };
                }
                currentState = currentState with { Sheets = sheets };
            }
            if (update.DestinationFolderId is { } destinationFolderId)
            {
                if (currentState.Folders.All(folder => folder.Id != destinationFolderId))
                    throw new InvalidOperationException("The destination folder is unavailable.");
                var sheets = currentState.Sheets.ToDictionary(pair => pair.Key, pair => pair.Value);
                var nextOrder = sheets.Values.Where(sheet => sheet.FolderId == destinationFolderId)
                    .Select(sheet => sheet.Order).DefaultIfEmpty(-1).Max() + 1;
                foreach (var pageViewId in update.SheetPageViewIds)
                {
                    var sheet = sheets[pageViewId];
                    if (sheet.FolderId == destinationFolderId) continue;
                    sheets[pageViewId] = sheet with
                    {
                        FolderId = destinationFolderId,
                        Order = nextOrder++,
                    };
                }
                currentState = currentState with { Sheets = sheets };
            }
            if (update.ChangeAppearanceState)
            {
                if (update.AppearanceStateId is { } appearanceStateId &&
                    currentState.AppearanceStates.All(state => state.Id != appearanceStateId))
                    throw new InvalidOperationException("The selected appearance state is unavailable.");
                var targets = update.SheetPageViewIds.Select(id =>
                    new HierarchyScope(HierarchyScopeKind.Sheet, id)).ToHashSet();
                var assignments = currentState.StateAssignments
                    .Where(assignment => !targets.Contains(assignment.Target)).ToList();
                if (update.AppearanceStateId is { } stateId)
                    assignments.AddRange(targets.Select(target =>
                        new AppearanceStateAssignment(Guid.NewGuid(), target, stateId)));
                currentState = currentState with { StateAssignments = assignments };
            }
            if (update.DetailUpdates is { Count: > 0 } &&
                update.DetailUpdates.Any(item => item.ChangeAppearanceState))
            {
                var detailTargets = update.DetailUpdates
                    .Where(item => item.ChangeAppearanceState)
                    .Select(item => new HierarchyScope(
                        HierarchyScopeKind.Detail,
                        item.DetailViewportId))
                    .ToHashSet();
                var assignments = currentState.StateAssignments
                    .Where(assignment => !detailTargets.Contains(assignment.Target)).ToList();
                foreach (var detailUpdate in update.DetailUpdates.Where(item => item.ChangeAppearanceState))
                {
                    if (detailUpdate.AppearanceStateId is not { } stateId) continue;
                    if (currentState.AppearanceStates.All(state => state.Id != stateId))
                        throw new InvalidOperationException(
                            "An appearance state selected for a detail is unavailable.");
                    assignments.Add(new AppearanceStateAssignment(
                        Guid.NewGuid(),
                        new HierarchyScope(HierarchyScopeKind.Detail, detailUpdate.DetailViewportId),
                        stateId));
                }
                currentState = currentState with { StateAssignments = assignments };
            }
            if (dedicatedDetailLayer is not null)
                currentState = currentState with
                {
                    DedicatedDetailLayerId = dedicatedDetailLayer.LayerId,
                };
            foreach (var page in pages.Where(page =>
                         update.NewNames.ContainsKey(page.MainViewport.Id) ||
                         update.ReplaceRevisionSchedule is not null ||
                         update.AppendRevision is not null))
                RefreshManagedTitleBlockAttributes(document, page, currentState);
            if (update.DestinationFolderId is not null || update.ChangeAppearanceState ||
                update.DetailUpdates?.Any(item => item.ChangeAppearanceState) == true)
            {
                var rootScope = new HierarchyScope(HierarchyScopeKind.Folder, currentState.RootFolderId);
                var rootRules = currentState.AppearanceRules.LastOrDefault(item => item.Scope == rootScope);
                var appearanceResult = ApplyViewportAppearanceRules(
                    document,
                    plan,
                    new SetHierarchyViewportRulesChange(rootScope, rootRules, rootRules),
                    afterAssignmentsOverride: currentState.StateAssignments,
                    afterDocumentStateOverride: currentState);
                if (!appearanceResult.Succeeded)
                    throw new InvalidOperationException(
                        appearanceResult.Diagnostics.FirstOrDefault()?.Message ??
                        "The resulting appearance state could not be applied.");
                return appearanceResult;
            }
            _stateStore.Set(document, currentState);
            document.Modified = true;
            _revisionTracker.Bump(document);
            document.Views.Redraw();
            return new OperationResult(true, plan.Diagnostics);
        }
        catch (Exception exception)
        {
            var recovery = new CompensationJournal();
            if (dedicatedDetailLayer is { Created: true })
                recovery.Register("detail layer", () =>
                {
                    if (!document.Layers.Delete(dedicatedDetailLayer.LayerId, quiet: true))
                        throw new InvalidOperationException("Rhino did not remove the new detail layer.");
                });
            foreach (var page in pages)
                recovery.Register($"title-block fields on '{page.PageName}'", () =>
                    RefreshManagedTitleBlockAttributes(document, page, _stateStore.Get(document)));
            recovery.Register("metadata and title blocks", () =>
            {
                if (update.ChangeTitleBlock || changesPaper)
                    RestoreTitleBlocks(document, stateBefore, titleBlocksBefore);
                else
                    _stateStore.Set(document, stateBefore);
            });
            foreach (var item in before)
            {
                recovery.Register($"layout '{item.Name}'", () =>
                {
                    item.Page.PageName = item.Name;
                    item.Page.PageWidth = item.Width;
                    item.Page.PageHeight = item.Height;
                });
                foreach (var detailBefore in item.DetailModes)
                    recovery.Register($"detail {detailBefore.ObjectId}", () =>
                        RestoreDetailBefore(document, detailBefore));
            }
            foreach (var id in createdTitleBlockIds)
                recovery.Register($"new title block {id}", () =>
                {
                    if (document.Objects.FindId(id) is not null && !document.Objects.Delete(id, true))
                        throw new InvalidOperationException("Rhino did not remove the new title block.");
                });
            var failures = recovery.Rollback();
            document.Views.Redraw();
            return Failure("batch.apply_failed", RecoveryMessage("Batch Apply", exception, failures));
        }
        finally
        {
            foreach (var item in before.SelectMany(page => page.DetailModes))
                item.Viewport.Dispose();
        }
    }

    private static DetailViewObject FindCurrentDetail(RhinoDoc document, Guid objectId) =>
        document.Objects.FindId(objectId) as DetailViewObject
        ?? throw new InvalidOperationException($"Detail {objectId} is no longer available.");

    private static void RestoreDetailBefore(RhinoDoc document, DetailModeBefore before)
    {
        var detail = FindCurrentDetail(document, before.ObjectId);
        if (detail.Attributes.LayerIndex != before.LayerIndex)
        {
            detail.Attributes.LayerIndex = before.LayerIndex;
            if (!detail.CommitChanges())
                throw new InvalidOperationException("Rhino did not restore the detail layer.");
            detail = FindCurrentDetail(document, before.ObjectId);
        }
        if (!detail.Viewport.SetViewProjection(before.Viewport, true))
            throw new InvalidOperationException("Rhino did not restore the detail camera.");
        using var mode = DisplayModeDescription.GetDisplayMode(before.DisplayModeId)
            ?? throw new InvalidOperationException("The original display mode is unavailable.");
        detail.Viewport.DisplayMode = mode;
        if (!detail.CommitViewportChanges())
            throw new InvalidOperationException("Rhino did not commit the restored detail viewport.");
    }

    private static string RecoveryMessage(string operation, Exception exception, IReadOnlyList<string> failures) =>
        failures.Count == 0
            ? $"{operation} failed; the previous values were restored: {exception.Message}"
            : $"{operation} failed: {exception.Message} Recovery is incomplete. " + string.Join(" ", failures);

    private static void RefreshManagedTitleBlockAttributes(
        RhinoDoc document,
        RhinoPageView page,
        DocumentState state)
    {
        if (!state.Sheets.TryGetValue(page.MainViewport.Id, out var record) ||
            record.TitleBlock?.BuiltInKind is null ||
            document.Objects.FindId(record.TitleBlock.InstanceObjectId) is not InstanceObject instance)
            return;
        var attributes = instance.Attributes.Duplicate();
        var data = record.TitleBlockData ?? new SheetTitleBlockData(string.Empty, []);
        var details = page.GetDetailViews().Select(detail => CaptureDetail(document, detail)).ToArray();
        foreach (var pair in TitleBlockValues(state.ProjectInfo, page.PageName, data, details))
            SetBlockAttributeValue(attributes, pair.Key, pair.Value);
        if (!document.Objects.ModifyAttributes(instance.Id, attributes, quiet: true))
            throw new InvalidOperationException($"Rhino could not refresh title-block fields on '{page.PageName}'.");
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
            detail.Id,
            detail.Viewport.DisplayMode.Id,
            detail.Attributes.LayerIndex,
            new ViewportInfo(detail.Viewport))).ToArray();
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
            var recovery = new CompensationJournal();
            foreach (var item in before)
                recovery.Register($"detail {item.ObjectId}", () => RestoreDetailBefore(document, item));
            var failures = recovery.Rollback();
            document.Views.Redraw();
            return Failure("inline.apply_failed", RecoveryMessage("The display-mode edit", exception, failures));
        }
        finally
        {
            foreach (var item in before) item.Viewport.Dispose();
        }
    }

    private void ApplyBatchTitleBlocks(
        RhinoDoc document,
        IReadOnlyList<RhinoPageView> pages,
        DocumentState stateBefore,
        BuiltInTitleBlockKind? requestedBuiltInKind,
        ICollection<Guid> createdTitleBlockIds)
    {

        var sheets = stateBefore.Sheets.ToDictionary(pair => pair.Key, pair => pair.Value);
        var newRoles = new Dictionary<Guid, TitleBlockRole?>();
        foreach (var page in pages)
        {
            TitleBlockRole? role = null;
            if (requestedBuiltInKind is { } managedKind)
            {
                var paper = new PaperRecipe(page.PageWidth, page.PageHeight, document.PageUnitSystem.ToString());
                var sheet = sheets[page.MainViewport.Id];
                var sheetData = sheet.TitleBlockData ?? new SheetTitleBlockData(string.Empty, []);
                var instanceId = CreateManagedTitleBlock(document, page, paper, managedKind,
                    stateBefore.ProjectInfo, sheetData,
                    page.GetDetailViews().Select(detail => CaptureDetail(document, detail)).ToArray());
                createdTitleBlockIds.Add(instanceId);
                var instance = document.Objects.FindId(instanceId) as InstanceObject
                    ?? throw new InvalidOperationException("Rhino could not resolve the generated title block.");
                role = new TitleBlockRole(InstanceObjectId: instanceId, InstanceDefinitionId: instance.InstanceDefinition.Id,
                    BuiltInKind: managedKind);
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

        var afterState = stateBefore with { Sheets = sheets };
        _stateStore.Set(document, afterState);
        foreach (var page in pages) RefreshManagedTitleBlockAttributes(document, page, afterState);
        DeleteUnusedGeneratedTitleBlockDefinitions(document);
    }

    private void RebuildManagedTitleBlocks(
        RhinoDoc document,
        IReadOnlyList<RhinoPageView> pages,
        DocumentState stateBefore,
        ICollection<Guid> createdTitleBlockIds)
    {
        var sheets = stateBefore.Sheets.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var page in pages)
        {
            var record = sheets[page.MainViewport.Id];
            if (record.TitleBlock?.BuiltInKind is not { } kind) continue;
            var paper = new PaperRecipe(page.PageWidth, page.PageHeight, document.PageUnitSystem.ToString());
            var sheetData = record.TitleBlockData ?? new SheetTitleBlockData(string.Empty, []);
            var replacementId = CreateManagedTitleBlock(document, page, paper, kind,
                stateBefore.ProjectInfo, sheetData,
                page.GetDetailViews().Select(detail => CaptureDetail(document, detail)).ToArray());
            createdTitleBlockIds.Add(replacementId);
            var replacement = document.Objects.FindId(replacementId) as InstanceObject
                ?? throw new InvalidOperationException("Rhino could not resolve the resized title block.");
            if (document.Objects.FindId(record.TitleBlock.InstanceObjectId) is not null &&
                !document.Objects.Delete(record.TitleBlock.InstanceObjectId, true))
                throw new InvalidOperationException($"Rhino could not replace the title block on '{page.PageName}'.");
            sheets[page.MainViewport.Id] = record with
            {
                TitleBlock = record.TitleBlock with
                {
                    InstanceObjectId = replacementId,
                    InstanceDefinitionId = replacement.InstanceDefinition.Id,
                    BuiltInKind = kind == BuiltInTitleBlockKind.FullWidthBottom
                        ? BuiltInTitleBlockKind.FullWidthBottom
                        : BuiltInTitleBlockKind.RightSidebar,
                },
            };
        }
        _stateStore.Set(document, stateBefore with { Sheets = sheets });
        DeleteUnusedGeneratedTitleBlockDefinitions(document);
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
        var recovery = new CompensationJournal();
        recovery.Register("title-block metadata", () =>
            _stateStore.Set(document, stateBefore with { Sheets = sheets }));
        foreach (var item in before)
            recovery.Register($"title block on layout {item.PageViewId}", () =>
            {
                var role = item.Role;
                if (role is not null)
                {
                    if (item.Attributes is null || item.DefinitionIndex is not { } definitionIndex)
                        throw new InvalidOperationException("The original title block could not be captured.");
                    if (document.Objects.FindId(role.InstanceObjectId) is null)
                    {
                        var restoredId = document.Objects.AddInstanceObject(definitionIndex, item.Transform, item.Attributes);
                        if (restoredId == Guid.Empty)
                            throw new InvalidOperationException("Rhino did not restore the title block.");
                        role = role with { InstanceObjectId = restoredId };
                    }
                    else if (!document.Objects.ModifyAttributes(role.InstanceObjectId, item.Attributes, quiet: true))
                        throw new InvalidOperationException("Rhino did not restore the title-block fields.");
                }
                sheets[item.PageViewId] = sheets[item.PageViewId] with { TitleBlock = role };
            });
        var failures = recovery.Rollback();
        if (failures.Count > 0)
            throw new InvalidOperationException(string.Join(" ", failures));
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

}

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
    private OperationResult ApplyViewportAppearanceRules(
        RhinoDoc document,
        OperationPlan plan,
        SetHierarchyViewportRulesChange change,
        IReadOnlyList<LayoutTemplateRegistration>? afterRegistrationsOverride = null,
        IReadOnlyList<AppearanceStateRecord>? afterStatesOverride = null,
        IReadOnlyList<AppearanceStateAssignment>? afterAssignmentsOverride = null,
        DocumentState? afterDocumentStateOverride = null)
    {
        var storedBeforeState = _stateStore.Get(document);
        var beforeState = WithCurrentPageRecords(document, storedBeforeState);
        var existing = beforeState.AppearanceRules.LastOrDefault(item => item.Scope == change.Scope);
        if (!RuleSetEquals(existing, change.ExpectedRules))
            return Failure("appearance.before_value_changed",
                "Viewport appearance rules changed before this edit was applied.");

        var baseAfterState = afterDocumentStateOverride ?? beforeState;
        var afterRules = baseAfterState.AppearanceRules
            .Where(item => item.Scope != change.Scope)
            .Concat(change.NewRules is null ? [] : [change.NewRules])
            .ToArray();
        var afterState = baseAfterState with
        {
            SchemaVersion = DocumentState.CurrentSchemaVersion,
            AppearanceRules = afterRules,
            TemplateRegistrations = afterRegistrationsOverride ?? baseAfterState.TemplateRegistrations,
            AppearanceStates = afterStatesOverride ?? baseAfterState.AppearanceStates,
            StateAssignments = afterAssignmentsOverride ?? baseAfterState.StateAssignments,
        };
        var pages = document.Views.GetPageViews();
        var details = afterStatesOverride is not null || afterAssignmentsOverride is not null
            ? pages.SelectMany(page => page.GetDetailViews()).ToArray()
            : AffectedDetails(change.Scope, beforeState, pages).ToArray();
        if (details.Length == 0)
            return ApplyStateOnlyChange(document, plan, storedBeforeState, afterState);

        var layerSnapshots = document.Layers
            .Where(layer => !layer.IsDeleted && !layer.IsReference)
            .ToDictionary(
                layer => layer.Id,
                layer => new LayerSnapshot(layer.Id,
                    layer.ParentLayerId == Guid.Empty ? null : layer.ParentLayerId,
                    layer.FullPath,
                    layer.IsVisible));
        var modelObjects = document.Objects
            .Where(item => item is not DetailViewObject &&
                           item.Attributes.Space == ActiveSpace.ModelSpace)
            .Select(item =>
            {
                var layer = document.Layers[item.Attributes.LayerIndex];
                return new ModelObjectSnapshot(item.Id,
                    string.IsNullOrWhiteSpace(item.Attributes.Name)
                        ? item.ObjectType.ToString()
                        : item.Attributes.Name,
                    layer.Id,
                    layer.FullPath,
                    item is InstanceObject);
            })
            .ToDictionary(item => item.Id);
        var beforeByScope = beforeState.AppearanceRules
            .GroupBy(item => item.Scope).ToDictionary(group => group.Key, group => group.Last());
        var afterByScope = afterRules
            .GroupBy(item => item.Scope).ToDictionary(group => group.Key, group => group.Last());
        var beforeStates = beforeState.AppearanceStates.ToDictionary(item => item.Id);
        var afterStates = afterState.AppearanceStates.ToDictionary(item => item.Id);
        var layerBefore = new Dictionary<Guid, Layer>();
        var objectBefore = new Dictionary<Guid, ObjectAttributes>();
        var undoRecord = document.BeginUndoRecord(plan.UndoDescription);
        if (undoRecord == 0)
            return Failure("operation.undo_unavailable", "Rhino could not start a dedicated undo record.");
        try
        {
            if (!document.AddCustomUndoEvent(plan.UndoDescription, OnUndoDocumentState,
                    new DocumentStateUndoTag(plan.UndoDescription, storedBeforeState)))
                throw new InvalidOperationException("Rhino could not register Foundry settings with Undo.");

            foreach (var detail in details)
            {
                var beforeScopes = ScopeChain(detail.Viewport.Id, beforeState, pages);
                var afterScopes = ScopeChain(detail.Viewport.Id, afterState, pages);
                var before = ViewportAppearanceResolver.Resolve(
                    beforeScopes, beforeByScope, layerSnapshots, modelObjects,
                    beforeStates, beforeState.StateAssignments);
                var after = ViewportAppearanceResolver.Resolve(
                    afterScopes, afterByScope, layerSnapshots, modelObjects,
                    afterStates, afterState.StateAssignments);
                foreach (var layerId in before.Layers.Keys.Concat(after.Layers.Keys).Distinct())
                {
                    var foundLayer = document.Layers.FindId(layerId);
                    if (foundLayer is null) continue;
                    var index = foundLayer.Index;
                    if (!layerBefore.ContainsKey(layerId))
                        layerBefore[layerId] = CopyLayer(document.Layers[index]);
                    var layer = CopyLayer(document.Layers[index]);
                    if (after.Layers.TryGetValue(layerId, out var visibility))
                    {
                        layer.SetPerViewportVisible(detail.Viewport.Id,
                            visibility == LayerVisibilityOverride.Visible);
                        layer.SetPerViewportPersistentVisibility(detail.Viewport.Id,
                            visibility == LayerVisibilityOverride.Visible);
                    }
                    else
                    {
                        layer.DeletePerViewportVisible(detail.Viewport.Id);
                        layer.UnsetPerViewportPersistentVisibility(detail.Viewport.Id);
                    }
                    if (!document.Layers.Modify(layer, index, quiet: true))
                        throw new InvalidOperationException($"Rhino could not update layer '{layer.FullPath}'.");
                }

                foreach (var objectId in before.Objects.Keys.Concat(after.Objects.Keys).Distinct())
                {
                    var item = document.Objects.FindId(objectId);
                    if (item is null) continue;
                    if (!objectBefore.ContainsKey(objectId))
                        objectBefore[objectId] = item.Attributes.Duplicate();
                    var attributes = item.Attributes.Duplicate();
                    if (after.Objects.TryGetValue(objectId, out var rule))
                    {
                        using var mode = DisplayModeDescription.GetDisplayMode(rule.DisplayModeId)
                            ?? throw new InvalidOperationException(
                                $"Display mode '{rule.DisplayModeName}' is unavailable.");
                        if (!RhinoObjectDisplayModeOverride.TrySet(
                                attributes,
                                mode,
                                detail.Viewport.Id))
                            throw new InvalidOperationException(
                                $"Rhino could not set an object display override on '{item.Id}'.");
                    }
                    else
                    {
                        attributes.RemoveDisplayModeOverride(detail.Viewport.Id);
                    }
                    if (!document.Objects.ModifyAttributes(item, attributes, quiet: true))
                        throw new InvalidOperationException(
                            $"Rhino could not update object display overrides on '{item.Id}'.");
                }
            }

            _stateStore.Set(document, afterState);
            document.Modified = true;
            _revisionTracker.Bump(document);
            document.Views.Redraw();
            return new OperationResult(true, plan.Diagnostics);
        }
        catch (Exception exception)
        {
            foreach (var pair in layerBefore)
            {
                var foundLayer = document.Layers.FindId(pair.Key);
                if (foundLayer is not null)
                    document.Layers.Modify(pair.Value, foundLayer.Index, quiet: true);
            }
            foreach (var pair in objectBefore)
            {
                var item = document.Objects.FindId(pair.Key);
                if (item is not null) document.Objects.ModifyAttributes(item, pair.Value, quiet: true);
            }
            _stateStore.Set(document, storedBeforeState);
            return Failure("appearance.apply_failed",
                $"Viewport appearance update failed and was rolled back: {exception.Message}");
        }
        finally
        {
            document.EndUndoRecord(undoRecord);
        }
    }

    private OperationResult ApplyTemplateRegistration(
        RhinoDoc document,
        OperationPlan plan,
SetLayoutTemplateRegistrationChange change)
    {
        var before = _stateStore.Get(document);
        var registrations = before.TemplateRegistrations.ToList();
        var failure = UpdateTemplateRegistration(registrations, change);
        if (failure is not null) return failure;
        return ApplyStateOnlyChange(
            document,
            plan,
before, before with { TemplateRegistrations = registrations.ToArray() });
    }

    private OperationResult ApplyAppearanceStateChanges(
        RhinoDoc document,
        OperationPlan plan)
    {
        var storedBefore = _stateStore.Get(document);
        var before = WithCurrentPageRecords(document, storedBefore);
        var resources = before.AppearanceStates.ToList();
        var assignments = before.StateAssignments.ToList();
        var statePlacements = before.Canvas.StatePlacements
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var operation in plan.Changes)
        {
            if (operation is SetAppearanceStateResourceChange resourceChange)
            {
                var current = resources.LastOrDefault(item => item.Id == resourceChange.StateId);
                if (current != resourceChange.ExpectedState)
                    return Failure("appearance_state.before_value_changed",
                        "The appearance state changed before this edit was applied.");
                if (current is not null) resources.Remove(current);
                if (resourceChange.NewState is not null) resources.Add(resourceChange.NewState);
                if (resourceChange.NewState is null)
                {
                    assignments.RemoveAll(item => item.StateId == resourceChange.StateId);
                    statePlacements.Remove(resourceChange.StateId);
                }
                else if (current is not null && current.FolderId != resourceChange.NewState.FolderId)
                {
                    statePlacements.Remove(resourceChange.StateId);
                }
            }
            else if (operation is SetAppearanceStateAssignmentChange assignmentChange)
            {
                var current = assignments.LastOrDefault(item => item.Target == assignmentChange.Target);
                if (current != assignmentChange.ExpectedAssignment)
                    return Failure("appearance_state.assignment_changed",
                        "The appearance-state assignment changed before this edit was applied.");
                assignments.RemoveAll(item => item.Target == assignmentChange.Target);
                if (assignmentChange.NewAssignment is not null)
                    assignments.Add(assignmentChange.NewAssignment);
            }
        }

        var isUnassignedResourceCreation = plan.Changes.Count > 0 && plan.Changes.All(change =>
            change is SetAppearanceStateResourceChange
            {
                ExpectedState: null,
                NewState: not null,
            });
        if (isUnassignedResourceCreation)
        {
            var after = before with
            {
                SchemaVersion = DocumentState.CurrentSchemaVersion,
                AppearanceStates = resources,
                StateAssignments = assignments,
                Canvas = before.Canvas with
                {
                    StatePlacements = statePlacements,
                },
            };
            return ApplyStateOnlyChange(document, plan, storedBefore, after);
        }

        var rootScope = new HierarchyScope(HierarchyScopeKind.Folder, before.RootFolderId);
        var rootRules = before.AppearanceRules.LastOrDefault(item => item.Scope == rootScope);
        return ApplyViewportAppearanceRules(
            document,
            plan,
            new SetHierarchyViewportRulesChange(rootScope, rootRules, rootRules),
            afterStatesOverride: resources,
            afterAssignmentsOverride: assignments,
            afterDocumentStateOverride: before with
            {
                Canvas = before.Canvas with
                {
                    StatePlacements = statePlacements,
                },
            });
    }

    private static OperationResult? UpdateTemplateRegistration(
        IList<LayoutTemplateRegistration> registrations,
SetLayoutTemplateRegistrationChange change)
    {
        var existing = registrations.LastOrDefault(item => item.Source == change.Source);
        if (existing != change.ExpectedRegistration)
            return Failure("template.before_value_changed",
"Template registration changed before this edit was applied.");
        if (existing is not null) registrations.Remove(existing);
        if (change.NewRegistration is not null) registrations.Add(change.NewRegistration);
        return null;
    }

    private static bool RuleSetEquals(
        HierarchyViewportRuleSet? first,
        HierarchyViewportRuleSet? second) =>
        first?.Scope == second?.Scope &&
        (first?.LayerRules ?? []).SequenceEqual(second?.LayerRules ?? []) &&
        (first?.ObjectDisplayRules ?? []).SequenceEqual(second?.ObjectDisplayRules ?? []);

    private static Layer CopyLayer(Layer source)
    {
        var copy = new Layer();
        copy.CopyAttributesFrom(source);
        return copy;
    }

    private static IEnumerable<DetailViewObject> AffectedDetails(
        HierarchyScope scope,
        DocumentState state,
        IReadOnlyList<RhinoPageView> pages)
    {
        if (scope.Kind == HierarchyScopeKind.Detail)
            return pages.SelectMany(page => page.GetDetailViews())
                .Where(detail => detail.Viewport.Id == scope.Id);
        if (scope.Kind == HierarchyScopeKind.Sheet)
            return pages.Where(page => page.MainViewport.Id == scope.Id)
                .SelectMany(page => page.GetDetailViews());

        var folderIds = FolderDescendants(scope.Id, state.Folders);
        var pageIds = state.Sheets.Values
            .Where(sheet => folderIds.Contains(sheet.FolderId))
            .Select(sheet => sheet.PageViewId)
            .ToHashSet();
        return pages.Where(page => pageIds.Contains(page.MainViewport.Id))
            .SelectMany(page => page.GetDetailViews());
    }

    private static IReadOnlyList<HierarchyScope> ScopeChain(
        Guid detailViewportId,
        DocumentState state,
        IReadOnlyList<RhinoPageView> pages)
    {
        var page = pages.First(page => page.GetDetailViews()
            .Any(detail => detail.Viewport.Id == detailViewportId));
        var sheetId = page.MainViewport.Id;
        var folderId = state.Sheets.GetValueOrDefault(sheetId)?.FolderId ?? state.RootFolderId;
        var folders = state.Folders.ToDictionary(folder => folder.Id);
        var folderChain = new List<Guid>();
        var visited = new HashSet<Guid>();
        while (visited.Add(folderId) && folders.TryGetValue(folderId, out var folder))
        {
            folderChain.Add(folder.Id);
            if (folder.ParentId is not { } parentId) break;
            folderId = parentId;
        }
        folderChain.Reverse();
        return folderChain.Select(id => new HierarchyScope(HierarchyScopeKind.Folder, id))
            .Append(new HierarchyScope(HierarchyScopeKind.Sheet, sheetId))
            .Append(new HierarchyScope(HierarchyScopeKind.Detail, detailViewportId))
            .ToArray();
    }

}

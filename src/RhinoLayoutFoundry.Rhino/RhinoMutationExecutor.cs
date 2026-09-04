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

// Operation families share one transaction owner. UI-thread dispatch and document guards live in the service.
internal sealed partial class RhinoMutationExecutor(
    DocumentRevisionTracker revisionTracker,
    DocumentStateStore stateStore,
    Action<OverviewInvalidation> overviewChanged)
{
    private const string DedicatedDetailLayerName = ".details";
    private readonly DocumentRevisionTracker _revisionTracker = revisionTracker;
    private readonly DocumentStateStore _stateStore = stateStore;
    private readonly Action<OverviewInvalidation> _overviewChanged = overviewChanged;

    internal OperationResult Apply(RhinoDoc document, OperationPlan plan)
    {
        return plan.Changes switch
        {
            [CreateNamedViewChange createNamedView] =>
                ApplyCreateNamedView(document, plan, createNamedView),
            [CreateClippingPlaneChange createClippingPlane] =>
                ApplyCreateClippingPlane(document, plan, createClippingPlane),
            [RenameSheetChange rename] => ApplyRename(document, plan, rename),
            [CreateSheetChange create] => ApplyCreateSheet(document, plan, create),
            [BatchUpdateSheetsChange update] => ApplyBatchUpdate(document, plan, update),
            [UpdateDetailDisplayModesChange updateDetails] => ApplyDetailDisplayModes(document, plan, updateDetails),
            [AssignNamedViewToDetailsChange assignNamedView] => ApplyNamedView(document, plan, assignNamedView),
            [CaptureSheetTemplateChange capture] => ApplyCaptureTemplate(document, plan, capture),
            [UpdateProjectInformationChange project] => ApplyProjectInformation(document, plan, project),
            [SetHierarchyViewportRulesChange appearance] =>
                ApplyViewportAppearanceRules(document, plan, appearance),
            [SetTemplateCapabilitiesChange capabilities] =>
                ApplyViewportTemplateCapabilities(document, plan, capabilities),
            _ when plan.Changes.Count > 0 && plan.Changes.All(change => change is
                SetAppearanceStateResourceChange or SetAppearanceStateAssignmentChange) =>
                ApplyAppearanceStateChanges(document, plan),
            _ when plan.Changes.Count > 0 && plan.Changes.All(change => change is DeleteSheetTemplateChange) =>
                ApplyDeleteTemplates(document, plan, plan.Changes.Cast<DeleteSheetTemplateChange>().ToArray()),
            _ when plan.Changes.All(change => change is DeleteFolderChange or DeleteSheetChange or
                SetAppearanceStateResourceChange) =>
                ApplyDeleteHierarchySelection(document, plan),
            _ when plan.Changes.All(change => change is DuplicateFolderChange or DuplicateSheetChange or PlacePastedHierarchyOnCanvasChange) =>
                ApplyDuplicateHierarchySelection(document, plan),
            _ when plan.Changes.All(change => change is CreateSheetFromTemplateChange) =>
                ApplyTemplateBatch(document, plan, plan.Changes.Cast<CreateSheetFromTemplateChange>().ToArray()),
            _ when plan.Changes.Count(change => change is AssignNamedViewToDetailsChange) == 1 &&
                   plan.Changes.All(change => change is AssignNamedViewToDetailsChange or UpdateLinkedSheetNamesChange) =>
                ApplyNamedView(
                    document,
                    plan,
                    plan.Changes.OfType<AssignNamedViewToDetailsChange>().Single(),
                    plan.Changes.OfType<UpdateLinkedSheetNamesChange>().SingleOrDefault()),
            _ when plan.Changes.All(IsDocumentStateChange) => ApplyDocumentStateChanges(document, plan),
            _ => Failure("operation.unsupported_plan", "The operation plan is not supported by this build."),
        };
    }

    private static bool IsDocumentStateChange(OperationChange change)
    {
        return change is AddFolderChange or RenameFolderChange or
            MoveSheetChange or MoveFolderChange or SetPrintInclusionChange or
            SetObserverCanvasStateChange or ReorderSheetsChange or
            ReorganizeHierarchyChange or SetTemplateCapabilitiesChange or
            SetCapabilityTemplateLinkChange or UpdateLinkedSheetNamesChange or
            UpdateHierarchyNotesChange;
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
        var stateBefore = _stateStore.Get(document);
        try
        {
            page.PageName = rename.NewName;
            if (!string.Equals(page.PageName, rename.NewName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Rhino did not retain the requested layout name.");
            }

            var stateAfter = stateBefore;
            if (rename.DetachNamingBinding && stateBefore.Sheets.TryGetValue(rename.PageViewId, out var record) &&
                record.NamingBinding is not null)
            {
                var sheets = stateBefore.Sheets.ToDictionary(pair => pair.Key, pair => pair.Value);
                sheets[rename.PageViewId] = record with { NamingBinding = null };
                stateAfter = stateBefore with { Sheets = sheets };
                _stateStore.SetCurrentSchema(document, stateAfter);
            }
            RefreshManagedTitleBlockAttributes(document, page, stateAfter);

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
            _stateStore.Set(document, stateBefore);

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
        var linkedNames = plan.Changes.OfType<UpdateLinkedSheetNamesChange>().SingleOrDefault();
        if (linkedNames is not null && ValidateLinkedSheetNames(document, linkedNames) is { } namingFailure)
            return namingFailure;
        var storedBeforeState = _stateStore.Get(document);
        var beforeState = plan.Changes.Any(change => change is SetPrintInclusionChange or ReorderSheetsChange or
            ReorganizeHierarchyChange or UpdateHierarchyNotesChange)
            ? WithCurrentPageRecords(document, storedBeforeState)
            : storedBeforeState;
        var folders = beforeState.Folders.ToList();
        var sheets = beforeState.Sheets.ToDictionary(pair => pair.Key, pair => pair.Value);
        var pageIds = document.Views.GetPageViews()
            .Select(page => page.MainViewport.Id)
            .ToHashSet();
        var templateRegistrations = beforeState.TemplateRegistrations.ToList();
        var templateLinks = beforeState.TemplateLinks.ToList();

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
                ReorganizeHierarchyChange reorganize => ApplyReorganizeHierarchy(
                    beforeState.RootFolderId, folders, sheets, reorganize),
                SetTemplateCapabilitiesChange templates => ApplyTemplateCapabilities(
                    templateRegistrations, templateLinks, templates),
                SetCapabilityTemplateLinkChange link => ApplyTemplateLink(templateLinks, link),
                UpdateLinkedSheetNamesChange naming => ApplyLinkedSheetBindings(sheets, naming),
                UpdateHierarchyNotesChange notes => ApplyHierarchyNotes(folders, sheets, notes),
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
            CapabilityTemplates = templateRegistrations.ToArray(),
            CapabilityLinks = templateLinks.ToArray(),
        };
        var undoRecord = document.BeginUndoRecord(plan.UndoDescription);
        if (undoRecord == 0)
        {
            return Failure(
                "operation.undo_unavailable",
                "Rhino could not start a dedicated undo record, so no hierarchy changes were made.");
        }

        var pageNamesBefore = CapturePageNames(document, linkedNames?.NewNames.Keys);
        try
        {
            _stateStore.Set(document, afterState);
            if (linkedNames is not null)
                ApplyLinkedPageNames(document, linkedNames.NewNames, afterState);
            var undoEvent = document.AddCustomUndoEvent(
                plan.UndoDescription,
                OnUndoDocumentState,
                new DocumentStateUndoTag(plan.UndoDescription, storedBeforeState, pageNamesBefore));
            if (!undoEvent)
                throw new InvalidOperationException(
                    "Rhino could not register hierarchy metadata with Undo.");
            document.Modified = true;
            _revisionTracker.Bump(document);
            if (linkedNames is not null) document.Views.Redraw();
            return new OperationResult(true, plan.Diagnostics);
        }
        catch (Exception exception)
        {
            RestorePageNames(document, pageNamesBefore, storedBeforeState);
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

    private static OperationResult? ValidateLinkedSheetNames(
        RhinoDoc document,
        UpdateLinkedSheetNamesChange change)
    {
        var pages = document.Views.GetPageViews().ToDictionary(page => page.MainViewport.Id);
        foreach (var pair in change.ExpectedNames)
        {
            if (!pages.TryGetValue(pair.Key, out var page))
                return Failure("linked_name.sheet_missing", "A linked layout no longer exists.");
            if (!string.Equals(page.PageName, pair.Value, StringComparison.Ordinal))
                return Failure("linked_name.before_value_changed",
                    $"The linked layout '{pair.Value}' was renamed before the source change was applied.");
        }

        if (change.NewNames.Values.Any(string.IsNullOrWhiteSpace))
            return Failure("linked_name.empty", "A linked naming rule produced an empty layout name.");
        var duplicates = pages.Values
            .Select(page => change.NewNames.GetValueOrDefault(page.MainViewport.Id, page.PageName))
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        return duplicates is null
            ? null
            : Failure("linked_name.duplicate",
                $"The linked naming rules would duplicate layout name '{duplicates.Key}'.");
    }

    private static OperationResult? ApplyLinkedSheetBindings(
        IDictionary<Guid, SheetRecord> sheets,
        UpdateLinkedSheetNamesChange change)
    {
        foreach (var pair in change.NewBindings)
        {
            if (!sheets.TryGetValue(pair.Key, out var sheet))
                return Failure("linked_name.sheet_missing", "A linked layout record no longer exists.");
            sheets[pair.Key] = sheet with { NamingBinding = pair.Value };
        }
        return null;
    }

    private static IReadOnlyDictionary<Guid, string> CapturePageNames(
        RhinoDoc document,
        IEnumerable<Guid>? pageViewIds)
    {
        if (pageViewIds is null) return new Dictionary<Guid, string>();
        var ids = pageViewIds.ToHashSet();
        return document.Views.GetPageViews()
            .Where(page => ids.Contains(page.MainViewport.Id))
            .ToDictionary(page => page.MainViewport.Id, page => page.PageName);
    }

    private void ApplyLinkedPageNames(
        RhinoDoc document,
        IReadOnlyDictionary<Guid, string> names,
        DocumentState state)
    {
        if (names.Count == 0) return;
        var pages = document.Views.GetPageViews()
            .Where(page => names.ContainsKey(page.MainViewport.Id))
            .ToDictionary(page => page.MainViewport.Id);
        foreach (var page in pages.Values)
            page.PageName = $"__FoundryLinked_{page.MainViewport.Id:N}";
        foreach (var pair in names)
        {
            var page = pages[pair.Key];
            page.PageName = pair.Value;
            if (!string.Equals(page.PageName, pair.Value, StringComparison.Ordinal))
                throw new InvalidOperationException($"Rhino did not retain linked layout name '{pair.Value}'.");
        }
        foreach (var page in pages.Values) RefreshManagedTitleBlockAttributes(document, page, state);
    }

    private void RestorePageNames(
        RhinoDoc document,
        IReadOnlyDictionary<Guid, string> names,
        DocumentState state)
    {
        if (names.Count == 0) return;
        var pages = document.Views.GetPageViews()
            .Where(page => names.ContainsKey(page.MainViewport.Id))
            .ToDictionary(page => page.MainViewport.Id);
        foreach (var page in pages.Values)
            page.PageName = $"__FoundryRestore_{page.MainViewport.Id:N}";
        foreach (var pair in names)
            if (pages.TryGetValue(pair.Key, out var page)) page.PageName = pair.Value;
        foreach (var page in pages.Values)
        {
            try { RefreshManagedTitleBlockAttributes(document, page, state); }
            catch { /* Preserve rollback progress even if a managed block is damaged. */ }
        }
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

    internal static UnitSystem ParseUnitSystem(string value) =>
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
        var currentPageNames = CapturePageNames(document, tag.PageNames?.Keys);
        document.AddCustomUndoEvent(
            tag.Description,
            OnUndoDocumentState,
            new DocumentStateUndoTag(tag.Description, currentState, currentPageNames));
        _stateStore.Set(document, tag.State);
        if (tag.PageNames is { Count: > 0 })
        {
            RestorePageNames(document, tag.PageNames, tag.State);
            document.Views.Redraw();
        }
        document.Modified = true;
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

    private sealed record DocumentStateUndoTag(
        string Description,
        DocumentState State,
        IReadOnlyDictionary<Guid, string>? PageNames = null);

    private sealed record DetailLayerResolution(int LayerIndex, Guid LayerId, bool Created);

    private sealed record PagePropertiesBefore(
        RhinoPageView Page,
        string Name,
        double Width,
        double Height,
        IReadOnlyList<DetailModeBefore> DetailModes);

    private sealed record DetailModeBefore(
        DetailViewObject Detail,
        Guid DisplayModeId,
        int LayerIndex,
        ViewportInfo Viewport);

    private sealed record TitleBlockBefore(
        Guid PageViewId,
        TitleBlockRole? Role,
        int? DefinitionIndex,
        Transform Transform,
        ObjectAttributes? Attributes);
}

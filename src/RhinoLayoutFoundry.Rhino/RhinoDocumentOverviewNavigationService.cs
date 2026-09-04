using Rhino;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Rhino;

internal sealed class RhinoDocumentOverviewNavigationService : IDocumentOverviewNavigationService
{
    private readonly DocumentStateStore _stateStore;
    private readonly DocumentRevisionTracker _revisionTracker;

    public RhinoDocumentOverviewNavigationService(
        DocumentStateStore stateStore,
        DocumentRevisionTracker revisionTracker)
    {
        _stateStore = stateStore;
        _revisionTracker = revisionTracker;
    }

    public OverviewNavigationResult Navigate(OverviewNavigationTarget target)
    {
        var document = RhinoDoc.ActiveDoc;
        if (document is null)
        {
            return new OverviewNavigationResult(false, "No active Rhino document.");
        }

        var page = document.Views.GetPageViews()
            .FirstOrDefault(candidate => candidate.MainViewport.Id == target.SheetPageViewId);
        if (page is null)
        {
            return new OverviewNavigationResult(false, "That layout sheet no longer exists.");
        }

        document.Views.ActiveView = page;
        if (target.DetailViewportId is { } detailId)
        {
            if (!page.SetActiveDetail(detailId))
            {
                return new OverviewNavigationResult(false, "That detail viewport no longer exists.");
            }
        }
        else
        {
            page.SetPageAsActive();
        }

        page.Redraw();
        return new OverviewNavigationResult(true);
    }

    public OverviewNavigationResult DuplicateSheet(Guid sheetPageViewId)
    {
        var lookup = FindPage(sheetPageViewId);
        if (lookup.Error is not null)
        {
            return lookup.Error;
        }

        var document = lookup.Document!;
        var source = lookup.Page!;
        var beforeState = _stateStore.Get(document);
        var duplicate = source.Duplicate(duplicatePageGeometry: true);
        if (duplicate is null)
        {
            return new OverviewNavigationResult(false, "Rhino could not duplicate that layout.");
        }

        var sheets = beforeState.Sheets.ToDictionary(pair => pair.Key, pair => pair.Value);
        var sourceRecord = sheets.GetValueOrDefault(sheetPageViewId) ?? new SheetRecord(
            sheetPageViewId,
            beforeState.RootFolderId,
            0,
            [],
            new Dictionary<string, string>(StringComparer.Ordinal),
            null);
        var nextOrder = sheets.Values
            .Where(sheet => sheet.FolderId == sourceRecord.FolderId)
            .Select(sheet => sheet.Order)
            .DefaultIfEmpty(-1)
            .Max() + 1;
        sheets[duplicate.MainViewport.Id] = sourceRecord with
        {
            PageViewId = duplicate.MainViewport.Id,
            Order = nextOrder,
        };
        _stateStore.Set(document, _stateStore.Reconcile(document, beforeState with { Sheets = sheets }));
        document.Modified = true;
        _revisionTracker.Bump(document);
        document.Views.ActiveView = duplicate;
        duplicate.SetPageAsActive();
        duplicate.Redraw();
        return new OverviewNavigationResult(true);
    }

    public OverviewNavigationResult DeleteSheet(Guid sheetPageViewId)
    {
        var lookup = FindPage(sheetPageViewId);
        if (lookup.Error is not null)
        {
            return lookup.Error;
        }

        var document = lookup.Document!;
        var page = lookup.Page!;
        if (!page.Close())
        {
            return new OverviewNavigationResult(false, "Rhino could not delete that layout.");
        }

        var beforeState = _stateStore.Get(document);
        var sheets = beforeState.Sheets
            .Where(pair => pair.Key != sheetPageViewId)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        _stateStore.Set(document, _stateStore.Reconcile(document, beforeState with { Sheets = sheets }));
        document.Modified = true;
        _revisionTracker.Bump(document);
        document.Views.Redraw();
        return new OverviewNavigationResult(true);
    }

    public OverviewNavigationResult RenameSheet(Guid sheetPageViewId, string newName)
    {
        var lookup = FindPage(sheetPageViewId);
        if (lookup.Error is not null)
        {
            return lookup.Error;
        }

        var document = lookup.Document!;
        var page = lookup.Page!;
        var trimmedName = newName.Trim();
        if (trimmedName.Length == 0)
        {
            return new OverviewNavigationResult(false, "Enter a name for the layout.");
        }

        if (document.Views.GetPageViews().Any(candidate =>
                candidate.MainViewport.Id != sheetPageViewId &&
                string.Equals(candidate.PageName, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            return new OverviewNavigationResult(false, $"A layout named '{trimmedName}' already exists.");
        }

        var beforeName = page.PageName;
        var beforeState = _stateStore.Get(document);
        page.PageName = trimmedName;
        if (!string.Equals(page.PageName, trimmedName, StringComparison.Ordinal))
        {
            return new OverviewNavigationResult(false, "Rhino did not retain the requested layout name.");
        }

        try
        {
            if (beforeState.Sheets.TryGetValue(sheetPageViewId, out var record) && record.NamingBinding is not null)
            {
                var sheets = beforeState.Sheets.ToDictionary(pair => pair.Key, pair => pair.Value);
                sheets[sheetPageViewId] = record with { NamingBinding = null };
                _stateStore.SetCurrentSchema(document, beforeState with { Sheets = sheets });
            }
        }
        catch (Exception exception)
        {
            page.PageName = beforeName;
            _stateStore.Set(document, beforeState);
            return new OverviewNavigationResult(false,
                $"The layout rename was restored because its naming link could not be detached: {exception.Message}");
        }

        document.Modified = true;
        _revisionTracker.Bump(document);
        page.Redraw();
        return new OverviewNavigationResult(true);
    }

    public OverviewNavigationResult RunSheetCommand(Guid sheetPageViewId, LayoutSheetCommand command)
    {
        var lookup = FindPage(sheetPageViewId);
        if (lookup.Error is not null)
        {
            return lookup.Error;
        }

        lookup.Document!.Views.ActiveView = lookup.Page;
        lookup.Page!.SetPageAsActive();
        var script = command switch
        {
            LayoutSheetCommand.NewDetail => "_Detail",
            LayoutSheetCommand.Print => "_Print",
            LayoutSheetCommand.Properties => "_Properties",
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };
        return RhinoApp.RunScript(script, echo: false)
            ? new OverviewNavigationResult(true)
            : new OverviewNavigationResult(false, $"Rhino could not start {command}.");
    }

    private (RhinoDoc? Document, global::Rhino.Display.RhinoPageView? Page, OverviewNavigationResult? Error)
        FindPage(Guid sheetPageViewId)
    {
        var document = RhinoDoc.ActiveDoc;
        if (document is null)
        {
            return (null, null, new OverviewNavigationResult(false, "No active Rhino document."));
        }

        if (!_stateStore.CanWrite(document))
            return (document, null, new OverviewNavigationResult(false, _stateStore.Diagnostic(document)!));

        var page = document.Views.GetPageViews()
            .FirstOrDefault(candidate => candidate.MainViewport.Id == sheetPageViewId);
        return page is null
            ? (document, null, new OverviewNavigationResult(false, "That layout sheet no longer exists."))
            : (document, page, null);
    }
}

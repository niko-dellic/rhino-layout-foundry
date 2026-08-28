using Rhino;
using Rhino.Commands;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.DocObjects.Tables;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Rhino;

internal sealed class RhinoDocumentEventBridge : IDisposable
{
    private readonly Action<OverviewInvalidation> _activeDocumentChanged;
    private readonly DocumentRevisionTracker _revisionTracker;
    private bool _isStarted;

    public RhinoDocumentEventBridge(
        DocumentRevisionTracker revisionTracker,
        Action<OverviewInvalidation> activeDocumentChanged)
    {
        _revisionTracker = revisionTracker ?? throw new ArgumentNullException(nameof(revisionTracker));
        _activeDocumentChanged = activeDocumentChanged
            ?? throw new ArgumentNullException(nameof(activeDocumentChanged));
    }

    public void Start()
    {
        if (_isStarted)
        {
            return;
        }

        RhinoView.Create += OnViewStructureChanged;
        RhinoView.Destroy += OnViewStructureChanged;
        RhinoView.SetActive += OnActiveViewChanged;
        RhinoView.Rename += OnViewStructureChanged;
        RhinoPageView.PageViewPropertiesChange += OnPageViewPropertiesChanged;
        Command.EndCommand += OnCommandEnded;
        Command.UndoRedo += OnUndoRedo;
        RhinoDoc.EndSaveDocument += OnDocumentSaved;
        RhinoDoc.DocumentPropertiesChanged += OnDocumentChanged;
        RhinoDoc.AddRhinoObject += OnObjectChanged;
        RhinoDoc.ReplaceRhinoObject += OnObjectReplaced;
        RhinoDoc.DeleteRhinoObject += OnObjectChanged;
        RhinoDoc.UndeleteRhinoObject += OnObjectChanged;
        RhinoDoc.ModifyObjectAttributes += OnObjectAttributesChanged;
        RhinoDoc.LayerTableEvent += OnLayerChanged;
        RhinoDoc.InstanceDefinitionTableEvent += OnInstanceDefinitionChanged;
        RhinoDoc.DimensionStyleTableEvent += OnDimensionStyleChanged;
        RhinoDoc.HatchPatternTableEvent += OnHatchPatternChanged;
        RhinoDoc.LinetypeTableEvent += OnLinetypeChanged;
        _isStarted = true;
    }

    public void Dispose()
    {
        if (!_isStarted)
        {
            return;
        }

        RhinoView.Create -= OnViewStructureChanged;
        RhinoView.Destroy -= OnViewStructureChanged;
        RhinoView.SetActive -= OnActiveViewChanged;
        RhinoView.Rename -= OnViewStructureChanged;
        RhinoPageView.PageViewPropertiesChange -= OnPageViewPropertiesChanged;
        Command.EndCommand -= OnCommandEnded;
        Command.UndoRedo -= OnUndoRedo;
        RhinoDoc.EndSaveDocument -= OnDocumentSaved;
        RhinoDoc.DocumentPropertiesChanged -= OnDocumentChanged;
        RhinoDoc.AddRhinoObject -= OnObjectChanged;
        RhinoDoc.ReplaceRhinoObject -= OnObjectReplaced;
        RhinoDoc.DeleteRhinoObject -= OnObjectChanged;
        RhinoDoc.UndeleteRhinoObject -= OnObjectChanged;
        RhinoDoc.ModifyObjectAttributes -= OnObjectAttributesChanged;
        RhinoDoc.LayerTableEvent -= OnLayerChanged;
        RhinoDoc.InstanceDefinitionTableEvent -= OnInstanceDefinitionChanged;
        RhinoDoc.DimensionStyleTableEvent -= OnDimensionStyleChanged;
        RhinoDoc.HatchPatternTableEvent -= OnHatchPatternChanged;
        RhinoDoc.LinetypeTableEvent -= OnLinetypeChanged;
        _isStarted = false;
    }

    private void OnViewStructureChanged(object? sender, ViewEventArgs eventArgs)
    {
        Track(
            eventArgs.View.Document,
            OverviewInvalidationKind.Hierarchy |
            OverviewInvalidationKind.Diagnostics |
            OverviewInvalidationKind.Thumbnails,
            eventArgs.View.MainViewport.Id);
    }

    private void OnActiveViewChanged(object? sender, ViewEventArgs eventArgs)
    {
        Track(
            eventArgs.View.Document,
            OverviewInvalidationKind.ActiveView,
            eventArgs.View.MainViewport.Id);
    }

    private void OnPageViewPropertiesChanged(
        object? sender,
        PageViewPropertiesChangeEventArgs eventArgs)
    {
        Track(
            eventArgs.Document,
            OverviewInvalidationKind.Hierarchy |
            OverviewInvalidationKind.Metadata |
            OverviewInvalidationKind.Diagnostics |
            OverviewInvalidationKind.Thumbnails);
    }

    private void OnCommandEnded(object? sender, CommandEventArgs eventArgs)
    {
        Track(eventArgs.Document, OverviewInvalidationKind.All);
    }

    private void OnUndoRedo(object? sender, UndoRedoEventArgs eventArgs)
    {
        Track(RhinoDoc.ActiveDoc, OverviewInvalidationKind.All);
    }

    private void OnDocumentSaved(object? sender, DocumentSaveEventArgs eventArgs)
    {
        Track(
            eventArgs.Document,
            OverviewInvalidationKind.DocumentIdentity |
            OverviewInvalidationKind.Metadata);
    }

    private void OnDocumentChanged(object? sender, DocumentEventArgs eventArgs)
    {
        Track(
            eventArgs.Document,
            OverviewInvalidationKind.DocumentIdentity |
            OverviewInvalidationKind.Metadata |
            OverviewInvalidationKind.Diagnostics |
            OverviewInvalidationKind.Thumbnails);
    }

    private void OnObjectChanged(object? sender, RhinoObjectEventArgs eventArgs)
    {
        Track(
            eventArgs.TheObject.Document,
            OverviewInvalidationKind.Thumbnails);
    }

    private void OnObjectAttributesChanged(
        object? sender,
        RhinoModifyObjectAttributesEventArgs eventArgs)
    {
        Track(
            eventArgs.Document,
            OverviewInvalidationKind.Thumbnails);
    }

    private void OnObjectReplaced(object? sender, RhinoReplaceObjectEventArgs eventArgs) =>
        Track(eventArgs.Document, OverviewInvalidationKind.Thumbnails);

    private void OnLayerChanged(object? sender, LayerTableEventArgs eventArgs) =>
        Track(eventArgs.Document,
            OverviewInvalidationKind.Metadata | OverviewInvalidationKind.Thumbnails);

    private void OnInstanceDefinitionChanged(
        object? sender,
        InstanceDefinitionTableEventArgs eventArgs) =>
        Track(eventArgs.Document,
            OverviewInvalidationKind.Metadata |
            OverviewInvalidationKind.Diagnostics |
            OverviewInvalidationKind.Thumbnails);

    private void OnDimensionStyleChanged(object? sender, DimStyleTableEventArgs eventArgs) =>
        Track(eventArgs.Document, OverviewInvalidationKind.Thumbnails);

    private void OnHatchPatternChanged(object? sender, HatchPatternTableEventArgs eventArgs) =>
        Track(eventArgs.Document, OverviewInvalidationKind.Thumbnails);

    private void OnLinetypeChanged(object? sender, LinetypeTableEventArgs eventArgs) =>
        Track(eventArgs.Document, OverviewInvalidationKind.Thumbnails);

    private void Track(
        RhinoDoc? document,
        OverviewInvalidationKind kind,
        Guid? entityId = null)
    {
        if (document is null)
        {
            return;
        }

        _revisionTracker.Bump(document);
        if (RhinoDoc.ActiveDoc?.RuntimeSerialNumber == document.RuntimeSerialNumber)
        {
            _activeDocumentChanged(new OverviewInvalidation(
                document.RuntimeSerialNumber,
                kind,
                entityId is { } id && id != Guid.Empty
                    ? new HashSet<Guid> { id }
                    : null));
        }
    }

}

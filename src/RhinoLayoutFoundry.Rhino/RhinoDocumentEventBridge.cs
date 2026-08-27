using Rhino;
using Rhino.Commands;
using Rhino.Display;
using Rhino.DocObjects;

namespace RhinoLayoutFoundry.Rhino;

internal sealed class RhinoDocumentEventBridge : IDisposable
{
    private readonly Action _activeDocumentChanged;
    private readonly DocumentRevisionTracker _revisionTracker;
    private bool _isStarted;

    public RhinoDocumentEventBridge(
        DocumentRevisionTracker revisionTracker,
        Action activeDocumentChanged)
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

        RhinoView.Create += OnViewChanged;
        RhinoView.Destroy += OnViewChanged;
        RhinoView.SetActive += OnViewChanged;
        RhinoView.Rename += OnViewChanged;
        RhinoView.Modified += OnViewChanged;
        RhinoPageView.PageViewPropertiesChange += OnPageViewPropertiesChanged;
        Command.EndCommand += OnCommandEnded;
        Command.UndoRedo += OnUndoRedo;
        RhinoDoc.DocumentPropertiesChanged += OnDocumentChanged;
        RhinoDoc.AddRhinoObject += OnObjectChanged;
        RhinoDoc.DeleteRhinoObject += OnObjectChanged;
        RhinoDoc.UndeleteRhinoObject += OnObjectChanged;
        RhinoDoc.ModifyObjectAttributes += OnObjectAttributesChanged;
        _isStarted = true;
    }

    public void Dispose()
    {
        if (!_isStarted)
        {
            return;
        }

        RhinoView.Create -= OnViewChanged;
        RhinoView.Destroy -= OnViewChanged;
        RhinoView.SetActive -= OnViewChanged;
        RhinoView.Rename -= OnViewChanged;
        RhinoView.Modified -= OnViewChanged;
        RhinoPageView.PageViewPropertiesChange -= OnPageViewPropertiesChanged;
        Command.EndCommand -= OnCommandEnded;
        Command.UndoRedo -= OnUndoRedo;
        RhinoDoc.DocumentPropertiesChanged -= OnDocumentChanged;
        RhinoDoc.AddRhinoObject -= OnObjectChanged;
        RhinoDoc.DeleteRhinoObject -= OnObjectChanged;
        RhinoDoc.UndeleteRhinoObject -= OnObjectChanged;
        RhinoDoc.ModifyObjectAttributes -= OnObjectAttributesChanged;
        _isStarted = false;
    }

    private void OnViewChanged(object? sender, ViewEventArgs eventArgs)
    {
        Track(eventArgs.View.Document);
    }

    private void OnPageViewPropertiesChanged(
        object? sender,
        PageViewPropertiesChangeEventArgs eventArgs)
    {
        Track(eventArgs.Document);
    }

    private void OnCommandEnded(object? sender, CommandEventArgs eventArgs)
    {
        Track(eventArgs.Document);
    }

    private void OnUndoRedo(object? sender, UndoRedoEventArgs eventArgs)
    {
        Track(RhinoDoc.ActiveDoc);
    }

    private void OnDocumentChanged(object? sender, DocumentEventArgs eventArgs)
    {
        Track(eventArgs.Document);
    }

    private void OnObjectChanged(object? sender, RhinoObjectEventArgs eventArgs)
    {
        Track(eventArgs.TheObject.Document);
    }

    private void OnObjectAttributesChanged(
        object? sender,
        RhinoModifyObjectAttributesEventArgs eventArgs)
    {
        Track(eventArgs.Document);
    }

    private void Track(RhinoDoc? document)
    {
        if (document is null)
        {
            return;
        }

        _revisionTracker.Bump(document);
        if (RhinoDoc.ActiveDoc?.RuntimeSerialNumber == document.RuntimeSerialNumber)
        {
            _activeDocumentChanged();
        }
    }

}

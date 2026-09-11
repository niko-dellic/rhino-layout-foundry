using System.Runtime.InteropServices;
using Eto.Forms;
using Rhino.UI;
using Rhino;

namespace RhinoLayoutFoundry.UI;

/// <summary>The Rhino-owned shell; document UI is released when its document closes.</summary>
[Guid("c43e26dd-b64b-454b-8b50-a10560e5045f")]
public sealed class LayoutFoundryPanel : Panel, IPanel
{
    private LayoutFoundryWorkspace? _workspace;
    private bool _documentClosed;
    private readonly uint _documentSerialNumber;

    public LayoutFoundryPanel() : this(RhinoDoc.ActiveDoc?.RuntimeSerialNumber ?? 0) { }

    // Rhino discovers this constructor for per-document panels, including panels
    // created for a document that is not currently active.
    public LayoutFoundryPanel(uint documentSerialNumber)
    {
        _documentSerialNumber = documentSerialNumber;
        Content = _workspace = new LayoutFoundryWorkspace();
        RhinoDoc.CloseDocument += OnDocumentClosed;
    }

    private void OnDocumentClosed(object? sender, DocumentEventArgs args)
    {
        if (args.Document.RuntimeSerialNumber == _documentSerialNumber) ReleaseWorkspace();
    }

    public void ShowListView() => _workspace?.ShowListView();
    public void ShowThumbnailView() => _workspace?.ShowThumbnailView();
    public void ShowCanvasView() => _workspace?.ShowCanvasView();
    public bool TryInvokeCreateAction(string actionId) => _workspace?.TryInvokeCreateAction(actionId) ?? false;

    void IPanel.PanelShown(uint documentSerialNumber, ShowPanelReason reason) { }
    void IPanel.PanelHidden(uint documentSerialNumber, ShowPanelReason reason) { }
    void IPanel.PanelClosing(uint documentSerialNumber, bool onCloseDocument)
    {
        // Rhino may retain the host instance after closing a Mac document.
        // Hiding a tab or switching documents must preserve the live workspace.
        if (onCloseDocument) ReleaseWorkspace();
    }

    private void ReleaseWorkspace()
    {
        if (_documentClosed) return;
        _documentClosed = true;
        RhinoDoc.CloseDocument -= OnDocumentClosed;
        var workspace = _workspace;
        _workspace = null;
        Content = null;
        workspace?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed) ReleaseWorkspace();
        base.Dispose(disposing);
    }
}

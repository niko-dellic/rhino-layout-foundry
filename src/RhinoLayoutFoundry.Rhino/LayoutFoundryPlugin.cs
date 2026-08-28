using Rhino;
using Rhino.FileIO;
using Rhino.PlugIns;
using Rhino.UI;
using RhinoLayoutFoundry.Core.Overview;
using RhinoLayoutFoundry.UI;

namespace RhinoLayoutFoundry.Rhino;

public sealed class LayoutFoundryPlugin : PlugIn
{
    private readonly DocumentStateStore _stateStore = new();
    private readonly DocumentRevisionTracker _revisionTracker = new();
    private RhinoDocumentEventBridge? _eventBridge;
    private System.Drawing.Icon? _panelIcon;

    public LayoutFoundryPlugin()
    {
        Instance = this;
    }

    public static LayoutFoundryPlugin? Instance { get; private set; }

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        var snapshotProvider = new RhinoDocumentSnapshotProvider(_stateStore, _revisionTracker);
        var mutationService = new RhinoDocumentMutationService(
            _revisionTracker,
            _stateStore,
            LayoutFoundryUiHost.NotifyOverviewChanged);
        LayoutFoundryUiHost.Configure(
            new RhinoDocumentOverviewProvider(_stateStore),
            snapshotProvider,
            mutationService,
            new RhinoDocumentOverviewNavigationService(_stateStore, _revisionTracker),
            new RhinoLayoutPdfExportService(),
            new RhinoLayoutPackageService(
                _stateStore,
                _revisionTracker,
                () => LayoutFoundryUiHost.NotifyOverviewChanged(OverviewInvalidation.All)),
            new RhinoDocumentThumbnailProvider(),
            new RhinoNamedViewThumbnailProvider(),
            new RhinoMutationCapabilityProvider(),
            new RhinoTemplateCaptureContextProvider(),
            new RhinoDocumentObserverSnapshotProvider(_stateStore, _revisionTracker),
            RhinoProjectIconLoader.Load());
        _panelIcon = PanelIcon.Create();
        Panels.RegisterPanel(this, typeof(LayoutFoundryPanel), "Layout Foundry", _panelIcon);
        _eventBridge = new RhinoDocumentEventBridge(
            _revisionTracker,
            LayoutFoundryUiHost.NotifyOverviewChanged);
        _eventBridge.Start();
        RhinoDoc.ActiveDocumentChanged += OnActiveDocumentChanged;
        RhinoDoc.CloseDocument += OnCloseDocument;
        return LoadReturnCode.Success;
    }

    protected override void OnShutdown()
    {
        RhinoDoc.ActiveDocumentChanged -= OnActiveDocumentChanged;
        RhinoDoc.CloseDocument -= OnCloseDocument;
        _eventBridge?.Dispose();
        _eventBridge = null;
        LayoutFoundryUiHost.Reset();
        _panelIcon = null;
        Instance = null;
    }

    protected override bool ShouldCallWriteDocument(FileWriteOptions options)
    {
        return true;
    }

    protected override void WriteDocument(
        RhinoDoc document,
        BinaryArchiveWriter archive,
        FileWriteOptions options)
    {
        _stateStore.Write(document, archive);
    }

    protected override void ReadDocument(
        RhinoDoc document,
        BinaryArchiveReader archive,
        FileReadOptions options)
    {
        _stateStore.Read(document, archive);
    }

    private static void OnActiveDocumentChanged(object? sender, DocumentEventArgs eventArgs)
    {
        LayoutFoundryUiHost.NotifyOverviewChanged(new OverviewInvalidation(
            eventArgs.Document?.RuntimeSerialNumber,
            OverviewInvalidationKind.DocumentIdentity | OverviewInvalidationKind.All));
    }

    private void OnCloseDocument(object? sender, DocumentEventArgs eventArgs)
    {
        _stateStore.Remove(eventArgs.Document);
        _revisionTracker.Remove(eventArgs.Document);
        LayoutFoundryUiHost.NotifyOverviewChanged(new OverviewInvalidation(
            eventArgs.Document?.RuntimeSerialNumber,
            OverviewInvalidationKind.DocumentIdentity | OverviewInvalidationKind.All));
    }
}

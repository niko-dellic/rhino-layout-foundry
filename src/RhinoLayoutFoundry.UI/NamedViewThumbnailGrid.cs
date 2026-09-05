namespace RhinoLayoutFoundry.UI;

/// <summary>Supplies the product's empty message and drag identity to the shared gallery.</summary>
internal sealed class NamedViewThumbnailGrid : FoundryThumbnailGallery
{
    internal NamedViewThumbnailGrid()
    {
        EmptyText = "No named views";
        DragDataFormat = ObserverCanvasDrawable.NamedViewDragType;
    }
}

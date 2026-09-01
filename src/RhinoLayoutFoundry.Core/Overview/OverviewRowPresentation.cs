namespace RhinoLayoutFoundry.Core.Overview;

public sealed record OverviewRowPresentation(
    string PrimaryText,
    string SecondaryText,
    string StatusText,
    bool ShowThumbnail)
{
    public static OverviewRowPresentation Create(
        OverviewTreeNode node,
        bool useMacSafeSingleColumn)
    {
        ArgumentNullException.ThrowIfNull(node);
        var label = $"{Glyph(node)}  {node.Label}";

        if (!useMacSafeSingleColumn)
        {
            return new OverviewRowPresentation(
                label,
                node.SecondaryText,
                node.StatusText,
                node.Key.Kind == OverviewNodeKind.Sheet);
        }

        var metadata = new[] { node.SecondaryText, node.StatusText }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        var primaryText = metadata.Length == 0
            ? label
            : $"{label}  ·  {string.Join(" · ", metadata)}";

        return new OverviewRowPresentation(
            primaryText,
            string.Empty,
            string.Empty,
            ShowThumbnail: false);
    }

    private static string Glyph(OverviewTreeNode node)
    {
        if (node.IsDocumentRoot)
        {
            return "3DM";
        }

        return node.Key.Kind switch
        {
            OverviewNodeKind.Folder => "📁",
            OverviewNodeKind.Sheet => "▣",
            OverviewNodeKind.Detail => "⌗",
            OverviewNodeKind.AppearanceState => "◫",
            _ => throw new ArgumentOutOfRangeException(
                nameof(node),
                node.Key.Kind,
                null),
        };
    }
}

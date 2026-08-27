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
        var label = $"{Glyph(node.Key.Kind)}  {node.Label}";

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

    private static string Glyph(OverviewNodeKind kind)
    {
        return kind switch
        {
            OverviewNodeKind.Folder => "📁",
            OverviewNodeKind.Sheet => "▣",
            OverviewNodeKind.Detail => "⌗",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }
}

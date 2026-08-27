namespace RhinoLayoutFoundry.Core.Overview;

public enum FoundryPanelDensity
{
    Compact,
    Standard,
    Wide,
}

public sealed record FoundryResponsiveLayout(
    FoundryPanelDensity Density,
    bool ShowSecondaryColumn,
    bool ShowSelectionHint,
    bool StackToolbar,
    int ThumbnailWidth,
    int ThumbnailHeight)
{
    private const int CompactEnterWidth = 340;
    private const int CompactExitWidth = 380;
    private const int WideEnterWidth = 580;
    private const int WideExitWidth = 540;

    public static FoundryResponsiveLayout ForWidth(int width)
    {
        if (width < 360)
        {
            return new FoundryResponsiveLayout(
                FoundryPanelDensity.Compact,
                ShowSecondaryColumn: false,
                ShowSelectionHint: false,
                StackToolbar: true,
                ThumbnailWidth: 48,
                ThumbnailHeight: 32);
        }

        if (width < 560)
        {
            return new FoundryResponsiveLayout(
                FoundryPanelDensity.Standard,
                ShowSecondaryColumn: true,
                ShowSelectionHint: true,
                StackToolbar: false,
                ThumbnailWidth: 56,
                ThumbnailHeight: 38);
        }

        return new FoundryResponsiveLayout(
            FoundryPanelDensity.Wide,
            ShowSecondaryColumn: true,
            ShowSelectionHint: true,
            StackToolbar: false,
            ThumbnailWidth: 72,
            ThumbnailHeight: 48);
    }

    public static FoundryResponsiveLayout Transition(
        int width,
        FoundryPanelDensity currentDensity)
    {
        var density = currentDensity switch
        {
            FoundryPanelDensity.Compact when width < CompactExitWidth => FoundryPanelDensity.Compact,
            FoundryPanelDensity.Standard when width < CompactEnterWidth => FoundryPanelDensity.Compact,
            FoundryPanelDensity.Standard when width >= WideEnterWidth => FoundryPanelDensity.Wide,
            FoundryPanelDensity.Standard => FoundryPanelDensity.Standard,
            FoundryPanelDensity.Wide when width >= WideExitWidth => FoundryPanelDensity.Wide,
            FoundryPanelDensity.Wide when width < CompactEnterWidth => FoundryPanelDensity.Compact,
            FoundryPanelDensity.Wide => FoundryPanelDensity.Standard,
            _ => ForWidth(width).Density,
        };

        return ForDensity(density);
    }

    private static FoundryResponsiveLayout ForDensity(FoundryPanelDensity density)
    {
        return density switch
        {
            FoundryPanelDensity.Compact => ForWidth(CompactEnterWidth - 1),
            FoundryPanelDensity.Standard => ForWidth(420),
            FoundryPanelDensity.Wide => ForWidth(WideEnterWidth),
            _ => throw new ArgumentOutOfRangeException(nameof(density)),
        };
    }
}

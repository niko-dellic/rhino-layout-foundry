using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class FoundryResponsiveLayoutTests
{
    [Fact]
    public void CompactPanelsStackToolsAndHideSecondaryCopy()
    {
        var layout = FoundryResponsiveLayout.ForWidth(320);

        Assert.Equal(FoundryPanelDensity.Compact, layout.Density);
        Assert.True(layout.StackToolbar);
        Assert.False(layout.ShowSecondaryColumn);
        Assert.False(layout.ShowSelectionHint);
    }

    [Fact]
    public void WidePanelsUseLargerPreviews()
    {
        var standard = FoundryResponsiveLayout.ForWidth(420);
        var wide = FoundryResponsiveLayout.ForWidth(700);

        Assert.Equal(FoundryPanelDensity.Wide, wide.Density);
        Assert.True(wide.ThumbnailWidth > standard.ThumbnailWidth);
    }

    [Fact]
    public void DensityTransitionsUseHysteresisAroundCompactBreakpoint()
    {
        var staysStandard = FoundryResponsiveLayout.Transition(350, FoundryPanelDensity.Standard);
        var entersCompact = FoundryResponsiveLayout.Transition(330, FoundryPanelDensity.Standard);
        var staysCompact = FoundryResponsiveLayout.Transition(370, FoundryPanelDensity.Compact);
        var exitsCompact = FoundryResponsiveLayout.Transition(390, FoundryPanelDensity.Compact);

        Assert.Equal(FoundryPanelDensity.Standard, staysStandard.Density);
        Assert.Equal(FoundryPanelDensity.Compact, entersCompact.Density);
        Assert.Equal(FoundryPanelDensity.Compact, staysCompact.Density);
        Assert.Equal(FoundryPanelDensity.Standard, exitsCompact.Density);
    }

    [Fact]
    public void DensityTransitionsUseHysteresisAroundWideBreakpoint()
    {
        Assert.Equal(
            FoundryPanelDensity.Standard,
            FoundryResponsiveLayout.Transition(560, FoundryPanelDensity.Standard).Density);
        Assert.Equal(
            FoundryPanelDensity.Wide,
            FoundryResponsiveLayout.Transition(590, FoundryPanelDensity.Standard).Density);
        Assert.Equal(
            FoundryPanelDensity.Wide,
            FoundryResponsiveLayout.Transition(550, FoundryPanelDensity.Wide).Density);
        Assert.Equal(
            FoundryPanelDensity.Standard,
            FoundryResponsiveLayout.Transition(530, FoundryPanelDensity.Wide).Density);
    }
}

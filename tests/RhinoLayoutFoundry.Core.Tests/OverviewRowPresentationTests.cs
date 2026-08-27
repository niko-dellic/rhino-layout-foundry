using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class OverviewRowPresentationTests
{
    [Fact]
    public void FullTableKeepsMetadataAndThumbnailInSeparateCells()
    {
        var node = CreateSheetNode();

        var presentation = OverviewRowPresentation.Create(
            node,
            useMacSafeSingleColumn: false);

        Assert.Equal("▣  A101", presentation.PrimaryText);
        Assert.Equal("2 details · Issued", presentation.SecondaryText);
        Assert.Equal("Warning · 1", presentation.StatusText);
        Assert.True(presentation.ShowThumbnail);
    }

    [Fact]
    public void MacSafeTableFoldsMetadataIntoOneTextOnlyCell()
    {
        var node = CreateSheetNode();

        var presentation = OverviewRowPresentation.Create(
            node,
            useMacSafeSingleColumn: true);

        Assert.Equal(
            "▣  A101  ·  2 details · Issued · Warning · 1",
            presentation.PrimaryText);
        Assert.Equal(string.Empty, presentation.SecondaryText);
        Assert.Equal(string.Empty, presentation.StatusText);
        Assert.False(presentation.ShowThumbnail);
    }

    [Fact]
    public void HierarchyKindsReceiveDistinctMonochromeGlyphs()
    {
        var folder = new OverviewTreeNode(
            new OverviewNodeKey(OverviewNodeKind.Folder, Guid.NewGuid()),
            "Plans",
            "2 sheets",
            []);
        var detail = new OverviewTreeNode(
            new OverviewNodeKey(OverviewNodeKind.Detail, Guid.NewGuid()),
            "Plan viewport",
            "Detail viewport",
            []);

        var sheet = CreateSheetNode();

        Assert.Equal(
            "📁  Plans  ·  2 sheets",
            OverviewRowPresentation.Create(folder, useMacSafeSingleColumn: true).PrimaryText);
        Assert.True(
            OverviewRowPresentation.Create(sheet, useMacSafeSingleColumn: true).PrimaryText
                .StartsWith("▣  A101", StringComparison.Ordinal));
        Assert.Equal(
            "⌗  Plan viewport  ·  Detail viewport",
            OverviewRowPresentation.Create(detail, useMacSafeSingleColumn: true).PrimaryText);
    }

    private static OverviewTreeNode CreateSheetNode()
    {
        return new OverviewTreeNode(
            new OverviewNodeKey(OverviewNodeKind.Sheet, Guid.NewGuid()),
            "A101",
            "2 details · Issued",
            [],
            Diagnostics:
            [
                new OverviewIssue(
                    "duplicate-sheet-name",
                    OverviewIssueSeverity.Warning,
                    "Duplicate sheet name."),
            ]);
    }
}

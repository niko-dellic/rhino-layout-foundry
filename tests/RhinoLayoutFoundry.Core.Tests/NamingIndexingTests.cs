using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Naming;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class NamingIndexingTests
{
    private static readonly Guid Root = Guid.Parse("71000000-0000-0000-0000-000000000001");
    private static readonly Guid Plans = Guid.Parse("71000000-0000-0000-0000-000000000002");

    [Fact]
    public void FolderSameStemCountsOnlyNamesMatchingResolvedPattern()
    {
        var mechOne = Candidate("MECH_1", "MECH", 0);
        var cover = Candidate("Cover", "MECH", 1);
        var archOne = Candidate("ARCH_1", "ARCH", 2);
        var target = Candidate("Pending", "MECH", 3, target: true);

        var indices = NamingIndexing.Resolve(
            "{discipline}_{index}", 1, 1,
            NamingIndexMode.FolderSameStemPosition,
            Root, Folders(), [mechOne, cover, archOne, target]);

        Assert.Equal(2, indices[target.Item.SheetId]);
    }

    [Fact]
    public void FolderPositionUsesFinalSheetPositionWithStartAndStep()
    {
        var target = Candidate("Pending", "MECH", 2, target: true);
        var indices = NamingIndexing.Resolve(
            "S-{index}", 10, 5,
            NamingIndexMode.FolderPosition,
            Root, Folders(),
            [Candidate("A", "", 0), Candidate("B", "", 1), target]);

        Assert.Equal(20, indices[target.Item.SheetId]);
    }

    [Fact]
    public void PreserveUsesStoredIndexAndFolderPositionFallback()
    {
        var preserved = Candidate("P-40", "", 0, target: true, preserved: 40);
        var fallback = Candidate("Pending", "", 1, target: true);
        var indices = NamingIndexing.Resolve(
            "P-{index}", 1, 1,
            NamingIndexMode.PreserveCurrent,
            Root, Folders(), [preserved, fallback]);

        Assert.Equal(40, indices[preserved.Item.SheetId]);
        Assert.Equal(2, indices[fallback.Item.SheetId]);
    }

    [Fact]
    public void AvailablePreviewAdvancesPastDocumentWideNameCollisions()
    {
        var first = new NamingItem(Guid.NewGuid(), "Pending", new Dictionary<string, string>());
        var second = new NamingItem(Guid.NewGuid(), "Pending", new Dictionary<string, string>());

        var result = NamingIndexing.PreviewAvailable(
            "Page {index}",
            [first, second],
            new Dictionary<Guid, int> { [first.SheetId] = 1, [second.SheetId] = 2 },
            ["Page 1", "Page 2", "Page 3"]);

        Assert.True(result.Preview.CanApply);
        Assert.Equal(["Page 4", "Page 5"], result.Preview.Entries.Select(entry => entry.ProposedName));
        Assert.Equal(4, result.Indices[first.SheetId]);
        Assert.Equal(5, result.Indices[second.SheetId]);
    }

    private static NamingIndexCandidate Candidate(
        string name,
        string discipline,
        int order,
        bool target = false,
        int? preserved = null) => new(
        new NamingItem(Guid.NewGuid(), name,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["discipline"] = discipline,
            }),
        Plans,
        order,
        target,
        preserved);

    private static IReadOnlyDictionary<Guid, FolderRecord> Folders() =>
        new Dictionary<Guid, FolderRecord>
        {
            [Root] = new(Root, null, "Root", 0),
            [Plans] = new(Plans, Root, "Plans", 0),
        };
}

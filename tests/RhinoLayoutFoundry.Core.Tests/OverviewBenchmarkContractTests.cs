using System.Diagnostics;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class OverviewBenchmarkContractTests
{
    [Fact]
    public void TargetScaleBuildsCompleteHierarchyWithinColdBudget()
    {
        var overview = CreateBenchmarkOverview();
        var stopwatch = Stopwatch.StartNew();

        var tree = OverviewTreeBuilder.Build(overview);

        stopwatch.Stop();
        Assert.Equal(1_211, Flatten(tree).Count());
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(1),
            $"Hierarchy build took {stopwatch.Elapsed.TotalMilliseconds:F1} ms.");
    }

    [Fact]
    public void TargetScaleFilterReturnsOnlyMatchingBranch()
    {
        var overview = CreateBenchmarkOverview();
        var stopwatch = Stopwatch.StartNew();

        var tree = OverviewTreeBuilder.Build(overview, "Sheet 0199");

        stopwatch.Stop();
        Assert.Equal(8, Flatten(tree).Count());
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromMilliseconds(50),
            $"Hierarchy filter took {stopwatch.Elapsed.TotalMilliseconds:F1} ms.");
    }

    private static DocumentOverview CreateBenchmarkOverview()
    {
        var rootId = Guid.Parse("80000000-0000-0000-0000-000000000001");
        var folders = new List<FolderOverview>
        {
            new(rootId, null, "Unorganized", 0),
        };
        for (var index = 0; index < 10; index++)
        {
            folders.Add(new FolderOverview(
                DeterministicId(1, index),
                rootId,
                $"Folder {index:D2}",
                index));
        }

        var sheets = new List<SheetOverview>();
        for (var sheetIndex = 0; sheetIndex < 200; sheetIndex++)
        {
            var details = Enumerable.Range(0, 5)
                .Select(detailIndex => new DetailOverview(
                    DeterministicId(3, (sheetIndex * 5) + detailIndex),
                    $"Detail {sheetIndex:D4}-{detailIndex + 1}",
                    detailIndex))
                .ToArray();
            sheets.Add(new SheetOverview(
                DeterministicId(2, sheetIndex),
                folders[1 + (sheetIndex % 10)].Id,
                $"Sheet {sheetIndex:D4}",
                sheetIndex,
                [$"Tag-{sheetIndex % 5}"],
                details));
        }

        return new DocumentOverview(42, "Benchmark", rootId, folders, sheets);
    }

    private static Guid DeterministicId(int category, int index)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, category);
        BitConverter.TryWriteBytes(bytes[4..], index + 1);
        return new Guid(bytes);
    }

    private static IEnumerable<OverviewTreeNode> Flatten(
        IEnumerable<OverviewTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }
}

using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class CompensationJournalTests
{
    [Fact]
    public void FailureAtEveryAcquisitionStageRestoresEverythingAlreadyOwned()
    {
        // Preview page construction and import both register before their next fallible step.
        for (var failAfter = 1; failAfter <= 6; failAfter++)
        {
            var resources = new HashSet<int>();
            var journal = new CompensationJournal();
            for (var step = 0; step < failAfter; step++)
            {
                var acquired = step;
                resources.Add(acquired);
                journal.Register($"resource {acquired}", () => resources.Remove(acquired));
            }
            Assert.Empty(journal.Rollback());
            Assert.Empty(resources);
            Assert.Empty(journal.Rollback());
        }
    }

    [Fact]
    public void CleanupFailureDoesNotPreventRemainingRestoration()
    {
        var order = new List<int>();
        var journal = new CompensationJournal();
        journal.Register("Undo recording", () => order.Add(1));
        journal.Register("broken resource", () => throw new InvalidOperationException("failed"));
        journal.Register("page", () => order.Add(3));
        Assert.Single(journal.Rollback());
        Assert.Equal(new[] { 3, 1 }, order);
    }

    [Fact]
    public void CommitKeepsAcquiredResources()
    {
        var journal = new CompensationJournal();
        journal.Register("committed", () => throw new InvalidOperationException());
        journal.Commit();
        Assert.Empty(journal.Rollback());
    }
}

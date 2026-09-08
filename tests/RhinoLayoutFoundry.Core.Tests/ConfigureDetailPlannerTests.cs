using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class ConfigureDetailPlannerTests
{
    [Theory]
    [InlineData(100, true)]
    [InlineData(200, true)]
    [InlineData(0, false)]
    [InlineData(double.NaN, false)]
    public void ValidatesScaleBeforeStaging(double scale, bool expected)
    {
        var (snapshot, detail) = Fixture();
        var plan = new ConfigureDetailPlanner().Plan(new(7, 3, detail, scale, new(4000,5000,3000), "First floor"), snapshot);
        Assert.Equal(expected, plan.CanApply);
        if (expected) Assert.IsType<ConfigureDetailChange>(Assert.Single(plan.Changes));
        else Assert.Empty(plan.Changes);
    }

    [Fact]
    public void RejectsStaleDocumentAndMissingDetail()
    {
        var (snapshot, _) = Fixture();
        var plan = new ConfigureDetailPlanner().Plan(new(8, 2, Guid.NewGuid(), 100, new(0,0,0), ""), snapshot);
        Assert.False(plan.CanApply);
        Assert.Empty(plan.Changes);
        Assert.Contains(plan.Diagnostics, d => d.Code == "automation.document_mismatch");
        Assert.Contains(plan.Diagnostics, d => d.Code == "automation.stale_revision");
        Assert.Contains(plan.Diagnostics, d => d.Code == "detail.missing");
    }

    private static (DocumentSnapshot Snapshot, Guid Detail) Fixture()
    {
        var detail = Guid.NewGuid();
        var page = Guid.NewGuid();
        var folder = Guid.NewGuid();
        return (new DocumentSnapshot(7,3,folder,new Dictionary<Guid,FolderRecord>(),
            new Dictionary<Guid,SheetSnapshot> { [page] = new(page,folder,0,"Plans",[detail],new Dictionary<string,string>(),
                DetailSettings: [new(detail,"Plan",Guid.NewGuid(),"Technical")]) },new HashSet<Guid>(),new HashSet<Guid>()),detail);
    }
}

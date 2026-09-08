using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class FlipClippingPlanePlannerTests
{
    [Theory]
    [InlineData("demo", 3, true)]
    [InlineData("", 3, false)]
    [InlineData("demo", 2, false)]
    public void RequiresOwnedPlaneAndCurrentRevision(string owner, long revision, bool allowed)
    {
        var id = Guid.NewGuid();
        var snapshot = new DocumentSnapshot(7,3,Guid.NewGuid(),new Dictionary<Guid,FolderRecord>(),
            new Dictionary<Guid,SheetSnapshot>(),new HashSet<Guid>(),new HashSet<Guid>())
        { ClippingPlanes = [new(id,"Plan cut",new(0,0,1200),new(0,0,1),100,100,[Guid.NewGuid()],owner)] };
        var plan = new FlipClippingPlanePlanner().Plan(new(7,revision,id),snapshot);
        Assert.Equal(allowed,plan.CanApply);
        if (allowed) Assert.Equal(id,Assert.IsType<FlipClippingPlaneChange>(Assert.Single(plan.Changes)).ObjectId);
        else Assert.Empty(plan.Changes);
    }
}

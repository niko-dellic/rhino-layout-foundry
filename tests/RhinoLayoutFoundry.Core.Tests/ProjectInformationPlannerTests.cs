using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class ProjectInformationPlannerTests
{
    [Fact]
    public void ValidProjectInformationCreatesUpdate()
    {
        var snapshot = TestSnapshots.Create() with { ProjectData = ProjectInformation.Empty };
        var information = ProjectInformation.Empty with
        {
            ProjectName = "Civic Library",
            CustomFields = new Dictionary<string, string> { ["Contract"] = "Design-build" },
        };

        var plan = new UpdateProjectInformationPlanner().Plan(
            new UpdateProjectInformationRequest(42, 1, information), snapshot);

        Assert.True(plan.CanApply);
        var change = Assert.IsType<UpdateProjectInformationChange>(Assert.Single(plan.Changes));
        Assert.Equal("Civic Library", change.NewInformation.ProjectName);
    }

    [Fact]
    public void OversizedOrUnsupportedLogoIsRejected()
    {
        var snapshot = TestSnapshots.Create();
        var information = ProjectInformation.Empty with
        {
            Logo = new BrandAsset("logo.svg", "image/svg+xml", "abc", new byte[5 * 1024 * 1024 + 1]),
        };

        var plan = new UpdateProjectInformationPlanner().Plan(
            new UpdateProjectInformationRequest(42, 1, information), snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "project.logo_size");
        Assert.Contains(plan.Diagnostics, item => item.Code == "project.logo_type");
    }
}

using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Persistence;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class LayoutPackageProjectInformationPolicyTests
{
    [Fact]
    public void OptOutPreservesDestinationForEveryImportMode()
    {
        var destination = ProjectInformation.Empty with { ProjectName = "Destination" };
        var source = ProjectInformation.Empty with { ProjectName = "Source" };

        Assert.Equal(destination, LayoutPackageProjectInformationPolicy.Resolve(
            destination, source, LayoutPackageImportMode.Merge, importProjectInformation: false));
        Assert.Equal(destination, LayoutPackageProjectInformationPolicy.Resolve(
            destination, source, LayoutPackageImportMode.Replace, importProjectInformation: false));
    }

    [Fact]
    public void MergeFillsBlankValuesWithoutOverwritingDestinationValues()
    {
        var destination = ProjectInformation.Empty with
        {
            ProjectName = "Destination",
            CustomFields = new Dictionary<string, string> { ["Existing"] = "Keep" },
        };
        var source = ProjectInformation.Empty with
        {
            ProjectName = "Source",
            ProjectNumber = "P-204",
            CustomFields = new Dictionary<string, string>
            {
                ["Existing"] = "Replace",
                ["Imported"] = "Add",
            },
        };

        var result = LayoutPackageProjectInformationPolicy.Resolve(
            destination, source, LayoutPackageImportMode.Merge, importProjectInformation: true);

        Assert.Equal("Destination", result.ProjectName);
        Assert.Equal("P-204", result.ProjectNumber);
        Assert.Equal("Keep", result.CustomFields["Existing"]);
        Assert.Equal("Add", result.CustomFields["Imported"]);
    }

    [Fact]
    public void ReplaceUsesPackageInformationWhenOptedIn()
    {
        var destination = ProjectInformation.Empty with { ProjectName = "Destination" };
        var source = ProjectInformation.Empty with { ProjectName = "Source" };

        var result = LayoutPackageProjectInformationPolicy.Resolve(
            destination, source, LayoutPackageImportMode.Replace, importProjectInformation: true);

        Assert.Equal(source, result);
    }
}

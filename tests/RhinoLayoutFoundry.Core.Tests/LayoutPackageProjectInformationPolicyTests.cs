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
            TitleBlockOptions = new TitleBlockContentOptions(
                [TitleBlockContentField.ProjectName],
                [new CustomTitleBlockFieldOption("Existing", false)],
                true),
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
            TitleBlockOptions = new TitleBlockContentOptions(
                [TitleBlockContentField.FirmName],
                [
                    new CustomTitleBlockFieldOption("Existing"),
                    new CustomTitleBlockFieldOption("Imported"),
                ]),
        };

        var result = LayoutPackageProjectInformationPolicy.Resolve(
            destination, source, LayoutPackageImportMode.Merge, importProjectInformation: true);

        Assert.Equal("Destination", result.ProjectName);
        Assert.Equal("P-204", result.ProjectNumber);
        Assert.Equal("Keep", result.CustomFields["Existing"]);
        Assert.Equal("Add", result.CustomFields["Imported"]);
        Assert.True(result.ContentOptions.ReserveRevisionArea);
        Assert.True(result.ContentOptions.Includes(TitleBlockContentField.ProjectName));
        Assert.False(result.ContentOptions.CustomFields[0].IsIncluded);
        Assert.Equal("Imported", result.ContentOptions.CustomFields[1].Label);
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

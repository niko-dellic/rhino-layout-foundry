using System.Text.Json;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Extensibility;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class AutomationLayoutRequestTests
{
    [Fact]
    public void BridgeStagesCanonicalPerDetailRequest()
    {
        var snapshot = TestSnapshots.Create() with
        {
            NamedViews = new HashSet<string>
            {
                "Plan",
                "Elevation"
            }
        };
        using var json = JsonDocument.Parse($$"""
            {"destination_folder_id":"{{snapshot.RootFolderId}}", "naming_pattern":"API-{index}",
             "layouts":[{"quantity":2,"page_width":594,"page_height":420,"page_units":"Millimeters",
             "layout_kind":"two_details_horizontal","named_views_by_detail":["Plan","Elevation"],"title_block":"right"}]}
            """);
        var request = AutomationLayoutRequest.Parse(json.RootElement, snapshot);
        var plan = new BatchCreateSheetsPlanner().Plan(request, snapshot);
        Assert.True(plan.CanApply);
        Assert.Equal(2, plan.Changes.Count);
        Assert.All(plan.Changes.Cast<CreateSheetFromTemplateChange>(), change =>
        {
            Assert.Equal(BuiltInTitleBlockKind.RightSidebar, change.Template.TitleBlock!.BuiltInKind);
            Assert.Equal(["Plan", "Elevation"], change.Template.DetailSlots.Select(slot => change.NamedViewAssignments[slot.Id]));
        });
    }

    [Theory]
    [InlineData("\"templates\":[]")]
    [InlineData("\"layouts\":[]")]
    [InlineData("\"layouts\":[{\"named_view\":\"Plan\"}]")]
    [InlineData("\"layouts\":[{\"layout_kind\":\"unknown\"}]")]
    public void ObsoleteOrInvalidInputsAreRejected(string fields)
    {
        using var json = JsonDocument.Parse("{" + fields + "}");
        Assert.Throws<JsonException>(() => AutomationLayoutRequest.Parse(json.RootElement, TestSnapshots.Create()));
    }
}

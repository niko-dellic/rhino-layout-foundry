using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class ViewAuthoringPlannerTests
{
    [Fact]
    public void Named_view_plan_rejects_duplicate_and_degenerate_camera()
    {
        var snapshot = Snapshot(namedViews: new HashSet<string>(["Section A"], StringComparer.OrdinalIgnoreCase));
        var request = new CreateNamedViewRequest(
            7,
            3,
            new NamedViewDefinition(
                "Section A",
                new Point3Coordinates(1, 2, 3),
                new Point3Coordinates(1, 2, 3),
                new Vector3Coordinates(0, 0, 1),
                FoundryViewProjection.Parallel,
                SessionId: "session"));

        var plan = new CreateNamedViewPlanner().Plan(request, snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "named_view.duplicate_name");
        Assert.Contains(plan.Diagnostics, item => item.Code == "named_view.camera_degenerate");
    }

    [Fact]
    public void Named_view_plan_normalizes_name_and_freezes_change()
    {
        var snapshot = Snapshot();
        var request = new CreateNamedViewRequest(
            7,
            3,
            new NamedViewDefinition(
                "  North elevation  ",
                new Point3Coordinates(0, -10, 2),
                new Point3Coordinates(0, 0, 2),
                new Vector3Coordinates(0, 0, 1),
                FoundryViewProjection.Parallel,
                SessionId: "alpha-1"));

        var plan = new CreateNamedViewPlanner().Plan(request, snapshot);

        Assert.True(plan.CanApply);
        var change = Assert.IsType<CreateNamedViewChange>(Assert.Single(plan.Changes));
        Assert.Equal("North elevation", change.Definition.Name);
        Assert.Equal("alpha-1", change.Definition.SessionId);
    }

    [Fact]
    public void Clipping_plane_plan_requires_size_session_and_viewport()
    {
        var snapshot = Snapshot();
        var request = new CreateClippingPlaneRequest(
            7,
            3,
            new ClippingPlaneDefinition(
                "Section A cut",
                new Point3Coordinates(0, 0, 0),
                new Vector3Coordinates(1, 0, 0),
                new Vector3Coordinates(0, 1, 0),
                0,
                10,
                [],
                ""));

        var plan = new CreateClippingPlanePlanner().Plan(request, snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "clipping_plane.size_invalid");
        Assert.Contains(plan.Diagnostics, item => item.Code == "clipping_plane.viewport_required");
        Assert.Contains(plan.Diagnostics, item => item.Code == "automation.session_required");
    }

    [Fact]
    public void Clipping_plane_plan_deduplicates_viewports()
    {
        var snapshot = Snapshot();
        var viewportId = Guid.NewGuid();
        var request = new CreateClippingPlaneRequest(
            7,
            3,
            new ClippingPlaneDefinition(
                "Section A cut",
                new Point3Coordinates(0, 0, 0),
                new Vector3Coordinates(1, 0, 0),
                new Vector3Coordinates(0, 1, 0),
                10,
                12,
                [viewportId, viewportId],
                "alpha-1"));

        var plan = new CreateClippingPlanePlanner().Plan(request, snapshot);

        Assert.True(plan.CanApply);
        var change = Assert.IsType<CreateClippingPlaneChange>(Assert.Single(plan.Changes));
        Assert.Equal(new[] { viewportId }, change.Definition.ViewportIds);
    }

    private static DocumentSnapshot Snapshot(IReadOnlySet<string>? namedViews = null) =>
        new(
            7,
            3,
            Guid.NewGuid(),
            new Dictionary<Guid, FolderRecord>(),
            new Dictionary<Guid, SheetSnapshot>(),
            new HashSet<Guid>(),
            new HashSet<Guid>())
        {
            NamedViews = namedViews ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };
}

using System.Text.Json;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;
using RhinoLayoutFoundry.Extensibility;

namespace RhinoLayoutFoundry.UI;

/// <summary>
/// Type-erased boundary for separately loaded companions. Only BCL values cross
/// Rhino plug-in load contexts; Foundry domain objects remain in this assembly.
/// </summary>
internal static class FoundryAutomationBridge
{
    internal const string DispatchKey = "automationDispatch";
    internal const string DocumentContextKey = "documentContextJson";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static IReadOnlyDictionary<string, object?> CreateInvocationContext()
    {
        var serial = FoundryAutomation.Current?.CaptureSnapshot().DocumentRuntimeSerialNumber;
        async Task<string> BoundDispatch(string request, CancellationToken cancellation)
        {
            if (serial is null || FoundryAutomation.Current?.CaptureSnapshot().DocumentRuntimeSerialNumber != serial)
                throw new InvalidOperationException("This drawing task belongs to another document. Return to that document to continue.");
            return await DispatchAsync(request, cancellation);
        }
        var dispatch = new Func<string, CancellationToken, Task<string>>(BoundDispatch);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [DispatchKey] = dispatch,
            [DocumentContextKey] = InspectJson(),
        };
    }

    private static async Task<string> DispatchAsync(string requestJson, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(requestJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("protocol_major", out var protocol) ||
                protocol.ValueKind != JsonValueKind.Number || !protocol.TryGetInt32(out var major) || major != FoundryAutomationProtocol.MajorVersion)
                throw new InvalidOperationException("Layout Foundry and Foundry AI use incompatible automation protocols. Update both components together and restart Rhino.");
            var operation = String(root, "operation");
            var arguments = root.TryGetProperty("arguments", out var value)
                ? value
                : default;
            var host = FoundryAutomation.Current ??
                throw new InvalidOperationException("Layout Foundry automation is not available.");

            return operation switch
            {
                "inspect_document" => InspectJson(host),
                "stage_drawing_set" => Stage(host, DrawingSetPlanner.Plan(
                    DrawingSetSpecificationValidator.Parse(arguments), host.CaptureSnapshot())),
                "validate_drawing_set" => Json(DrawingSetSpecificationValidator.Validate(
                    DrawingSetSpecificationValidator.Parse(arguments), host.CaptureSnapshot())),
                "capture_layout" => await CaptureAsync(host, arguments, true, cancellationToken),
                "capture_named_view" => await CaptureAsync(host, arguments, false, cancellationToken),
                "capture_model" => await CaptureModelAsync(host, cancellationToken),
                "stage_create_named_view" => Stage(host, NamedViewPlan(host, arguments, String(root, "session_id"))),
                "stage_create_clipping_plane" => Stage(host, ClippingPlanePlan(host, arguments, String(root, "session_id"))),
                "stage_assign_named_view" => Stage(host, AssignmentPlan(host, arguments)),
                "stage_configure_detail" => Stage(host, DetailPlan(host, arguments)),
                "stage_set_detail_captions" => Stage(host, CaptionPlan(host, arguments)),
                "stage_flip_clipping_plane" => Stage(host, FlipClipPlan(host, arguments)),
                "apply_plan" => await ApplyAsync(host, GuidValue(arguments, "plan_id"), cancellationToken),
                "abandon_plan" => Abandon(host, GuidValue(arguments, "plan_id")),
                _ => Json(new { error = "Unsupported Foundry bridge operation." }),
            };
        }
        catch (Exception exception)
        {
            return Json(new { error = exception.Message });
        }
    }

    private static string InspectJson() => FoundryAutomation.Current is { } host
        ? InspectJson(host)
        : Json(new { error = "Layout Foundry automation is not available." });

    private static string InspectJson(IFoundryAutomationHost host)
    {
        var snapshot = host.CaptureSnapshot();
        IReadOnlyCollection<OverviewNodeKey> selection = LayoutFoundryUiHost.Selection.DocumentRuntimeSerialNumber ==
                        snapshot.DocumentRuntimeSerialNumber
            ? LayoutFoundryUiHost.Selection.Selected
            : Array.Empty<OverviewNodeKey>();
        return Json(new
        {
            protocol = host.GetCapabilities(),
            document = new
            {
                runtime_serial = snapshot.DocumentRuntimeSerialNumber,
                revision = snapshot.Revision,
                root_folder_id = snapshot.RootFolderId,
                model_bounds = snapshot.ModelBounds,
                model_units = snapshot.ModelUnitSystem,
                automation_features = new[] { "drawing_set_v2", "drawing_captions", "linked_detail_captions", "per_view_hidden_layers", "single_approval_batch", "proposal_receipts" },
                drawing_set_receipts = snapshot.Metadata.Where(p => p.Key.StartsWith("RhinoLayoutFoundry.DrawingSet.", StringComparison.Ordinal))
                    .ToDictionary(p => p.Key, p => p.Value),
                display_modes = snapshot.DisplayModes,
                standard_viewport_ids = snapshot.StandardViewports,
                folders = snapshot.Folders.Values.Select(folder => new
                {
                    id = folder.Id,
                    parent_id = folder.ParentId,
                    folder.Name,
                    folder.Order,
                    caption_settings = DetailCaptions.Read(snapshot.Metadata, new(HierarchyScopeKind.Folder, folder.Id)),
                }),
                layouts = snapshot.Sheets.Values.Select(sheet => new
                {
                    id = sheet.PageViewId,
                    folder_id = sheet.FolderId,
                    sheet.Name,
                    caption_settings = DetailCaptions.Read(snapshot.Metadata, new(HierarchyScopeKind.Sheet, sheet.PageViewId)),
                    notes = sheet.Notes,
                    ai_provenance = sheet.Metadata.Where(p => p.Key.StartsWith("RhinoLayoutFoundry.DrawingSet.", StringComparison.Ordinal))
                        .ToDictionary(p => p.Key, p => p.Value),
                    paper = new { width = sheet.PageWidth, height = sheet.PageHeight, units = sheet.PageUnitSystem },
                    details = sheet.Details.Select(detail => new
                    {
                        id = detail.DetailViewportId,
                        detail.Name,
                        managed_caption = detail.HasManagedCaption,
                        caption_warning = detail.CaptionWarning,
                        caption_settings = DetailCaptions.Read(snapshot.Metadata, new(HierarchyScopeKind.Detail, detail.DetailViewportId)),
                        effective_caption = DetailCaptions.Resolve(snapshot.Metadata, snapshot.Folders, sheet.FolderId,
                            sheet.PageViewId, detail.DetailViewportId, detail.IsParallelProjection == true),
                        display_mode = detail.DisplayModeName,
                        page_bounds = detail.PageBounds,
                        scale_denominator = detail.ScaleDenominator,
                        camera_location = detail.CameraLocation,
                        camera_target = detail.CameraTarget,
                        parallel_projection = detail.IsParallelProjection,
                        named_view = sheet.DetailNamedViews.GetValueOrDefault(detail.DetailViewportId),
                    }),
                }),
                templates = snapshot.Templates.Select(template => new
                {
                    id = template.Id,
                    template.Name,
                    paper = new { width = template.Paper.Width, height = template.Paper.Height, units = template.Paper.UnitSystem },
                    detail_slots = template.DetailSlots.Count,
                    has_title_block = template.TitleBlock is not null,
                }),
                named_views = snapshot.NamedViewSnapshots,
                clipping_planes = snapshot.ClippingPlanes,
                layers = snapshot.LayerSnapshots.Values.Select(layer => new
                {
                    id = layer.Id,
                    parent_id = layer.ParentId,
                    name = layer.FullPath,
                    visible = layer.IsGloballyVisible,
                    object_count = snapshot.ModelObjects.Values.Count(item => item.LayerId == layer.Id),
                }),
                selection = selection.Select(item => new
                {
                    kind = item.Kind.ToString().ToLowerInvariant(),
                    id = item.Id,
                }),
            },
        });
    }

    private static async Task<string> CaptureAsync(
        IFoundryAutomationHost host,
        JsonElement arguments,
        bool layout,
        CancellationToken cancellationToken)
    {
        var request = new AutomationCaptureRequest(
            layout ? AutomationCaptureKind.Layout : AutomationCaptureKind.NamedView,
            layout ? GuidValue(arguments, "sheet_page_view_id") : null,
            layout ? null : String(arguments, "named_view_name"),
            Int32(arguments, "width"),
            Int32(arguments, "height"),
            BackgroundArgb: layout ? 0xffffffff : 0xfff5f5f5);
        var result = await host.CaptureAsync(request, cancellationToken);
        return Json(new
        {
            succeeded = result.Succeeded,
            media_type = result.MediaType,
            content_base64 = result.Content is null ? null : Convert.ToBase64String(result.Content),
            result.Message,
        });
    }

    private static async Task<string> CaptureModelAsync(IFoundryAutomationHost host, CancellationToken token)
    {
        var result = await host.CaptureAsync(new AutomationCaptureRequest(AutomationCaptureKind.Model, null, null, 1200, 900), token);
        return Json(new { succeeded = result.Succeeded, media_type = result.MediaType,
            content_base64 = result.Content is null ? null : Convert.ToBase64String(result.Content), result.Message });
    }

    private static OperationPlan CaptionPlan(IFoundryAutomationHost host, JsonElement arguments)
    {
        var snapshot = host.CaptureSnapshot();
        var scope = new HierarchyScope(Enum.Parse<HierarchyScopeKind>(String(arguments, "scope_kind"), true), GuidValue(arguments, "scope_id"));
        var settings = new DetailCaptionSettings(Enum.Parse<CaptionVisibility>(String(arguments, "name"), true),
            Enum.Parse<CaptionVisibility>(String(arguments, "scale"), true));
        return new SetDetailCaptionsPlanner().Plan(new(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision, scope, settings), snapshot);
    }

    private static OperationPlan NamedViewPlan(
        IFoundryAutomationHost host,
        JsonElement arguments,
        string sessionId)
    {
        var snapshot = host.CaptureSnapshot();
        var definition = new NamedViewDefinition(
            String(arguments, "name"),
            Point(arguments.GetProperty("camera_location")),
            Point(arguments.GetProperty("camera_target")),
            Vector(arguments.GetProperty("camera_up")),
            string.Equals(String(arguments, "projection"), "perspective", StringComparison.OrdinalIgnoreCase)
                ? FoundryViewProjection.Perspective
                : FoundryViewProjection.Parallel,
            Double(arguments, "lens_length"),
            sessionId);
        return new CreateNamedViewPlanner().Plan(new CreateNamedViewRequest(
            snapshot.DocumentRuntimeSerialNumber, snapshot.Revision, definition), snapshot);
    }

    private static OperationPlan ClippingPlanePlan(
        IFoundryAutomationHost host,
        JsonElement arguments,
        string sessionId)
    {
        var snapshot = host.CaptureSnapshot();
        var definition = new ClippingPlaneDefinition(
            String(arguments, "name"),
            Point(arguments.GetProperty("origin")),
            Vector(arguments.GetProperty("normal")),
            Vector(arguments.GetProperty("x_axis")),
            Double(arguments, "width"),
            Double(arguments, "height"),
            arguments.GetProperty("viewport_ids").EnumerateArray()
                .Select(item => Guid.Parse(item.GetString() ?? string.Empty)).ToArray(),
            sessionId);
        return new CreateClippingPlanePlanner().Plan(new CreateClippingPlaneRequest(
            snapshot.DocumentRuntimeSerialNumber, snapshot.Revision, definition), snapshot);
    }

    private static OperationPlan FlipClipPlan(IFoundryAutomationHost host, JsonElement arguments)
    {
        var snapshot = host.CaptureSnapshot();
        return new FlipClippingPlanePlanner().Plan(new FlipClippingPlaneRequest(
            snapshot.DocumentRuntimeSerialNumber, snapshot.Revision, GuidValue(arguments, "object_id")), snapshot);
    }

    private static OperationPlan DetailPlan(IFoundryAutomationHost host, JsonElement arguments)
    {
        var snapshot = host.CaptureSnapshot();
        var target = arguments.GetProperty("target");
        return new ConfigureDetailPlanner().Plan(new ConfigureDetailRequest(
            snapshot.DocumentRuntimeSerialNumber, snapshot.Revision, GuidValue(arguments, "detail_viewport_id"),
            arguments.GetProperty("scale_denominator").GetDouble(),
            new Point3Coordinates(target.GetProperty("x").GetDouble(), target.GetProperty("y").GetDouble(), target.GetProperty("z").GetDouble()),
            String(arguments, "name")), snapshot);
    }

    private static OperationPlan AssignmentPlan(IFoundryAutomationHost host, JsonElement arguments)
    {
        var snapshot = host.CaptureSnapshot();
        return new AssignNamedViewPlanner().Plan(new AssignNamedViewRequest(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            arguments.GetProperty("detail_viewport_ids").EnumerateArray()
                .Select(item => Guid.Parse(item.GetString() ?? string.Empty)).ToArray(),
            String(arguments, "named_view_name")), snapshot);
    }

    private static string Stage(IFoundryAutomationHost host, OperationPlan plan)
    {
        if (!plan.CanApply)
            return Json(new
            {
                staged = false,
                diagnostics = plan.Diagnostics.Select(item => new
                {
                    item.Code,
                    severity = item.Severity.ToString(),
                    item.Message,
                }),
            });
        var envelope = host.StagePlan(plan);
        return Json(new
        {
            staged = true,
            plan_id = envelope.PlanId,
            summary = envelope.Summary,
            expires_at = envelope.ExpiresAt,
        });
    }

    private static async Task<string> ApplyAsync(
        IFoundryAutomationHost host,
        Guid planId,
        CancellationToken cancellationToken)
    {
        var approval = host.ApprovePlan(planId);
        var result = await host.ApplyApprovedPlanAsync(approval, cancellationToken);
        return Json(new
        {
            succeeded = result.Succeeded,
            diagnostics = result.Diagnostics.Select(item => new
            {
                item.Code,
                severity = item.Severity.ToString(),
                item.Message,
            }),
        });
    }

    private static string Abandon(IFoundryAutomationHost host, Guid planId)
    {
        host.AbandonPlan(planId);
        return Json(new { abandoned = true });
    }

    private static Point3Coordinates Point(JsonElement value) =>
        new(Double(value, "x"), Double(value, "y"), Double(value, "z"));

    private static Vector3Coordinates Vector(JsonElement value) =>
        new(Double(value, "x"), Double(value, "y"), Double(value, "z"));

    private static string String(JsonElement value, string property) =>
        value.GetProperty(property).GetString()?.Trim() ?? string.Empty;

    private static string? OptionalString(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String
            ? item.GetString()?.Trim()
            : null;

    private static int Int32(JsonElement value, string property) => value.GetProperty(property).GetInt32();
    private static double Double(JsonElement value, string property) => value.GetProperty(property).GetDouble();
    private static Guid GuidValue(JsonElement value, string property) =>
        Guid.Parse(String(value, property));

    private static Guid? OptionalGuid(JsonElement value, string property) =>
        Guid.TryParse(OptionalString(value, property), out var parsed) && parsed != Guid.Empty
            ? parsed
            : null;

    private static string Json(object value) => JsonSerializer.Serialize(value, JsonOptions);
}

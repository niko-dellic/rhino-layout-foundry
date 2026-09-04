using System.Text.Json;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Extensibility;
/// <summary>
/// Experimental JSON creation contract shared by the companion bridge and boundary tests.
/// Each layout specification owns its paper units and ordered per-detail view assignments.
/// Parsing creates no document resources. Unknown fields or enum values fail before staging.
/// </summary>
public static class AutomationLayoutRequest
{
    public static BatchCreateSheetsRequest Parse(JsonElement arguments, DocumentSnapshot snapshot)
    {
        RequireFields(arguments, "destination_folder_id", "naming_pattern", "layouts");
        var layouts = arguments.GetProperty("layouts");
        if (layouts.ValueKind != JsonValueKind.Array || layouts.GetArrayLength() == 0)
            throw new JsonException("layouts must be a non-empty array of creation specifications.");
        var specs = layouts.EnumerateArray().Select(item =>
        {
            RequireFields(item, "quantity", "page_width", "page_height", "page_units", "layout_kind", "template_id", "named_views_by_detail", "title_block");
            var layout = Text(item, "layout_kind", "single_detail") switch
            {
                "blank" => BuiltInLayoutKind.Blank,
                "single_detail" => BuiltInLayoutKind.SingleDetail,
                "two_details_horizontal" => BuiltInLayoutKind.TwoDetailsHorizontal,
                "two_details_vertical" => BuiltInLayoutKind.TwoDetailsVertical,
                "four_details_grid" => BuiltInLayoutKind.FourDetailsGrid,
                var value => throw new JsonException($"Unknown layout_kind '{value}'."),
            };
            BuiltInTitleBlockKind? titleBlock = Text(item, "title_block", "none") switch
            {
                "none" => null,
                "right" => BuiltInTitleBlockKind.RightSidebar,
                "bottom" => BuiltInTitleBlockKind.FullWidthBottom,
                var value => throw new JsonException($"Unknown title_block '{value}'."),
            };
            IReadOnlyList<string?>? views = item.TryGetProperty("named_views_by_detail", out var viewItems) ? viewItems.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.Null ? null : value.GetString()?.Trim()).ToArray() : null;
            return new LayoutCreationSpec(item.GetProperty("quantity").GetInt32(), new PaperRecipe(item.GetProperty("page_width").GetDouble(), item.GetProperty("page_height").GetDouble(), item.GetProperty("page_units").GetString()!), layout, item.TryGetProperty("template_id", out var template) ? template.GetGuid() : null, BuiltInTitleBlock: titleBlock, NamedViewsByDetail: views);
        }).ToArray();
        return new BatchCreateSheetsRequest(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision, arguments.GetProperty("destination_folder_id").GetGuid(), specs, arguments.GetProperty("naming_pattern").GetString()!, 1, 1);
    }

    private static string Text(JsonElement value, string name, string fallback) => value.TryGetProperty(name, out var item) ? item.GetString() ?? throw new JsonException($"{name} cannot be null.") : fallback;
    private static void RequireFields(JsonElement value, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new JsonException("Expected a JSON object.");
        foreach (var field in value.EnumerateObject())
            if (!allowed.Contains(field.Name, StringComparer.Ordinal))
                throw new JsonException($"Unknown creation field '{field.Name}'.");
    }
}

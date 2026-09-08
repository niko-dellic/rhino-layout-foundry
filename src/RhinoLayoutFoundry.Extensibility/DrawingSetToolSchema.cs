using System.Text.Json;
using System.Text.Json.Nodes;

namespace RhinoLayoutFoundry.Extensibility;

/// <summary>Single schema source shared by model tools and the public boundary.</summary>
public static class DrawingSetToolSchema
{
    public static JsonElement Create()
    {
        var point = Object(("x", Number()), ("y", Number()), ("z", Number()));
        var cut = Object(("origin", point.DeepClone()), ("normal", point.DeepClone()));
        var view = Object(("key", Text()), ("name", Text()),
            ("kind", new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("site_plan", "floor_plan", "elevation", "section") }),
            ("scale_denominator", Number(1, 10000)),
            ("bounds_mm", Object(("left", Number()), ("bottom", Number()), ("right", Number()), ("top", Number()))),
            ("camera_location", point.DeepClone()), ("camera_target", point.DeepClone()), ("camera_up", point.DeepClone()),
            ("cut", new JsonObject { ["anyOf"] = new JsonArray(cut, new JsonObject { ["type"] = "null" }) }),
            ("hidden_layer_ids", new JsonObject { ["type"] = "array", ["items"] = Text(), ["maxItems"] = 512 }));
        var sheet = Object(("key", Text()), ("name", Text()), ("width_mm", Number(1, 5000)),
            ("height_mm", Number(1, 5000)), ("views", Array(view, DrawingSetSpecificationValidator.MaximumViewsPerSheet)));
        var root = Object(("schema_version", new JsonObject { ["type"] = "integer", ["enum"] = new JsonArray(2) }),
            ("proposal_id", Text()), ("document_runtime_serial_number", new JsonObject { ["type"] = "integer" }),
            ("source_revision", new JsonObject { ["type"] = "integer" }), ("destination_folder_id", Text()),
            ("new_destination_folder_name", new JsonObject { ["type"] = new JsonArray("string", "null") }),
            ("sheets", Array(sheet, DrawingSetSpecificationValidator.MaximumSheets)));
        return JsonSerializer.SerializeToElement(root);
    }

    private static JsonObject Text() => new() { ["type"] = "string" };
    private static JsonObject Number(double? minimum = null, double? maximum = null)
    {
        var node = new JsonObject { ["type"] = "number" };
        if (minimum is { } min) node["minimum"] = min;
        if (maximum is { } max) node["maximum"] = max;
        return node;
    }
    private static JsonObject Array(JsonNode item, int max) => new()
    { ["type"] = "array", ["items"] = item, ["minItems"] = 1, ["maxItems"] = max };
    private static JsonObject Object(params (string Name, JsonNode Schema)[] fields)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var field in fields) { properties[field.Name] = field.Schema; required.Add(field.Name); }
        return new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = required,
            ["additionalProperties"] = false };
    }
}

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
namespace RhinoLayoutFoundry.Extensibility;

public sealed record GeometryAnchor(Guid ObjectId, IReadOnlyList<int> InstancePath, string Component, int Index, double Parameter, string Topology);
public sealed record SheetAnnotationSpecification(string Key, Guid? AnnotationId, string Kind, Guid? DetailId,
    IReadOnlyList<GeometryAnchor> Anchors, double Xmm, double Ymm, string Text, Guid? StyleId);
public sealed record SheetDetailUpdate(Guid? DetailId, DrawingViewSpecification View, Guid? DisplayModeId);
public sealed record SheetUpdate(Guid SheetId, string? TitleBlock, Guid? DimensionStyleId,
    IReadOnlyList<SheetDetailUpdate> Details, IReadOnlyList<SheetAnnotationSpecification> Annotations,
    IReadOnlyList<Guid> RemoveAnnotationIds);
public sealed record NamedViewRename(string OldName, string NewName);
public sealed record SheetUpdateSpecification(Guid ProposalId, uint DocumentRuntimeSerialNumber, long SourceRevision,
    IReadOnlyList<SheetUpdate> Sheets, IReadOnlyList<NamedViewRename> RenameViews);
public sealed record UpdateSheetsChange(SheetUpdateSpecification Specification, string Digest) : OperationChange;

public static class SheetUpdatePlanner
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 32 };
    public static SheetUpdateSpecification Parse(JsonElement json) => json.Deserialize<SheetUpdateSpecification>(Json)
        ?? throw new JsonException("An update specification is required.");
    public static string Digest(SheetUpdateSpecification spec) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(spec, Json)));
    public static OperationPlan Plan(SheetUpdateSpecification spec, DocumentSnapshot snapshot)
    {
        spec = JsonSerializer.Deserialize<SheetUpdateSpecification>(JsonSerializer.Serialize(spec, Json), Json)!;
        var errors = new List<Diagnostic>();
        void Error(string message) => errors.Add(new("sheet_update.invalid", DiagnosticSeverity.Error, message));
        if (spec.ProposalId == Guid.Empty || spec.DocumentRuntimeSerialNumber != snapshot.DocumentRuntimeSerialNumber || spec.SourceRevision != snapshot.Revision)
            Error("The document changed; inspect and restage the proposal.");
        if (snapshot.Metadata.ContainsKey(DrawingSetPlanner.ReceiptKey(spec.ProposalId))) Error("This proposal was already applied.");
        if (spec.Sheets is null || spec.RenameViews is null || spec.Sheets.Count > 50 || spec.RenameViews.Count > 100 || spec.Sheets.Count + spec.RenameViews.Count == 0)
            Error("Provide 1–50 sheet updates or named-view renames.");
        else
        {
            if (spec.Sheets.Select(s => s.SheetId).Distinct().Count() != spec.Sheets.Count) Error("Target each sheet once per batch.");
            foreach (var update in spec.Sheets)
            {
                if (!snapshot.Sheets.TryGetValue(update.SheetId, out var sheet)) { Error("A selected sheet is missing."); continue; }
                if (update.TitleBlock is not (null or "off" or "right" or "bottom")) Error("Titleblock must be off, right, bottom or null to preserve it.");
                if (update.Details is null || update.Annotations is null || update.RemoveAnnotationIds is null || update.Details.Count > 16 || update.Annotations.Count > 200)
                { Error("Invalid or excessive detail/annotation list."); continue; }
                if (update.RemoveAnnotationIds.Distinct().Count() != update.RemoveAnnotationIds.Count ||
                    update.Annotations.Where(a => a.AnnotationId is not null).Select(a => a.AnnotationId).Distinct().Count() != update.Annotations.Count(a => a.AnnotationId is not null) ||
                    update.Annotations.Any(a => a.AnnotationId is { } aid && update.RemoveAnnotationIds.Contains(aid))) Error("Conflicting or duplicate annotation targets.");
                if (update.Details.Where(d => d.DetailId is not null).Select(d => d.DetailId).Distinct().Count() != update.Details.Count(d => d.DetailId is not null)) Error("Target each detail once.");
                foreach (var detail in update.Details)
                {
                    if (detail.DetailId is { } id && !sheet.DetailIds.Contains(id)) Error("The target detail is not on the selected sheet.");
                    if (detail.DisplayModeId is { } mode && !snapshot.DisplayModes.ContainsKey(mode)) Error("The display mode is unavailable.");
                    var factor = LayoutSpacing.Uniform(1, sheet.PageUnitSystem).InUnits("Millimeters").PageEdge;
                    var synthetic = new DrawingSetSpecification(2, spec.ProposalId, spec.DocumentRuntimeSerialNumber, spec.SourceRevision,
                        sheet.FolderId, [new("sheet", "__update_validation__", sheet.PageWidth * factor, sheet.PageHeight * factor, [detail.View])])
                        { TitleBlock = update.TitleBlock ?? "off" };
                    foreach (var issue in DrawingSetSpecificationValidator.Validate(synthetic, snapshot).Issues.Where(i => i.Code != "sheet.name")) Error(issue.Message);
                }
                foreach (var a in update.Annotations)
                {
                    if (a.Kind is not ("linear" or "aligned" or "angular" or "radius" or "diameter" or "leader" or "note")) Error("Unsupported annotation kind.");
                    if (string.IsNullOrWhiteSpace(a.Key) || !double.IsFinite(a.Xmm) || !double.IsFinite(a.Ymm) || a.Anchors is null || a.Anchors.Count > 3) Error("Invalid annotation placement/references.");
                    if (a.DetailId is { } id && !sheet.DetailIds.Contains(id)) Error("Annotation detail must already exist on the selected sheet.");
                    var required = a.Kind is "linear" or "aligned" ? 2 : a.Kind == "angular" ? 3 : a.Kind == "note" ? 0 : 1;
                    if (a.Anchors?.Count != required || a.Kind != "note" && a.DetailId is null) Error("The annotation needs the correct number of measured anchors and a source detail.");
                    if (a.Anchors is not null && a.Anchors.Any(anchor => anchor.ObjectId == Guid.Empty || anchor.InstancePath is null || anchor.InstancePath.Count > 16 ||
                        anchor.InstancePath.Any(i => i < 0) || anchor.Index < 0 || !double.IsFinite(anchor.Parameter) || anchor.Parameter < 0 || anchor.Parameter > 1 ||
                        string.IsNullOrWhiteSpace(anchor.Topology) || anchor.Component is not ("vertex" or "edge" or "curve"))) Error("Invalid measured geometry reference.");
                }
            }
            foreach (var rename in spec.RenameViews)
                if (!snapshot.NamedViews.Contains(rename.OldName) || string.IsNullOrWhiteSpace(rename.NewName) ||
                    snapshot.NamedViews.Contains(rename.NewName)) Error("Named-view rename source is missing or destination is occupied.");
        }
        var summary = "Update sheets: " + string.Join("; ", (spec.Sheets ?? []).Select(s =>
            $"{snapshot.Sheets.GetValueOrDefault(s.SheetId)?.Name}: {s.Details?.Count ?? 0} view changes, {s.Annotations?.Count ?? 0} annotations, {s.RemoveAnnotationIds?.Count ?? 0} removals" +
            (s.TitleBlock is null ? "" : $", titleblock {s.TitleBlock}") + (s.DimensionStyleId is null ? "" : ", change all dimension styles (clear conflicting overrides)"))) +
            string.Join("; ", (spec.RenameViews ?? []).Select(v => $" Rename {v.OldName} to {v.NewName}"));
        return new(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision, summary,
            errors.Count == 0 ? [new UpdateSheetsChange(spec, Digest(spec))] : [], errors);
    }
    public static JsonElement Schema()
    {
        JsonObject Text() => new() { ["type"] = "string" };
        JsonObject NullableText() => new() { ["type"] = new JsonArray("string", "null") };
        JsonObject Num(bool integer = false) => new() { ["type"] = integer ? "integer" : "number" };
        JsonObject Arr(JsonNode node, int max) => new() { ["type"] = "array", ["items"] = node, ["maxItems"] = max };
        JsonObject Obj(params (string, JsonNode)[] fields)
        {
            var p = new JsonObject(); var required = new JsonArray();
            foreach (var (key, node) in fields) { p[key] = node; required.Add(key); }
            return new() { ["type"] = "object", ["properties"] = p, ["required"] = required, ["additionalProperties"] = false };
        }
        var drawing = JsonNode.Parse(DrawingSetToolSchema.Create().GetRawText())!["properties"]!["sheets"]!["items"]!["properties"]!["views"]!["items"]!.DeepClone();
        var anchor = Obj(("object_id", Text()), ("instance_path", Arr(Num(true), 16)), ("component", Text()), ("index", Num(true)), ("parameter", Num()), ("topology", Text()));
        var annotation = Obj(("key", Text()), ("annotation_id", NullableText()), ("kind", Text()), ("detail_id", NullableText()),
            ("anchors", Arr(anchor, 3)), ("xmm", Num()), ("ymm", Num()), ("text", Text()), ("style_id", NullableText()));
        var sheet = Obj(("sheet_id", Text()), ("title_block", NullableText()), ("dimension_style_id", NullableText()),
            ("details", Arr(Obj(("detail_id", NullableText()), ("view", drawing), ("display_mode_id", NullableText())), 16)),
            ("annotations", Arr(annotation, 200)), ("remove_annotation_ids", Arr(Text(), 200)));
        return JsonSerializer.SerializeToElement(Obj(("proposal_id", Text()), ("document_runtime_serial_number", Num(true)), ("source_revision", Num(true)),
            ("sheets", Arr(sheet, 50)), ("rename_views", Arr(Obj(("old_name", Text()), ("new_name", Text())), 100))));
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Extensibility;

/// <summary>A proposal, never a persisted Rhino document or an authorization to mutate one.
/// Page coordinates are millimetres; model coordinates use the inspected document's units.
/// Logical keys are local to the proposal, not Rhino object IDs.</summary>
public sealed record DrawingSetSpecification(
    int SchemaVersion, Guid ProposalId, uint DocumentRuntimeSerialNumber, long SourceRevision,
    Guid DestinationFolderId, IReadOnlyList<DrawingSheetSpecification> Sheets)
{
    public string? NewDestinationFolderName { get; init; }
}

public sealed record DrawingSheetSpecification(string Key, string Name, double WidthMm,
    double HeightMm, IReadOnlyList<DrawingViewSpecification> Views);

public sealed record DrawingViewSpecification(string Key, string Name, string Kind,
    double ScaleDenominator, DetailPageBounds BoundsMm, Point3Coordinates CameraLocation,
    Point3Coordinates CameraTarget, Vector3Coordinates CameraUp, DrawingCutSpecification? Cut)
{
    // Required explicitly in v2; absent in legacy v1 proposals.
    public IReadOnlyList<Guid>? HiddenLayerIds { get; init; }
}

public sealed record DrawingCutSpecification(Point3Coordinates Origin, Vector3Coordinates Normal);

public sealed record DrawingSetIssue(string Path, string Code, string Message);
public sealed record DrawingSetPreflight(IReadOnlyList<DrawingSetIssue> Issues, int SheetCount, int ViewCount)
{
    public bool IsValid => Issues.Count == 0;
    // Preflight is deliberately not an executable operation plan or approval token.
}

public static class DrawingSetSpecificationValidator
{
    public const int MaximumSheets = 50;
    public const int MaximumViewsPerSheet = 16;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { type =>
            {
                if (type.Kind == JsonTypeInfoKind.Object)
                    foreach (var property in type.Properties.Where(p => p.Set is not null))
                        property.IsRequired = property.Name != "hidden_layer_ids";
            } },
        },
    };

    public static DrawingSetSpecification Parse(JsonElement json)
    {
        RejectDuplicateProperties(json);
        return json.Deserialize<DrawingSetSpecification>(Options)
            ?? throw new JsonException("A drawing-set specification is required.");
    }

    public static DrawingSetPreflight Validate(DrawingSetSpecification spec, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(snapshot);
        var issues = new List<DrawingSetIssue>();
        void Error(string path, string code, string message) => issues.Add(new(path, code, message));
        if (spec.SchemaVersion is not (1 or 2)) Error("schema_version", "version.unsupported", "Use drawing-set schema version 1 or 2.");
        if (spec.ProposalId == Guid.Empty) Error("proposal_id", "proposal.id_required", "Provide a stable proposal ID.");
        if (spec.DocumentRuntimeSerialNumber != snapshot.DocumentRuntimeSerialNumber)
            Error("document_runtime_serial_number", "document.changed", "Inspect the active document again.");
        if (spec.SourceRevision != snapshot.Revision)
            Error("source_revision", "document.stale", "The document changed; refresh the proposal before approval.");
        if (spec.DestinationFolderId != snapshot.RootFolderId && !snapshot.Folders.ContainsKey(spec.DestinationFolderId))
            Error("destination_folder_id", "folder.missing", "Choose an existing Layout Foundry folder.");
        if (spec.NewDestinationFolderName is { } newFolder)
        {
            if (string.IsNullOrWhiteSpace(newFolder) || newFolder.Trim().Length > 120 || newFolder.Any(c => char.IsControl(c) || c is '/' or '\\'))
                Error("new_destination_folder_name", "folder.invalid_name", "Use a folder name of 1–120 characters without slashes or control characters.");
            if (snapshot.Folders.Values.Any(f => f.ParentId == spec.DestinationFolderId && string.Equals(f.Name, newFolder.Trim(), StringComparison.OrdinalIgnoreCase)))
                Error("new_destination_folder_name", "folder.name_conflict", "That destination folder already exists. Select it instead.");
        }
        if (spec.Sheets is null || spec.Sheets.Count is < 1 or > MaximumSheets)
        {
            Error("sheets", "sheets.count", $"Provide between 1 and {MaximumSheets} sheets.");
            return new(issues, spec.Sheets?.Count ?? 0, 0);
        }
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(snapshot.Sheets.Values.Select(s => s.Name.Trim()), StringComparer.OrdinalIgnoreCase);
        var viewCount = 0;
        for (var i = 0; i < spec.Sheets.Count; i++)
        {
            var sheet = spec.Sheets[i];
            var path = $"sheets[{i}]";
            if (sheet is null) { Error(path, "sheet.required", "A sheet cannot be null."); continue; }
            CheckKey(sheet.Key, path + ".key");
            if (!ValidName(sheet.Name) || !names.Add(sheet.Name.Trim()))
                Error(path + ".name", "sheet.name", "Use a unique readable sheet name, up to 120 characters.");
            if (!Positive(sheet.WidthMm, 5000) || !Positive(sheet.HeightMm, 5000))
                Error(path, "paper.invalid", "Paper dimensions must be positive millimetres, no larger than 5000.");
            if (sheet.Views is null || sheet.Views.Count is < 1 or > MaximumViewsPerSheet)
            { Error(path + ".views", "views.count", $"Provide 1–{MaximumViewsPerSheet} views per sheet."); continue; }
            viewCount += sheet.Views.Count;
            for (var j = 0; j < sheet.Views.Count; j++)
            {
                var view = sheet.Views[j];
                var vp = $"{path}.views[{j}]";
                if (view is null) { Error(vp, "view.required", "A view cannot be null."); continue; }
                CheckKey(view.Key, vp + ".key");
                if (!ValidName(view.Name)) Error(vp + ".name", "view.name", "Provide a readable drawing title, up to 120 characters.");
                if (spec.SchemaVersion == 1 && view.HiddenLayerIds is not null)
                    Error(vp + ".hidden_layer_ids", "version.feature", "Per-view visibility requires schema version 2.");
                if (spec.SchemaVersion == 2)
                {
                    if (view.HiddenLayerIds is null || view.HiddenLayerIds.Count > 512 ||
                        view.HiddenLayerIds.Distinct().Count() != view.HiddenLayerIds.Count ||
                        view.HiddenLayerIds.Any(id => !snapshot.Layers.ContainsKey(id)))
                        Error(vp + ".hidden_layer_ids", "visibility.layers", "Provide an explicit list of at most 512 distinct existing layer IDs; use [] to retain current visibility.");
                    if (view.BoundsMm is { } frame && (frame.Left < 10 || frame.Right > sheet.WidthMm - 10 ||
                        frame.Bottom < 18 || frame.Top > sheet.HeightMm - 20))
                        Error(vp + ".bounds_mm", "frame.caption_space", "Reserve 10 mm side margins, 18 mm below the lowest view and 20 mm above the highest view for captions and sheet title.");
                    // A caption occupies the 8 mm band immediately below its frame.
                    if (view.BoundsMm is { IsValid: true } captionFrame)
                        for (var k = 0; k < j; k++)
                            if (sheet.Views[k]?.BoundsMm is { IsValid: true } otherFrame &&
                                captionFrame.Left < otherFrame.Right && captionFrame.Right > otherFrame.Left &&
                                captionFrame.Bottom - 8 < otherFrame.Top && captionFrame.Top > otherFrame.Bottom - 8)
                                Error(vp + ".bounds_mm", "caption.overlap", "Drawing frames and their 8 mm caption bands must not overlap.");
                }
                if (view.Kind is not ("site_plan" or "floor_plan" or "elevation" or "section"))
                    Error(vp + ".kind", "view.kind", "This contract supports site plans, floor plans, elevations and sections.");
                if (!Positive(view.ScaleDenominator, 10000) || view.ScaleDenominator < 1)
                    Error(vp + ".scale_denominator", "scale.invalid", "Use a scale denominator between 1 and 10000.");
                if (view.BoundsMm is not { IsValid: true } bounds || bounds.Left < 0 || bounds.Bottom < 0 ||
                    bounds.Right > sheet.WidthMm || bounds.Top > sheet.HeightMm)
                    Error(vp + ".bounds_mm", "frame.invalid", "The detail must fit entirely inside the sheet.");
                else
                    for (var k = 0; k < j; k++)
                        if (sheet.Views[k]?.BoundsMm is { IsValid: true } other &&
                            bounds.Left < other.Right && bounds.Right > other.Left &&
                            bounds.Bottom < other.Top && bounds.Top > other.Bottom)
                            Error(vp + ".bounds_mm", "frame.overlap", $"This detail overlaps view {k + 1}.");
                var direction = new Vector3Coordinates(view.CameraTarget.X - view.CameraLocation.X,
                    view.CameraTarget.Y - view.CameraLocation.Y, view.CameraTarget.Z - view.CameraLocation.Z);
                if (!view.CameraLocation.IsFinite || !view.CameraTarget.IsFinite || !Independent(direction, view.CameraUp))
                    Error(vp, "camera.invalid", "Provide finite, distinct camera and target coordinates with a nonparallel up vector.");
                if (view.Kind is "floor_plan" or "section" && view.Cut is null)
                    Error(vp + ".cut", "cut.required", "The agent must propose an explicit cut for review; do not ask users for camera coordinates.");
                if (view.Cut is { } cut && (!cut.Origin.IsFinite || !cut.Normal.IsFinite ||
                    !double.IsFinite(cut.Normal.Length) || cut.Normal.Length <= 1e-9))
                    Error(vp + ".cut", "cut.invalid", "Provide a finite cut origin and a nonzero normal.");
            }
        }
        return new(issues, spec.Sheets.Count, viewCount);

        void CheckKey(string key, string path)
        {
            if (!ValidName(key) || !keys.Add(key.Trim()))
                Error(path, "key.invalid", "Use a nonempty, unique logical key for every sheet and view.");
        }
    }

    private static bool ValidName(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value.Trim().Length <= 120 && !value.Any(char.IsControl);
    private static bool Positive(double value, double maximum) => double.IsFinite(value) && value > 0 && value <= maximum;
    private static bool Independent(Vector3Coordinates a, Vector3Coordinates b) =>
        a.IsFinite && b.IsFinite && double.IsFinite(a.Length) && double.IsFinite(b.Length) &&
        a.Length > 1e-9 && b.Length > 1e-9 &&
        Math.Abs((a.X / a.Length) * (b.X / b.Length) + (a.Y / a.Length) * (b.Y / b.Length) +
                 (a.Z / a.Length) * (b.Z / b.Length)) < 0.9999;

    private static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException($"Duplicate field '{property.Name}'.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RejectDuplicateProperties(item);
    }
}

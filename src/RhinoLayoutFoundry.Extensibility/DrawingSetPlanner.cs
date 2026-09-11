using System.Security.Cryptography;
using System.Text.Json;
using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Extensibility;

public sealed record CreateDrawingSetChange(DrawingSetSpecification Specification, string Digest) : OperationChange;

/// <summary>Compiles a complete proposal to one frozen, revision-bound approval unit.</summary>
public static class DrawingSetPlanner
{
    public static string ReceiptKey(Guid proposalId) => $"RhinoLayoutFoundry.DrawingSet.{proposalId:D}";
    public static string Digest(DrawingSetSpecification specification) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(specification)));

    public static OperationPlan Plan(DrawingSetSpecification specification, DocumentSnapshot snapshot)
    {
        // Own nested collections before returning the plan to any caller.
        var frozen = JsonSerializer.Deserialize<DrawingSetSpecification>(JsonSerializer.Serialize(specification))!;
        var validation = DrawingSetSpecificationValidator.Validate(frozen, snapshot);
        var diagnostics = validation.Issues.Select(issue => new Diagnostic(issue.Code,
            DiagnosticSeverity.Error, $"{issue.Path}: {issue.Message}")).ToList();
        if (snapshot.Metadata.ContainsKey(ReceiptKey(frozen.ProposalId)))
            diagnostics.Add(new("drawing_set.already_applied", DiagnosticSeverity.Error,
                "This proposal has already been applied. Inspect the existing sheets instead of repeating it."));
        var summary = validation.IsValid
            ? "Create drawing set: " + string.Join("; ", frozen.Sheets.Select(s =>
                $"{s.Name} ({s.WidthMm:g} × {s.HeightMm:g} mm): " + string.Join(", ", s.Views.Select(v =>
                    $"{v.Name}, 1:{v.ScaleDenominator:g}{(v.Cut is null ? "" : ", clipped")}" +
                    (v.HiddenLayerIds is { Count: > 0 } hidden ? ", hides: " + string.Join(", ", hidden.Select(id => snapshot.Layers[id])) : ""))))) +
                ". Includes editable sheet titles and drawing/scale captions. Titleblock: " + frozen.TitleBlock +
                ". Display mode: " + (frozen.DisplayModeId is { } mode ? snapshot.DisplayModes.GetValueOrDefault(mode, "Unavailable") : "Foundry drawing default") + "."
            : "Create drawing set";
        return new(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision, summary,
            diagnostics.Count == 0 ? [new CreateDrawingSetChange(frozen, Digest(frozen))] : [], diagnostics);
    }
}

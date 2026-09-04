using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Operations;

public sealed record UpdateProjectInformationRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    ProjectInformation Information);

public sealed class UpdateProjectInformationPlanner : IOperationPlanner<UpdateProjectInformationRequest>
{
    public OperationPlan Plan(UpdateProjectInformationRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = SheetPlanValidation.ValidateContext(
            request.DocumentRuntimeSerialNumber, request.SourceRevision, snapshot);
        Validate(request.Information, diagnostics);
        IReadOnlyList<OperationChange> changes = diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error)
            ? []
            : [new UpdateProjectInformationChange(snapshot.ProjectInfo, request.Information)];
        return new OperationPlan(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision,
            "Update project information", changes, diagnostics);
    }

    internal static void Validate(ProjectInformation information, ICollection<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(information);
        if (information.Logo is { } logo)
        {
            if (logo.Data.Length == 0 || logo.Data.Length > 5 * 1024 * 1024)
                diagnostics.Add(Error("project.logo_size", "The firm logo must be between 1 byte and 5 MB."));
            if (logo.MediaType is not ("image/png" or "image/jpeg"))
                diagnostics.Add(Error("project.logo_type", "The firm logo must be a PNG or JPEG image."));
            if (string.IsNullOrWhiteSpace(logo.Sha256))
                diagnostics.Add(Error("project.logo_hash", "The firm logo fingerprint is missing."));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in information.CustomFields)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                diagnostics.Add(Error("project.custom_key", "Custom project field labels cannot be empty."));
            else if (!seen.Add(pair.Key.Trim()))
                diagnostics.Add(Error("project.custom_duplicate", $"Custom project field '{pair.Key}' is duplicated."));
        }

        var configured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in information.ContentOptions?.CustomFields ?? [])
        {
            if (string.IsNullOrWhiteSpace(option.Label) || !information.CustomFields.ContainsKey(option.Label))
                diagnostics.Add(Error("project.custom_option_missing",
                    "Every configured custom title-block field must have a matching project value."));
            else if (!configured.Add(option.Label.Trim()))
                diagnostics.Add(Error("project.custom_option_duplicate",
                    $"Custom title-block field '{option.Label}' is configured more than once."));
        }
    }

    private static Diagnostic Error(string code, string message) =>
        new(code, DiagnosticSeverity.Error, message);
}

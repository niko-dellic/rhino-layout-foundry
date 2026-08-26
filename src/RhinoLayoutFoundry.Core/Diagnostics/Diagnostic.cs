namespace RhinoLayoutFoundry.Core.Diagnostics;

public enum DiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

public sealed record Diagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    Guid? EntityId = null);


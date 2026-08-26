using System.Globalization;
using System.Text.RegularExpressions;
using RhinoLayoutFoundry.Core.Diagnostics;

namespace RhinoLayoutFoundry.Core.Naming;

public sealed record NamingItem(
    Guid SheetId,
    string CurrentName,
    IReadOnlyDictionary<string, string> Tokens);

public sealed record NamingRequest(
    string Pattern,
    IReadOnlyList<NamingItem> Items,
    int Start,
    int Step);

public sealed record NamingPreviewEntry(
    Guid SheetId,
    string CurrentName,
    string ProposedName);

public sealed record NamingPreview(
    IReadOnlyList<NamingPreviewEntry> Entries,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool CanApply => Diagnostics.All(item => item.Severity != DiagnosticSeverity.Error);
}

public static partial class NamingEngine
{
    private static readonly HashSet<string> SupportedTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "project",
        "discipline",
        "folder",
        "tag",
        "view",
        "index",
    };

    public static NamingPreview Preview(NamingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entries = new List<NamingPreviewEntry>(request.Items.Count);
        var diagnostics = new List<Diagnostic>();
        var index = request.Start;

        foreach (var item in request.Items)
        {
            var proposed = TokenPattern().Replace(
                request.Pattern,
                match => ResolveToken(match, item, index, diagnostics));

            if (proposed.Contains('{', StringComparison.Ordinal) || proposed.Contains('}', StringComparison.Ordinal))
            {
                diagnostics.Add(new Diagnostic(
                    "NAME_PATTERN_INVALID",
                    DiagnosticSeverity.Error,
                    "The naming pattern contains an unmatched or invalid brace.",
                    item.SheetId));
            }

            proposed = proposed.Trim();
            if (string.IsNullOrWhiteSpace(proposed))
            {
                diagnostics.Add(new Diagnostic(
                    "NAME_EMPTY",
                    DiagnosticSeverity.Error,
                    "A naming rule produced an empty sheet name.",
                    item.SheetId));
            }

            entries.Add(new NamingPreviewEntry(item.SheetId, item.CurrentName, proposed));
            index = checked(index + request.Step);
        }

        foreach (var duplicate in entries
                     .Where(entry => !string.IsNullOrWhiteSpace(entry.ProposedName))
                     .GroupBy(entry => entry.ProposedName, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            foreach (var entry in duplicate)
            {
                diagnostics.Add(new Diagnostic(
                    "NAME_DUPLICATE",
                    DiagnosticSeverity.Error,
                    $"The proposed sheet name '{entry.ProposedName}' is duplicated.",
                    entry.SheetId));
            }
        }

        return new NamingPreview(entries, diagnostics);
    }

    private static string ResolveToken(
        Match match,
        NamingItem item,
        int index,
        ICollection<Diagnostic> diagnostics)
    {
        var token = match.Groups["name"].Value;
        var format = match.Groups["format"].Success
            ? match.Groups["format"].Value
            : null;

        if (!SupportedTokens.Contains(token))
        {
            diagnostics.Add(new Diagnostic(
                "NAME_TOKEN_UNKNOWN",
                DiagnosticSeverity.Error,
                $"The naming token '{token}' is not supported.",
                item.SheetId));
            return string.Empty;
        }

        if (token.Equals("index", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return index.ToString(format ?? "0", CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                diagnostics.Add(new Diagnostic(
                    "NAME_INDEX_FORMAT_INVALID",
                    DiagnosticSeverity.Error,
                    $"The index format '{format}' is invalid.",
                    item.SheetId));
                return string.Empty;
            }
        }

        if (format is not null)
        {
            diagnostics.Add(new Diagnostic(
                "NAME_TOKEN_FORMAT_UNSUPPORTED",
                DiagnosticSeverity.Error,
                $"Only the index token accepts a format; '{token}' does not.",
                item.SheetId));
        }

        if (item.Tokens.TryGetValue(token, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        diagnostics.Add(new Diagnostic(
            "NAME_TOKEN_MISSING",
            DiagnosticSeverity.Warning,
            $"The sheet has no value for naming token '{token}'.",
            item.SheetId));
        return string.Empty;
    }

    [GeneratedRegex(@"\{(?<name>[A-Za-z][A-Za-z0-9]*)(?::(?<format>[^{}]+))?\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}


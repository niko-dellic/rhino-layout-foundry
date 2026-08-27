using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Operations;

public enum BatchPropertyKind
{
    NamePattern,
    PaperSize,
    Orientation,
    DetailDisplayMode,
    DetailScale,
    Tags,
}

public sealed record BatchTarget(
    OverviewNodeKey Key,
    string Label,
    bool Included = true);

public sealed record BatchPropertiesValidation(
    bool CanApply,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed class BatchPropertiesSession
{
    private readonly Dictionary<OverviewNodeKey, BatchTarget> _targets;
    private readonly Dictionary<BatchPropertyKind, string> _stagedValues = [];

    public BatchPropertiesSession(
        uint documentRuntimeSerialNumber,
        long sourceRevision,
        IEnumerable<BatchTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        DocumentRuntimeSerialNumber = documentRuntimeSerialNumber;
        SourceRevision = sourceRevision;
        _targets = targets
            .GroupBy(target => target.Key)
            .Select(group => group.First())
            .ToDictionary(target => target.Key);
    }

    public uint DocumentRuntimeSerialNumber { get; }

    public long SourceRevision { get; }

    public bool HasConflict { get; private set; }

    public bool IsDirty => _stagedValues.Count > 0 || _targets.Values.Any(target => !target.Included);

    public IReadOnlyCollection<BatchTarget> Targets => _targets.Values;

    public IReadOnlyDictionary<BatchPropertyKind, string> StagedValues => _stagedValues;

    public void SetIncluded(OverviewNodeKey key, bool included)
    {
        if (_targets.TryGetValue(key, out var target))
        {
            _targets[key] = target with { Included = included };
        }
    }

    public void Stage(BatchPropertyKind property, string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            _stagedValues.Remove(property);
        }
        else
        {
            _stagedValues[property] = normalized;
        }
    }

    public void Revalidate(uint documentRuntimeSerialNumber, long currentRevision)
    {
        HasConflict = documentRuntimeSerialNumber != DocumentRuntimeSerialNumber ||
                      currentRevision != SourceRevision;
    }

    public BatchPropertiesValidation Validate(bool mutationCapabilityAvailable)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (_targets.Values.All(target => !target.Included))
        {
            errors.Add("Include at least one target.");
        }

        if (_stagedValues.Count == 0)
        {
            errors.Add("Choose at least one property to change.");
        }

        if (HasConflict)
        {
            errors.Add("The Rhino document changed while this editor was open.");
        }

        if (!mutationCapabilityAvailable)
        {
            warnings.Add("Apply remains disabled until the Rhino page-property Undo capability is verified.");
        }

        return new BatchPropertiesValidation(
            errors.Count == 0 && mutationCapabilityAvailable,
            errors,
            warnings);
    }
}

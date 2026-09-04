using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Persistence;

public enum LayoutPackageImportMode
{
    Merge,
    Replace,
}

public enum LayoutPackageConflictResolution
{
    ReuseDestination,
    ImportRenamedCopy,
    OverwriteDestination,
}

public enum LayoutPackageDependencyKind
{
    DisplayMode,
    NamedView,
    NamedLayerState,
    BlockDefinition,
}

public sealed record LayoutPackageManifest(
    int PackageVersion,
    string SourceDocumentName,
    DateTimeOffset CreatedUtc,
    string ProducerVersion,
    DocumentState FoundryState,
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyList<LayoutPackageSheet> Sheets,
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyList<LayoutPackageNamedView> NamedViews,
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyList<LayoutPackageNamedLayerState> NamedLayerStates,
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyList<LayoutPackageDisplayMode> DisplayModes,
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyDictionary<string, string> AssetChecksums)
{
    public const int CurrentPackageVersion = 6;
    public const string ManifestEntryName = "manifest.json";
    public const string LayoutAssetEntryName = "assets/layouts.3dm";

    [JsonRequired]
    public IReadOnlyList<LayoutPackageBlockDefinition> BlockDefinitions { get; init; } = [];
}

public sealed record LayoutPackageSheet(
    Guid SourcePageViewId,
    Guid SourceFolderId,
    int Order,
    string Name,
    PaperRecipe Paper,
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyList<LayoutPackageDetail> Details,
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyList<Guid> PageSpaceObjectIds,
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyDictionary<string, string> Metadata,
    TitleBlockRole? TitleBlock,
    bool IncludeInPrintAll,
    SheetTitleBlockData? TitleBlockData = null,
    SheetNamingBinding? NamingBinding = null,
    string Notes = "")
{
    [JsonRequired]
    public IReadOnlyDictionary<Guid, string> DetailNamedViews { get; init; } = new Dictionary<Guid, string>();
}

public sealed record LayoutPackageDetail(
    Guid SourceDetailViewportId,
    DetailSlotRecipe Recipe,
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyList<LayoutPackageLayerOverride> LayerOverrides);

public sealed record LayoutPackageLayerOverride(
    string LayerFullPath,
    bool IsVisible);

public sealed record LayoutPackageNamedView(
    string Name,
    string Fingerprint,
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyList<double> CameraLocation,
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyList<double> CameraTarget,
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyList<double> CameraUp,
    bool IsPerspective);

public sealed record LayoutPackageNamedLayerState(
    string Name,
    string Fingerprint);

public sealed record LayoutPackageDisplayMode(
    Guid SourceId,
    string Name,
    string Fingerprint,
    bool IsBuiltIn,
    string? AssetPath);

public sealed record LayoutPackageBlockDefinition(
    Guid SourceId,
    string Name,
    string Fingerprint);

public sealed record LayoutPackageConflict(
    string Key,
    LayoutPackageDependencyKind Kind,
    string Name,
    string Message,
    LayoutPackageConflictResolution RecommendedResolution,
    bool CanOverwrite);

public sealed record LayoutPackagePreflight(
    bool IsValid,
    string FilePath,
    LayoutPackageManifest? Manifest,
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyList<LayoutPackageConflict> Conflicts,
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyList<string> Warnings,
    string? ErrorMessage = null);

public sealed record LayoutPackageExportRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    string FilePath);

public sealed record LayoutPackageExportResult(
    bool Succeeded,
    int LayoutCount,
    string? ErrorMessage = null);

public sealed record LayoutPackageImportRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    string FilePath,
    LayoutPackageImportMode Mode,
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyDictionary<string, LayoutPackageConflictResolution>? ConflictResolutions = null,
    bool ImportProjectInformation = false);

public static class LayoutPackageProjectInformationPolicy
{
    public static ProjectInformation Resolve(
        ProjectInformation destination,
        ProjectInformation source,
        LayoutPackageImportMode mode,
        bool importProjectInformation)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(source);
        if (!importProjectInformation) return destination;
        if (mode == LayoutPackageImportMode.Replace) return source;

        static string Prefer(string destinationValue, string sourceValue) =>
            string.IsNullOrWhiteSpace(destinationValue) ? sourceValue : destinationValue;
        var custom = destination.CustomFields
            .Concat(source.CustomFields.Where(pair => !destination.CustomFields.ContainsKey(pair.Key)))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var destinationOptions = destination.ContentOptions;
        var sourceOptions = source.ContentOptions;
        var customOptions = destinationOptions.CustomFields.ToList();
        var configuredCustomFields = customOptions.Select(item => item.Label)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        customOptions.AddRange(sourceOptions.CustomFields.Where(item => configuredCustomFields.Add(item.Label)));
        return destination with
        {
            ProjectName = Prefer(destination.ProjectName, source.ProjectName),
            ProjectNumber = Prefer(destination.ProjectNumber, source.ProjectNumber),
            ClientName = Prefer(destination.ClientName, source.ClientName),
            SiteAddress = Prefer(destination.SiteAddress, source.SiteAddress),
            ProjectPhase = Prefer(destination.ProjectPhase, source.ProjectPhase),
            ProjectStatus = Prefer(destination.ProjectStatus, source.ProjectStatus),
            FirmName = Prefer(destination.FirmName, source.FirmName),
            FirmAddress = Prefer(destination.FirmAddress, source.FirmAddress),
            FirmPhone = Prefer(destination.FirmPhone, source.FirmPhone),
            FirmEmail = Prefer(destination.FirmEmail, source.FirmEmail),
            FirmWebsite = Prefer(destination.FirmWebsite, source.FirmWebsite),
            FirmRegistration = Prefer(destination.FirmRegistration, source.FirmRegistration),
            IssueDate = Prefer(destination.IssueDate, source.IssueDate),
            IssuePurpose = Prefer(destination.IssuePurpose, source.IssuePurpose),
            DrawnBy = Prefer(destination.DrawnBy, source.DrawnBy),
            CheckedBy = Prefer(destination.CheckedBy, source.CheckedBy),
            ApprovedBy = Prefer(destination.ApprovedBy, source.ApprovedBy),
            CustomFields = custom,
            Logo = destination.Logo ?? source.Logo,
            ContentOptions = destinationOptions with { CustomFields = customOptions },
        };
    }
}

public sealed record LayoutPackageImportResult(
    bool Succeeded,
    int LayoutCount,
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyList<string> Warnings,
    string? ErrorMessage = null,
    string? RecoveryPackagePath = null);

public interface ILayoutPackageService
{
    Task<LayoutPackageExportResult> ExportAsync(
        LayoutPackageExportRequest request,
        CancellationToken cancellationToken = default);

    Task<LayoutPackagePreflight> PreflightAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<LayoutPackageImportResult> ImportAsync(
        LayoutPackageImportRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record LayoutPackageContents(
    LayoutPackageManifest Manifest,
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyDictionary<string, byte[]> Assets);

public static class LayoutPackageArchive
{
    private const long MaximumExpandedBytes = 512L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
    };

    public static void Write(
        string filePath,
        LayoutPackageManifest manifest,
        IReadOnlyDictionary<string, byte[]> assets)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(assets);
        ValidateManifest(manifest);

        var normalizedAssets = assets.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => ValidateEntryName(pair.Key), pair => pair.Value, StringComparer.Ordinal);
        var checksums = normalizedAssets.ToDictionary(
            pair => pair.Key,
            pair => Sha256(pair.Value),
            StringComparer.Ordinal);
        var persisted = manifest with
        {
            AssetChecksums = checksums,
        };

        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath))
            ?? throw new InvalidOperationException("The package path has no parent folder.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                WriteEntry(archive, LayoutPackageManifest.ManifestEntryName,
                    JsonSerializer.SerializeToUtf8Bytes(persisted, JsonOptions));
                foreach (var asset in normalizedAssets)
                    WriteEntry(archive, asset.Key, asset.Value);
            }

            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public static LayoutPackageContents Read(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        using var stream = File.OpenRead(filePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count == 0)
            throw new InvalidDataException("The layout package is empty.");

        var totalLength = archive.Entries.Sum(entry => entry.Length);
        if (totalLength > MaximumExpandedBytes)
            throw new InvalidDataException("The layout package expands beyond the supported size limit.");
        foreach (var entry in archive.Entries) ValidateEntryName(entry.FullName);
        var duplicateEntry = archive.Entries.GroupBy(entry => entry.FullName, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateEntry is not null)
            throw new InvalidDataException($"The layout package contains duplicate entry '{duplicateEntry.Key}'.");

        var manifestEntry = archive.GetEntry(LayoutPackageManifest.ManifestEntryName)
            ?? throw new InvalidDataException("The layout package has no manifest.json entry.");
        var manifestBytes = ReadEntry(manifestEntry);
        var manifest = JsonSerializer.Deserialize<LayoutPackageManifest>(manifestBytes, JsonOptions)
            ?? throw new InvalidDataException("The layout package manifest is empty.");
        ValidateManifest(manifest)
;

        var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var expected in manifest.AssetChecksums.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var name = ValidateEntryName(expected.Key);
            var entry = archive.GetEntry(name)
                ?? throw new InvalidDataException($"The package asset '{name}' is missing.");
            var bytes = ReadEntry(entry);
            if (!string.Equals(Sha256(bytes), expected.Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The package asset '{name}' failed its checksum.");
            assets.Add(name, bytes);
        }

        if (!assets.ContainsKey(LayoutPackageManifest.LayoutAssetEntryName))
            throw new InvalidDataException("The package has no layouts.3dm asset.");
        return new LayoutPackageContents(manifest, assets);
    }

    public static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void ValidateManifest(LayoutPackageManifest manifest)
    {
        if (manifest.PackageVersion != LayoutPackageManifest.CurrentPackageVersion)
            throw new NotSupportedException($"Layout package version {manifest.PackageVersion} is unsupported; expected {LayoutPackageManifest.CurrentPackageVersion}.");
        if (manifest.FoundryState is null || manifest.Sheets is null || manifest.NamedViews is null || manifest.NamedLayerStates is null || manifest.DisplayModes is null || manifest.AssetChecksums is null || manifest.BlockDefinitions is null)
            throw new InvalidDataException("The layout package manifest is incomplete.");
        DocumentStateSerializer.Validate(manifest.FoundryState);
        if (manifest.Sheets.Any(sheet => sheet is null || sheet.Paper is null || sheet.Details is null || sheet.PageSpaceObjectIds is null || sheet.Metadata is null || sheet.DetailNamedViews is null || sheet.Details.Any(detail => detail is null || detail.Recipe is null || detail.LayerOverrides is null)) || manifest.BlockDefinitions.Any(item => item is null) || manifest.NamedViews.Any(item => item is null) || manifest.NamedLayerStates.Any(item => item is null) || manifest.DisplayModes.Any(item => item is null))
            throw new InvalidDataException("The layout package contains missing required values.");
        if (manifest.Sheets.Select(sheet => sheet.SourcePageViewId).Distinct().Count() != manifest.Sheets.Count)
            throw new InvalidDataException("The layout package contains duplicate layout identities.");
    }

    private static string ValidateEntryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name) ||
            name.Contains('\\') ||
            name.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException($"The package contains an unsafe archive path: '{name}'.");
        return name;
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var output = entry.Open();
        output.Write(bytes);
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var input = entry.Open();
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }
}

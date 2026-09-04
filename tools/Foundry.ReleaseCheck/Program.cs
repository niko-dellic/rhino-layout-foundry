using System.Reflection;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

// Stages only verified, allowlisted files. Never loads the Rhino assemblies into this process.
try
{
    if (args.Length == 4 && args[0] == "--verify-package")
    {
        VerifyPackage(args[1], args[2], args[3]);
        return 0;
    }
    if (args.Length != 4) throw new ArgumentException("Usage: Foundry.ReleaseCheck REPOSITORY BUILD_OUTPUT STAGING_DIRECTORY MacOS|Windows");
    var repository = Path.GetFullPath(args[0]);
    var source = Path.GetFullPath(args[1]);
    var destination = Path.GetFullPath(args[2]);
    var platform = args[3];
    if (platform is not ("MacOS" or "Windows")) throw new ArgumentException("Portable builds cannot be distributed.");
    if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
        throw new IOException("The staging directory must be empty. Use a new candidate directory.");
    var version = XDocument.Load(Path.Combine(repository, "Version.props")).Descendants("Version").Single().Value;
    var assemblyVersion = Version.Parse(version.Split('-')[0]);
    var binaries = new[] { "RhinoLayoutFoundry.rhp", "RhinoLayoutFoundry.Core.dll", "RhinoLayoutFoundry.UI.dll", "RhinoLayoutFoundry.Extensibility.dll" };
    foreach (var binary in binaries)
    {
        var path = Path.Combine(source, binary);
        var actual = AssemblyName.GetAssemblyName(path).Version!;
        if (actual.Major != assemblyVersion.Major || actual.Minor != assemblyVersion.Minor || actual.Build != assemblyVersion.Build)
            throw new InvalidDataException($"Assembly version mismatch: {binary} ({actual}, expected {version}).");
        if (PlatformMarker(path) != platform)
            throw new InvalidDataException($"Platform mismatch: {binary} must be built explicitly for {platform}.");
    }
    using (var runtime = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "RhinoLayoutFoundry.runtimeconfig.json"))))
    {
        if (runtime.RootElement.GetProperty("runtimeOptions").GetProperty("tfm").GetString() != "net8.0")
            throw new InvalidDataException("Rhino bundles must target net8.0.");
    }
    using (var deps = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "RhinoLayoutFoundry.deps.json"))))
    {
        if (!deps.RootElement.GetProperty("libraries").EnumerateObject().Any(item => item.Name == $"RhinoLayoutFoundry/{version}"))
            throw new InvalidDataException("The dependency manifest does not match the release version.");
    }
    Directory.CreateDirectory(destination);
    foreach (var file in binaries.Concat(new[] { "RhinoLayoutFoundry.deps.json", "RhinoLayoutFoundry.runtimeconfig.json" }))
        File.Copy(Path.Combine(source, file), Path.Combine(destination, file));
    foreach (var file in new[] { "LICENSE", "CHANGELOG.md", "THIRD_PARTY_NOTICES.md" })
        File.Copy(Path.Combine(repository, file), Path.Combine(destination, file));
    File.Copy(Path.Combine(repository, "docs", "RECOVERY.md"), Path.Combine(destination, "RECOVERY.md"));
    // The canonical source README also serves the package; keep its source links usable outside a checkout.
    var readme = File.ReadAllText(Path.Combine(repository, "README.md"));
    readme = Regex.Replace(readme, @"\]\(([^)]+)\)", match =>
    {
        var target = match.Groups[1].Value;
        if (target == "docs/RECOVERY.md") return "](RECOVERY.md)";
        if (Uri.TryCreate(target, UriKind.Absolute, out _) || target.StartsWith('#')) return match.Value;
        return $"](https://github.com/niko-dellic/rhino-layout-foundry/blob/main/{target})";
    });
    File.WriteAllText(Path.Combine(destination, "README.md"), readme);

    var manifest = File.ReadAllText(Path.Combine(repository, "packaging", "yak", "manifest.template.yml"));
    File.WriteAllText(Path.Combine(destination, "manifest.yml"), manifest.Replace("${VERSION}", version, StringComparison.Ordinal));
    // Yak rewrites manifest.yml when packaging; the final package hash covers it.
    var hashes = Directory.GetFiles(destination).Where(path => Path.GetFileName(path) != "manifest.yml").OrderBy(Path.GetFileName, StringComparer.Ordinal)
        .Select(path => $"{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()}  {Path.GetFileName(path)}");
    File.WriteAllLines(Path.Combine(destination, "SHA256SUMS"), hashes);
    Console.WriteLine($"Verified {version} {platform} bundle: {destination}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static string? PlatformMarker(string path)
{
    using var stream = File.OpenRead(path);
    using var pe = new PEReader(stream);
    var metadata = pe.GetMetadataReader();
    foreach (var handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
    {
        var attribute = metadata.GetCustomAttribute(handle);
        if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
        var member = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
        if (member.Parent.Kind != HandleKind.TypeReference) continue;
        var type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
        if (metadata.GetString(type.Name) != "AssemblyMetadataAttribute") continue;
        var value = metadata.GetBlobReader(attribute.Value);
        if (value.ReadUInt16() == 1 && value.ReadSerializedString() == "FoundryPlatform")
            return value.ReadSerializedString();
    }
    return null;
}

static void VerifyPackage(string repository, string path, string platform)
{
    if (platform is not ("MacOS" or "Windows")) throw new ArgumentException("Expected MacOS or Windows.");
    var version = XDocument.Load(Path.Combine(repository, "Version.props")).Descendants("Version").Single().Value;
    var expected = new HashSet<string>(StringComparer.Ordinal)
    {
        "RhinoLayoutFoundry.rhp", "RhinoLayoutFoundry.Core.dll", "RhinoLayoutFoundry.UI.dll",
        "RhinoLayoutFoundry.Extensibility.dll", "RhinoLayoutFoundry.deps.json", "RhinoLayoutFoundry.runtimeconfig.json",
        "README.md", "LICENSE", "CHANGELOG.md", "THIRD_PARTY_NOTICES.md", "RECOVERY.md", "manifest.yml", "SHA256SUMS",
    };
    using var zip = ZipFile.OpenRead(path);
    if (zip.Entries.Count != expected.Count || !expected.SetEquals(zip.Entries.Select(entry => entry.FullName)))
        throw new InvalidDataException("Package contains missing, duplicate, or unexpected files.");
    string Read(string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }
    var manifest = Read("manifest.yml");
    var yakPlatform = platform == "MacOS" ? "mac" : "win";
    foreach (var line in new[] { "name: rhino-layout-foundry", $"version: {version}", $"platform: {yakPlatform}" })
        if (!manifest.Split('\n').Select(value => value.TrimEnd('\r')).Contains(line))
            throw new InvalidDataException($"Package manifest is missing '{line}'.");
    var covered = new HashSet<string>(StringComparer.Ordinal);
    foreach (var line in Read("SHA256SUMS").Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
        var pieces = line.TrimEnd('\r').Split("  ", 2, StringSplitOptions.None);
        if (pieces.Length != 2 || !covered.Add(pieces[1]) || !expected.Contains(pieces[1]))
            throw new InvalidDataException("Invalid package checksum entry.");
        using var stream = zip.GetEntry(pieces[1])!.Open();
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(stream)), pieces[0], StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Package checksum mismatch: {pieces[1]}.");
    }
    expected.ExceptWith(new[] { "manifest.yml", "SHA256SUMS" });
    if (!covered.SetEquals(expected)) throw new InvalidDataException("Package checksums do not cover every payload file.");
    var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "PACKAGE-SHA256.txt"), $"{hash}  {Path.GetFileName(path)}\n");
    Console.WriteLine($"Verified package contents, manifest and payload hashes: {hash}");
}

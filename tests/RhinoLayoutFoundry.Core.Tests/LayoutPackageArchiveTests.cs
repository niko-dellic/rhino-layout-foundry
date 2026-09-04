using System.IO.Compression;
using System.Text.Json;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Persistence;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class LayoutPackageArchiveTests
{
    [Fact]
    public void PackageRoundTripsAndValidatesChecksums()
    {
        var path = TemporaryPath();
        try
        {
            var manifest = Manifest();
            var assets = new Dictionary<string, byte[]>
            {
                [LayoutPackageManifest.LayoutAssetEntryName] = [1, 2, 3, 4],
                ["display-modes/custom.ini"] = [9, 8, 7],
            };

            LayoutPackageArchive.Write(path, manifest, assets);
            var restored = LayoutPackageArchive.Read(path);

            Assert.Equal(LayoutPackageManifest.CurrentPackageVersion, restored.Manifest.PackageVersion);
            Assert.Equal("Example", restored.Manifest.SourceDocumentName);
            Assert.Equal(assets[LayoutPackageManifest.LayoutAssetEntryName],
                restored.Assets[LayoutPackageManifest.LayoutAssetEntryName]);
            Assert.Equal(LayoutPackageArchive.Sha256(assets["display-modes/custom.ini"]),
                restored.Manifest.AssetChecksums["display-modes/custom.ini"]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void PackageOutputIsDeterministicForStableManifest()
    {
        var first = TemporaryPath();
        var second = TemporaryPath();
        try
        {
            var assets = new Dictionary<string, byte[]>
            {
                [LayoutPackageManifest.LayoutAssetEntryName] = [5, 4, 3, 2, 1],
            };
            LayoutPackageArchive.Write(first, Manifest(), assets);
            LayoutPackageArchive.Write(second, Manifest(), assets);

            Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
        }
        finally
        {
            if (File.Exists(first)) File.Delete(first);
            if (File.Exists(second)) File.Delete(second);
        }
    }

    [Fact]
    public void ModifiedAssetIsRejected()
    {
        var path = TemporaryPath();
        try
        {
            LayoutPackageArchive.Write(path, Manifest(), new Dictionary<string, byte[]>
            {
                [LayoutPackageManifest.LayoutAssetEntryName] = [1, 2, 3],
            });
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                var old = archive.GetEntry(LayoutPackageManifest.LayoutAssetEntryName)!;
                old.Delete();
                var replacement = archive.CreateEntry(LayoutPackageManifest.LayoutAssetEntryName);
                using var output = replacement.Open();
                output.Write([3, 2, 1]);
            }

            var exception = Assert.Throws<InvalidDataException>(() => LayoutPackageArchive.Read(path));
            Assert.Contains("checksum", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void UnsafeArchivePathIsRejectedBeforeExtraction()
    {
        var path = TemporaryPath();
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var manifest = archive.CreateEntry(LayoutPackageManifest.ManifestEntryName);
                using (var writer = new StreamWriter(manifest.Open()))
                    writer.Write(JsonSerializer.Serialize(Manifest()));
                archive.CreateEntry("../outside.txt");
            }

            Assert.Throws<InvalidDataException>(() => LayoutPackageArchive.Read(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void HistoricalPackageIsRejectedWithoutConversion()
    {
        var path = TemporaryPath();
        try
        {
            byte[] layout = [4, 3, 2, 1];
            var manifest = Manifest() with
            {
                PackageVersion = 1,
                FoundryState = DocumentState.Empty() with { SchemaVersion = 8 },
                AssetChecksums = new Dictionary<string, string>
                {
                    [LayoutPackageManifest.LayoutAssetEntryName] = LayoutPackageArchive.Sha256(layout),
                },
            };
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var manifestEntry = archive.CreateEntry(LayoutPackageManifest.ManifestEntryName);
                using (var writer = new StreamWriter(manifestEntry.Open()))
                    writer.Write(JsonSerializer.Serialize(manifest));
                var layoutEntry = archive.CreateEntry(LayoutPackageManifest.LayoutAssetEntryName);
                using var output = layoutEntry.Open();
                output.Write(layout);
            }

            Assert.Throws<NotSupportedException>(() => LayoutPackageArchive.Read(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void OrdinaryBlocksAndPageObjectsRoundTripWithoutManagedClassification()
    {
        var path = TemporaryPath();
        try
        {
            var pageId = Guid.NewGuid();
            var objectId = Guid.NewGuid();
            var definitionId = Guid.NewGuid();
            var manifest = Manifest() with
            {
                Sheets = [new(pageId, DocumentState.Empty().RootFolderId, 0, "Geometry", new(420, 297, "Millimeters"),
                    [], [objectId], new Dictionary<string, string>(), null, true)],
                BlockDefinitions = [new(definitionId, "Ordinary block", "fingerprint")],
            };
            LayoutPackageArchive.Write(path, manifest, new Dictionary<string, byte[]>
            { [LayoutPackageManifest.LayoutAssetEntryName] = [1, 2, 3] });
            var restored = LayoutPackageArchive.Read(path).Manifest;
            Assert.Null(Assert.Single(restored.Sheets).TitleBlock);
            Assert.Equal(objectId, Assert.Single(restored.Sheets[0].PageSpaceObjectIds));
            Assert.Equal(definitionId, Assert.Single(restored.BlockDefinitions).SourceId);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void WriterCannotRelabelAnIncompatibleManifest()
    {
        var path = TemporaryPath();
        try
        {
            Assert.Throws<NotSupportedException>(() => LayoutPackageArchive.Write(path,
                Manifest() with { PackageVersion = 5 }, new Dictionary<string, byte[]>()));
            Assert.False(File.Exists(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static LayoutPackageManifest Manifest() => new(
        LayoutPackageManifest.CurrentPackageVersion,
        "Example",
        new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero),
        "1.0.0",
        DocumentState.Empty(),
        [],
        [],
        [],
        [],
        new Dictionary<string, string>(StringComparer.Ordinal));

    private static string TemporaryPath() =>
        Path.Combine(Path.GetTempPath(), $"LayoutPackage-{Guid.NewGuid():N}.rlf");
}

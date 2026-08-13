using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseArtifactVerifierTests
{
    private const string Thumbprint = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string VendorThumbprint = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";
    private const string SourceRevision = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void Verify_PortableCliReturnsHashBoundSignatureProvenanceAndSbomEvidence()
    {
        using var fixture = new PortableFixture();

        PowerForgeReleaseArtifactEvidence result = fixture.CreateVerifier().Verify(fixture.CreateRequest());

        Assert.Equal(PowerForgeReleaseArtifactKind.PortableCli, result.ArtifactKind);
        Assert.Equal("Sample.CLI", result.ArtifactId);
        Assert.Equal("1.2.3", result.Version);
        Assert.Equal(SourceRevision, result.SourceRevision);
        Assert.Equal(Thumbprint, result.SignerThumbprint);
        Assert.Equal("valid", result.SignatureStatus);
        Assert.Equal(fixture.ComputeDigest(fixture.ArchivePath), result.Sha256);
        Assert.Contains(result.EvidenceFiles, item => item.Role == "provenance" && item.Path == fixture.ManifestPath);
        Assert.Contains(result.EvidenceFiles, item => item.Role == "configuration" && item.Path == fixture.ConfigurationPath);
        Assert.Contains(result.EvidenceFiles, item => item.Role == "sbom" && item.Path == fixture.SbomPath);
    }

    [Fact]
    public void Verify_PortableCliRejectsArchiveWhoseSignedPayloadDiffersFromChecksummedOutput()
    {
        using var fixture = new PortableFixture();
        fixture.WriteArchive("different signed payload");
        fixture.WriteChecksums();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("different bytes", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliRejectsChangedSbomEvenWhenArtifactStillMatches()
    {
        using var fixture = new PortableFixture();
        File.WriteAllText(fixture.SbomPath, "{\"bomFormat\":\"CycloneDX\",\"changed\":true}");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("SBOM SHA-256", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliRequiresManifestExecutableSignature()
    {
        using var fixture = new PortableFixture();
        string dependencyPath = fixture.AddSignedDependency();
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.SignaturePaths = new[] { dependencyPath };

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(request));

        Assert.Contains("manifest executable signature", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliAcceptsGlobalMatrixDimensionDefaults()
    {
        using var fixture = new PortableFixture();
        fixture.WriteConfigurationWithMatrixDefaults();

        PowerForgeReleaseArtifactEvidence result = fixture.CreateVerifier().Verify(fixture.CreateRequest());

        Assert.Equal("1.2.3", result.Version);
    }

    [Fact]
    public void Verify_PortableCliIncludesReferencedConfigurationInEvidence()
    {
        using var fixture = new PortableFixture();
        string referencedConfiguration = fixture.WriteReferencedConfiguration();

        PowerForgeReleaseArtifactEvidence result = fixture.CreateVerifier().Verify(fixture.CreateRequest());

        Assert.Contains(result.EvidenceFiles, item => item.Role == "configuration" && item.Path == fixture.ConfigurationPath);
        Assert.Contains(result.EvidenceFiles, item => item.Role == "configuration" && item.Path == referencedConfiguration);
    }

    [Fact]
    public void Verify_PortableCliRejectsLabelOnlyCycloneDxDocument()
    {
        using var fixture = new PortableFixture();
        fixture.WriteSbom("{\"bomFormat\":\"CycloneDX\"}");
        fixture.WriteChecksums();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("document-level fields", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliRejectsUnsupportedSpdxDocumentVersion()
    {
        using var fixture = new PortableFixture();
        fixture.WriteSbom(JsonSerializer.Serialize(new
        {
            spdxVersion = "SPDX-9.9",
            dataLicense = "CC0-1.0",
            SPDXID = "SPDXRef-DOCUMENT",
            name = "sample",
            documentNamespace = "https://example.invalid/spdx/sample",
            creationInfo = new { created = "2026-08-13T00:00:00Z", creators = new[] { "Tool: PowerForge" } }
        }));
        fixture.WriteChecksums();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("document-level fields", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private abstract class FixtureBase : IDisposable
    {
        protected FixtureBase()
        {
            Root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            ChecksumsPath = Path.Combine(Root, "SHA256SUMS.txt");
            SbomPath = Path.Combine(Root, "sample.cdx.json");
            File.WriteAllText(SbomPath, JsonSerializer.Serialize(new
            {
                bomFormat = "CycloneDX",
                specVersion = "1.6",
                serialNumber = "urn:uuid:00000000-0000-0000-0000-000000000001",
                version = 1,
                components = Array.Empty<object>()
            }));
        }

        internal string Root { get; }
        internal string ChecksumsPath { get; }
        internal string SbomPath { get; }

        internal string ComputeDigest(string path)
        {
            using FileStream input = File.OpenRead(path);
            using SHA256 hash = SHA256.Create();
            return Convert.ToHexString(hash.ComputeHash(input));
        }

        internal void WriteChecksums(params string[] paths)
        {
            string[] allPaths = paths.Concat(new[] { SbomPath }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            File.WriteAllLines(ChecksumsPath, allPaths.Select(path =>
                $"{ComputeDigest(path)} *{Path.GetRelativePath(Root, path).Replace('\\', '/')}"));
        }

        internal void WriteSbom(string content) => File.WriteAllText(SbomPath, content);

        internal PowerForgeReleaseArtifactVerifier CreateVerifier(string signerThumbprint = Thumbprint) =>
            new(
                path => new DotNetPublishReleaseArtifactVerifier.AuthenticodeResult(
                    true,
                    0,
                    "CN=Publisher",
                    signerThumbprint),
                _ => "1.2.3.0");

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class PortableFixture : FixtureBase
    {
        internal PortableFixture()
        {
            OutputDirectory = Path.Combine(Root, "Artifacts", "Sample.CLI", "win-x64", "net10.0", "PortableCompat");
            Directory.CreateDirectory(OutputDirectory);
            ExecutablePath = Path.Combine(OutputDirectory, "Sample.CLI.exe");
            File.WriteAllText(ExecutablePath, "signed payload");
            ArchivePath = Path.Combine(Path.GetDirectoryName(OutputDirectory)!, "Sample.CLI.zip");
            ManifestPath = Path.Combine(Root, "manifest.json");
            ConfigurationPath = Path.Combine(Root, "powerforge.dotnetpublish.json");
            WriteArchive("signed payload");
            File.WriteAllText(ConfigurationPath, JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                DotNet = new { AllowOutputOutsideProjectRoot = false },
                Targets = new[]
                {
                    new
                    {
                        Name = "Sample.CLI",
                        Kind = "Cli",
                        Publish = new
                        {
                            Framework = "net10.0",
                            Runtimes = new[] { "win-x64" },
                            Style = "PortableCompat",
                            Sign = new { Enabled = true, Thumbprint }
                        }
                    }
                }
            }));
            File.WriteAllText(ManifestPath, JsonSerializer.Serialize(new[]
            {
                new
                {
                    Category = "Publish",
                    Target = "Sample.CLI",
                    Runtime = "win-x64",
                    Framework = "net10.0",
                    Style = "PortableCompat",
                    OutputDir = OutputDirectory,
                    ZipPath = ArchivePath,
                    ExePath = ExecutablePath,
                    SignedFiles = 1,
                    SourceRevision,
                    SourceDirty = false
                }
            }));
            WriteChecksums();
        }

        internal string OutputDirectory { get; }
        internal string ExecutablePath { get; }
        internal string ArchivePath { get; }
        internal string ManifestPath { get; }
        internal string ConfigurationPath { get; }

        internal PowerForgeReleaseArtifactVerificationRequest CreateRequest() => new()
        {
            Kind = PowerForgeReleaseArtifactKind.PortableCli,
            ArtifactId = "Sample.CLI",
            ProjectRoot = Root,
            ArtifactPath = ArchivePath,
            ChecksumsPath = ChecksumsPath,
            ManifestPath = ManifestPath,
            ConfigurationPath = ConfigurationPath,
            ExpectedSourceRevision = SourceRevision,
            Runtime = "win-x64",
            Framework = "net10.0",
            Style = "PortableCompat",
            SignaturePaths = new[] { ExecutablePath },
            SbomPaths = new[] { SbomPath }
        };

        internal void WriteArchive(string payload)
        {
            if (File.Exists(ArchivePath)) File.Delete(ArchivePath);
            using ZipArchive archive = ZipFile.Open(ArchivePath, ZipArchiveMode.Create);
            ZipArchiveEntry entry = archive.CreateEntry("Sample.CLI.exe");
            using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
            writer.Write(payload);
        }

        internal void WriteChecksums() =>
            base.WriteChecksums(ManifestPath, ExecutablePath, ArchivePath);

        internal string AddSignedDependency()
        {
            string dependencyPath = Path.Combine(OutputDirectory, "Dependency.dll");
            File.WriteAllText(dependencyPath, "signed dependency");
            using (ZipArchive archive = ZipFile.Open(ArchivePath, ZipArchiveMode.Update))
            {
                ZipArchiveEntry entry = archive.CreateEntry("Dependency.dll");
                using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
                writer.Write("signed dependency");
            }
            base.WriteChecksums(ManifestPath, ExecutablePath, ArchivePath, dependencyPath);
            return dependencyPath;
        }

        internal void WriteConfigurationWithMatrixDefaults()
        {
            File.WriteAllText(ConfigurationPath, JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                DotNet = new
                {
                    AllowOutputOutsideProjectRoot = false,
                    Runtimes = new[] { "win-x64" }
                },
                Matrix = new
                {
                    Frameworks = new[] { "net10.0" },
                    Styles = new[] { "PortableCompat" }
                },
                Targets = new[]
                {
                    new
                    {
                        Name = "Sample.CLI",
                        Kind = "Cli",
                        Publish = new
                        {
                            Sign = new { Enabled = true, Thumbprint }
                        }
                    }
                }
            }));
        }

        internal string WriteReferencedConfiguration()
        {
            string referencedPath = Path.Combine(Root, "referenced.dotnetpublish.json");
            File.Copy(ConfigurationPath, referencedPath);
            File.WriteAllText(ConfigurationPath, JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                Tools = new
                {
                    DotNetPublishConfigPath = Path.GetFileName(referencedPath)
                }
            }));
            return referencedPath;
        }
    }

}

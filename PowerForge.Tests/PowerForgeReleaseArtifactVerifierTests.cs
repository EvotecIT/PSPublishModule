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
        Assert.Contains(result.EvidenceFiles, item => item.Role == "manifest" && item.Path == fixture.ManifestPath);
        Assert.DoesNotContain(result.EvidenceFiles, item => item.Role == "provenance");
        Assert.Contains(result.EvidenceFiles, item => item.Role == "configuration" && item.Path == fixture.ConfigurationPath);
        Assert.Contains(result.EvidenceFiles, item => item.Role == "sbom" && item.Path == fixture.SbomPath);
        Assert.Contains(result.EvidenceFiles, item => item.Role == "sbom-signature" && item.Path == fixture.SbomSignaturePath);
    }

    [Fact]
    public void Verify_PortableCliSelectsAndVerifiesSignedBundlePayload()
    {
        using var fixture = new PortableFixture();
        fixture.ConfigureBundle("package");
        Directory.Delete(fixture.OutputDirectory, recursive: true);
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.BundleId = "package";

        PowerForgeReleaseArtifactEvidence result = fixture.CreateVerifier().Verify(request);

        Assert.Equal("Sample.CLI", result.ArtifactId);
        request.BundleId = null;
        InvalidDataException missingSelector = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(request));
        Assert.Contains("publish entry", missingSelector.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliRejectsBundleAbsentFromConfiguration()
    {
        using var fixture = new PortableFixture();
        fixture.ConfigureBundle("package", configureBundle: false);
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.BundleId = "package";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(request));

        Assert.Contains("must define exactly one bundle", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliRejectsBundleOutsideConfiguredMatrixSelectors()
    {
        using var fixture = new PortableFixture();
        fixture.ConfigureBundle("package", bundleRuntime: "linux-x64");
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.BundleId = "package";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(request));

        Assert.Contains("bundle selectors", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliRejectsBundlePackagingThatDiffersFromConfiguration()
    {
        using var fixture = new PortableFixture();
        fixture.ConfigureBundle("package", bundleZip: false);
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.BundleId = "package";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(request));

        Assert.Contains("ZIP policy", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliRejectsArtifactPackagingThatDiffersFromConfiguration()
    {
        using var fixture = new PortableFixture();
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.ArtifactPath = fixture.ExecutablePath;
        request.SignaturePaths = Array.Empty<string>();
        request.SbomPaths = Array.Empty<string>();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(request));

        Assert.Contains("ZIP policy", exception.Message, StringComparison.OrdinalIgnoreCase);
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
    public void Verify_PortableCliRejectsSignedInventoryFromDifferentMatrixCombination()
    {
        using var fixture = new PortableFixture();
        fixture.SetInventoryDimensions(runtime: "linux-x64", framework: "net8.0", style: "Portable");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("payload dimensions", exception.Message, StringComparison.OrdinalIgnoreCase);
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
    public void Verify_PortableCliRejectsConfigurationChangedAfterChecksumCatalogWasWritten()
    {
        using var fixture = new PortableFixture();
        File.AppendAllText(fixture.ConfigurationPath, " ");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("configuration SHA-256", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliRejectsReferencedConfigurationChangedAfterChecksumCatalogWasWritten()
    {
        using var fixture = new PortableFixture();
        string referenced = fixture.WriteReferencedConfiguration();
        File.AppendAllText(referenced, " ");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("configuration SHA-256", exception.Message, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void Verify_PortableCliRejectsSbomForDifferentArtifact()
    {
        using var fixture = new PortableFixture();
        fixture.WriteBoundCycloneDxSbom("Other.CLI", "1.2.3", fixture.ComputeDigest(fixture.ArchivePath));
        fixture.WriteChecksums();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("does not bind", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliAcceptsSpdxArtifactBinding()
    {
        using var fixture = new PortableFixture();
        fixture.WriteSpdxSbom("Sample.CLI", "1.2.3", fixture.ComputeDigest(fixture.ArchivePath));
        fixture.WriteChecksums();

        PowerForgeReleaseArtifactEvidence result = fixture.CreateVerifier().Verify(fixture.CreateRequest());

        Assert.Contains(result.EvidenceFiles, evidence => evidence.Role == "sbom");
    }

    [Fact]
    public void Verify_PortableCliRejectsReplacedSbomAndCatalogSignedByDifferentPublisher()
    {
        using var fixture = new PortableFixture();
        fixture.WriteBoundCycloneDxSbom("Sample.CLI", "1.2.3", fixture.ComputeDigest(fixture.ArchivePath));
        fixture.WriteChecksums();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier(sbomSignerThumbprint: VendorThumbprint).Verify(fixture.CreateRequest()));

        Assert.Contains("admitted artifact publisher", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Verify_PortableCliRejectsExplicitNonCliKind(bool configuredKind)
    {
        using var fixture = new PortableFixture();
        if (configuredKind)
            fixture.WriteConfigurationKind("Service");
        else
            fixture.WriteManifestKind("Service");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("not a CLI release target", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private abstract class FixtureBase : IDisposable
    {
        protected FixtureBase()
        {
            Root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            ChecksumsPath = Path.Combine(Root, "SHA256SUMS.txt");
            SbomPath = Path.Combine(Root, "sample.cdx.json");
            SbomSignaturePath = SbomPath + ".p7s";
            File.WriteAllText(SbomPath, "{}");
            File.WriteAllBytes(SbomSignaturePath, new byte[] { 2 });
        }

        internal string Root { get; }
        internal string ChecksumsPath { get; }
        internal string SbomPath { get; }
        internal string SbomSignaturePath { get; }

        internal string ComputeDigest(string path)
        {
            using FileStream input = File.OpenRead(path);
            using SHA256 hash = SHA256.Create();
            return Convert.ToHexString(hash.ComputeHash(input));
        }

        internal void WriteChecksums(params string[] paths)
        {
            string[] allPaths = paths
                .Concat(new[] { SbomPath, SbomSignaturePath })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            File.WriteAllLines(ChecksumsPath, allPaths.Select(path =>
                $"{ComputeDigest(path)} *{Path.GetRelativePath(Root, path).Replace('\\', '/')}"));
        }

        internal void WriteSbom(string content) => File.WriteAllText(SbomPath, content);

        internal void WriteBoundCycloneDxSbom(string artifactId, string version, string digest) =>
            WriteSbom(JsonSerializer.Serialize(new
            {
                bomFormat = "CycloneDX",
                specVersion = "1.6",
                serialNumber = "urn:uuid:00000000-0000-0000-0000-000000000001",
                version = 1,
                metadata = new
                {
                    component = new
                    {
                        name = artifactId,
                        version,
                        hashes = new[] { new { alg = "SHA-256", content = digest } }
                    }
                }
            }));

        internal void WriteSpdxSbom(string artifactId, string version, string digest) =>
            WriteSbom(JsonSerializer.Serialize(new
            {
                spdxVersion = "SPDX-2.3",
                dataLicense = "CC0-1.0",
                SPDXID = "SPDXRef-DOCUMENT",
                name = artifactId + " SBOM",
                documentNamespace = "https://example.invalid/spdx/" + Guid.NewGuid().ToString("N"),
                creationInfo = new { created = "2026-08-13T00:00:00Z", creators = new[] { "Tool: PowerForge" } },
                packages = new[]
                {
                    new
                    {
                        name = artifactId,
                        versionInfo = version,
                        checksums = new[] { new { algorithm = "SHA256", checksumValue = digest } }
                    }
                }
            }));

        internal PowerForgeReleaseArtifactVerifier CreateVerifier(
            string signerThumbprint = Thumbprint,
            string? inventorySignerThumbprint = null,
            string? sbomSignerThumbprint = null) =>
            new(
                path => new DotNetPublishReleaseArtifactVerifier.AuthenticodeResult(
                    true,
                    0,
                    "CN=Publisher",
                    signerThumbprint),
                _ => "1.2.3+" + SourceRevision,
                _ => "Sample.CLI",
                (_, signature) => new PowerForgePayloadInventorySignature(
                    "CN=Publisher",
                    signature.Length > 0 && signature[0] == 2
                        ? sbomSignerThumbprint ?? signerThumbprint
                        : inventorySignerThumbprint ?? signerThumbprint));

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed partial class PortableFixture : FixtureBase
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
            ProjectPath = Path.Combine(Root, "Sample.CLI.csproj");
            File.WriteAllText(ProjectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            WriteArchive("signed payload");
            WriteDirectInventory();
            File.WriteAllText(ConfigurationPath, JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                DotNet = new { AllowOutputOutsideProjectRoot = false },
                Targets = new[]
                {
                    new
                    {
                        Name = "Sample.CLI",
                        ProjectPath = Path.GetFileName(ProjectPath),
                        Kind = "Cli",
                        Publish = new
                        {
                            Framework = "net10.0",
                            Runtimes = new[] { "win-x64" },
                            Style = "PortableCompat",
                            Zip = true,
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
                    Kind = "Cli",
                    Runtime = "win-x64",
                    Framework = "net10.0",
                    Style = "PortableCompat",
                    OutputDir = OutputDirectory,
                    ZipPath = ArchivePath,
                    ExePath = ExecutablePath,
                    SignedFiles = 1,
                    SignedFilePaths = new[] { ExecutablePath },
                    SourceRevision,
                    SourceDirty = false
                }
            }));
            WriteBoundCycloneDxSbom("Sample.CLI", "1.2.3", ComputeDigest(ArchivePath));
            WriteChecksums();
        }

        internal string OutputDirectory { get; }
        internal string ExecutablePath { get; }
        internal string ArchivePath { get; }
        internal string ManifestPath { get; }
        internal string ConfigurationPath { get; }
        internal string ProjectPath { get; }
        internal string DirectInventoryPath =>
            ExecutablePath + PowerForgePortablePayloadInventory.DirectInventorySuffix;
        internal string DirectSignaturePath =>
            ExecutablePath + PowerForgePortablePayloadInventory.DirectSignatureSuffix;

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
            SignThumbprint = Thumbprint,
            Runtime = "win-x64",
            Framework = "net10.0",
            Style = "PortableCompat",
            SignaturePaths = new[] { ExecutablePath },
            SbomPaths = new[] { SbomPath }
        };

        internal void WriteArchive(string payload)
        {
            File.WriteAllText(ExecutablePath, payload);
            WritePortableInventory(new[] { ExecutablePath });
            WriteArchiveFromOutput();
        }

        private void WritePortableInventory(
            IEnumerable<string> signedPaths,
            string version = "1.2.3+" + SourceRevision,
            string? bundleId = null)
        {
            PowerForgePortablePayloadInventory inventory = PowerForgePortablePayloadInventoryCms.Create(
                OutputDirectory,
                "Sample.CLI",
                "win-x64",
                "net10.0",
                "PortableCompat",
                SourceRevision,
                ExecutablePath,
                "Sample.CLI",
                version,
                signedPaths,
                bundleId);
            File.WriteAllBytes(
                Path.Combine(OutputDirectory, PowerForgePortablePayloadInventory.InventoryFileName),
                PowerForgePortablePayloadInventoryCms.Serialize(inventory));
            File.WriteAllBytes(
                Path.Combine(OutputDirectory, PowerForgePortablePayloadInventory.SignatureFileName),
                new byte[] { 1 });
        }

        private void WriteDirectInventory(
            string runtime = "win-x64",
            string framework = "net10.0",
            string style = "PortableCompat",
            bool sourceDirty = false)
        {
            PowerForgePortablePayloadInventory inventory = PowerForgePortablePayloadInventoryCms.Create(
                OutputDirectory,
                "Sample.CLI",
                runtime,
                framework,
                style,
                SourceRevision,
                ExecutablePath,
                "Sample.CLI",
                "1.2.3+" + SourceRevision,
                new[] { ExecutablePath },
                sourceDirty: sourceDirty,
                includeCompleteOutput: false);
            File.WriteAllBytes(DirectInventoryPath, PowerForgePortablePayloadInventoryCms.Serialize(inventory));
            File.WriteAllBytes(DirectSignaturePath, new byte[] { 1 });
        }

        private void WriteArchiveFromOutput()
        {
            if (File.Exists(ArchivePath)) File.Delete(ArchivePath);
            using ZipArchive archive = ZipFile.Open(ArchivePath, ZipArchiveMode.Create);
            foreach (string path in Directory.EnumerateFiles(OutputDirectory, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(OutputDirectory, path).Replace('\\', '/');
                archive.CreateEntryFromFile(path, relative);
            }
        }

        internal void WriteChecksums() =>
            base.WriteChecksums(
                ManifestPath,
                ConfigurationPath,
                ExecutablePath,
                ArchivePath,
                DirectInventoryPath,
                DirectSignaturePath);

        internal string AddSignedDependency()
        {
            string dependencyPath = Path.Combine(OutputDirectory, "Dependency.dll");
            File.WriteAllText(dependencyPath, "signed dependency");
            WritePortableInventory(new[] { ExecutablePath });
            WriteArchiveFromOutput();
            WriteBoundCycloneDxSbom("Sample.CLI", "1.2.3", ComputeDigest(ArchivePath));
            base.WriteChecksums(
                ManifestPath,
                ConfigurationPath,
                ExecutablePath,
                ArchivePath,
                DirectInventoryPath,
                DirectSignaturePath,
                dependencyPath);
            return dependencyPath;
        }

        internal void EnableDllSigning(
            string dependencyPath,
            int signedFileCount = 2,
            bool omitDependencyFromManifest = false,
            bool zip = true)
        {
            WritePortableInventory(omitDependencyFromManifest
                ? new[] { ExecutablePath }
                : new[] { ExecutablePath, dependencyPath });
            WriteArchiveFromOutput();
            File.WriteAllText(ConfigurationPath, JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                DotNet = new { AllowOutputOutsideProjectRoot = false },
                Targets = new[]
                {
                    new
                    {
                        Name = "Sample.CLI",
                        ProjectPath = Path.GetFileName(ProjectPath),
                        Kind = "Cli",
                        Publish = new
                        {
                            Framework = "net10.0",
                            Runtimes = new[] { "win-x64" },
                            Style = "PortableCompat",
                            Zip = zip,
                            Sign = new { Enabled = true, IncludeDlls = true, Thumbprint }
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
                    Kind = "Cli",
                    Runtime = "win-x64",
                    Framework = "net10.0",
                    Style = "PortableCompat",
                    OutputDir = OutputDirectory,
                    ZipPath = ArchivePath,
                    ExePath = ExecutablePath,
                    SignedFiles = signedFileCount,
                    SignedFilePaths = omitDependencyFromManifest
                        ? new[] { ExecutablePath }
                        : new[] { ExecutablePath, dependencyPath },
                    SourceRevision,
                    SourceDirty = false
                }
            }));
            WriteBoundCycloneDxSbom("Sample.CLI", "1.2.3", ComputeDigest(ArchivePath));
            base.WriteChecksums(
                ManifestPath,
                ConfigurationPath,
                ExecutablePath,
                ArchivePath,
                DirectInventoryPath,
                DirectSignaturePath,
                dependencyPath);
        }

        internal void SetPortableVersion(string version)
        {
            WritePortableInventory(new[] { ExecutablePath }, version);
            WriteArchiveFromOutput();
        }

        internal void SetInventoryDimensions(string runtime, string framework, string style)
        {
            WritePortableInventory(new[] { ExecutablePath });
            string inventoryPath = Path.Combine(OutputDirectory, PowerForgePortablePayloadInventory.InventoryFileName);
            PowerForgePortablePayloadInventory inventory = JsonSerializer.Deserialize<PowerForgePortablePayloadInventory>(
                File.ReadAllBytes(inventoryPath))!;
            inventory.Runtime = runtime;
            inventory.Framework = framework;
            inventory.Style = style;
            File.WriteAllBytes(inventoryPath, PowerForgePortablePayloadInventoryCms.Serialize(inventory));
            WriteArchiveFromOutput();
            WriteBoundCycloneDxSbom("Sample.CLI", "1.2.3", ComputeDigest(ArchivePath));
            WriteChecksums();
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
                        ProjectPath = Path.GetFileName(ProjectPath),
                        Kind = "Cli",
                        Publish = new
                        {
                            Zip = true,
                            Sign = new { Enabled = true, Thumbprint }
                        }
                    }
                }
            }));
            WriteChecksums();
        }

        internal void ConfigureTargetAlias(string alias)
        {
            File.WriteAllText(ConfigurationPath, JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                DotNet = new { AllowOutputOutsideProjectRoot = false },
                Targets = new[]
                {
                    new
                    {
                        Name = alias,
                        ProjectPath = Path.GetFileName(ProjectPath),
                        Kind = "Cli",
                        Publish = new
                        {
                            Framework = "net10.0",
                            Runtimes = new[] { "win-x64" },
                            Style = "PortableCompat",
                            Zip = true,
                            RenameTo = alias + ".exe",
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
                    Target = alias,
                    Kind = "Cli",
                    Runtime = "win-x64",
                    Framework = "net10.0",
                    Style = "PortableCompat",
                    OutputDir = OutputDirectory,
                    ZipPath = ArchivePath,
                    ExePath = ExecutablePath,
                    SignedFiles = 1,
                    SignedFilePaths = new[] { ExecutablePath },
                    SourceRevision,
                    SourceDirty = false
                }
            }));
            WritePortableInventory(new[] { ExecutablePath });
            string inventoryPath = Path.Combine(OutputDirectory, PowerForgePortablePayloadInventory.InventoryFileName);
            PowerForgePortablePayloadInventory inventory = JsonSerializer.Deserialize<PowerForgePortablePayloadInventory>(
                File.ReadAllBytes(inventoryPath))!;
            inventory.ArtifactId = alias;
            inventory.Target = alias;
            File.WriteAllBytes(inventoryPath, PowerForgePortablePayloadInventoryCms.Serialize(inventory));
            WriteArchiveFromOutput();
            WriteBoundCycloneDxSbom(alias, "1.2.3", ComputeDigest(ArchivePath));
            base.WriteChecksums(ManifestPath, ConfigurationPath, ProjectPath, ExecutablePath, ArchivePath);
        }

        internal void ConfigureBundle(
            string bundleId,
            bool configureBundle = true,
            string bundleRuntime = "win-x64",
            bool bundleZip = true)
        {
            WritePortableInventory(new[] { ExecutablePath }, bundleId: bundleId);
            WriteArchiveFromOutput();
            File.WriteAllText(ManifestPath, JsonSerializer.Serialize(new[]
            {
                new
                {
                    Category = "Bundle",
                    BundleId = bundleId,
                    Target = "Sample.CLI",
                    Kind = "Cli",
                    Runtime = "win-x64",
                    Framework = "net10.0",
                    Style = "PortableCompat",
                    OutputDir = OutputDirectory,
                    ZipPath = ArchivePath,
                    ExePath = ExecutablePath,
                    SignedFiles = 1,
                    SignedFilePaths = new[] { ExecutablePath },
                    SourceRevision,
                    SourceDirty = false
                }
            }));
            if (configureBundle)
            {
                File.WriteAllText(ConfigurationPath, JsonSerializer.Serialize(new
                {
                    SchemaVersion = 1,
                    DotNet = new { AllowOutputOutsideProjectRoot = false },
                    Targets = new[]
                    {
                        new
                        {
                            Name = "Sample.CLI",
                            ProjectPath = Path.GetFileName(ProjectPath),
                            Kind = "Cli",
                            Publish = new
                            {
                                Framework = "net10.0",
                                Runtimes = new[] { "win-x64" },
                                Style = "PortableCompat",
                                Zip = false,
                                Sign = new { Enabled = true, Thumbprint }
                            }
                        }
                    },
                    Bundles = new[]
                    {
                        new
                        {
                            Id = bundleId,
                            PrepareFromTarget = "Sample.CLI",
                            Runtimes = new[] { bundleRuntime },
                            Frameworks = new[] { "net10.0" },
                            Styles = new[] { "PortableCompat" },
                            Zip = bundleZip
                        }
                    }
                }));
            }
            WriteBoundCycloneDxSbom("Sample.CLI", "1.2.3", ComputeDigest(ArchivePath));
            base.WriteChecksums(ManifestPath, ConfigurationPath, ExecutablePath, ArchivePath);
        }

        internal void ConfigureSubjectNameSigning()
        {
            File.WriteAllText(ConfigurationPath, JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                DotNet = new { AllowOutputOutsideProjectRoot = false },
                Targets = new[]
                {
                    new
                    {
                        Name = "Sample.CLI",
                        ProjectPath = Path.GetFileName(ProjectPath),
                        Kind = "Cli",
                        Publish = new
                        {
                            Framework = "net10.0",
                            Runtimes = new[] { "win-x64" },
                            Style = "PortableCompat",
                            Zip = true,
                            Sign = new { Enabled = true, SubjectName = "Publisher" }
                        }
                    }
                }
            }));
            WriteChecksums();
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
            base.WriteChecksums(ManifestPath, ConfigurationPath, referencedPath, ExecutablePath, ArchivePath);
            return referencedPath;
        }

        internal void WriteConfigurationKind(string kind)
        {
            File.WriteAllText(ConfigurationPath, JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                DotNet = new { AllowOutputOutsideProjectRoot = false },
                Targets = new[]
                {
                    new
                    {
                        Name = "Sample.CLI",
                        ProjectPath = Path.GetFileName(ProjectPath),
                        Kind = kind,
                        Publish = new
                        {
                            Framework = "net10.0",
                            Runtimes = new[] { "win-x64" },
                            Style = "PortableCompat",
                            Zip = true,
                            Sign = new { Enabled = true, Thumbprint }
                        }
                    }
                }
            }));
            WriteChecksums();
        }

        internal void WriteManifestKind(string kind)
        {
            File.WriteAllText(ManifestPath, JsonSerializer.Serialize(new[]
            {
                new
                {
                    Category = "Publish",
                    Target = "Sample.CLI",
                    Kind = kind,
                    Runtime = "win-x64",
                    Framework = "net10.0",
                    Style = "PortableCompat",
                    OutputDir = OutputDirectory,
                    ZipPath = ArchivePath,
                    ExePath = ExecutablePath,
                    SignedFiles = 1,
                    SignedFilePaths = new[] { ExecutablePath },
                    SourceRevision,
                    SourceDirty = false
                }
            }));
            WriteChecksums();
        }
    }

}

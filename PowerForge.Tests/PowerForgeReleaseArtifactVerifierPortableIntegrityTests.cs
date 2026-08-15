using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseArtifactVerifierTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PortableInventory_RejectsApplicationPayloadAtReservedEvidencePath(bool archivePayload)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string executable = Path.Combine(root, "Sample.CLI.exe");
            File.WriteAllText(executable, "signed payload");
            (string inventoryPath, string signaturePath) = PowerForgePortablePayloadInventoryCms.ResolveEvidencePaths(
                root,
                executable,
                archivePayload);
            File.WriteAllText(inventoryPath, "application-owned payload");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                PowerForgePortablePayloadInventoryCms.EnsureEvidencePathsAvailable(inventoryPath, signaturePath));

            Assert.Contains("reserved release-inventory", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("application-owned payload", File.ReadAllText(inventoryPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, 68157440L)]
    [InlineData(true, 5242880L)]
    public void Verify_PortableCliRejectsOversizedSbomEvidenceBeforeBuffering(bool signature, long bytes)
    {
        using var fixture = new PortableFixture();
        string path = signature ? fixture.SbomSignaturePath : fixture.SbomPath;
        using (FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(bytes);
            stream.Position = 0;
            stream.WriteByte(2);
        }
        fixture.WriteChecksums();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("byte limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliRejectsOversizedManifestBeforeParsing()
    {
        using var fixture = new PortableFixture();
        using (FileStream stream = new(fixture.ManifestPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.SetLength((16L * 1024L * 1024L) + 1L);
            stream.Position = 0;
            stream.WriteByte((byte)'[');
        }
        fixture.WriteChecksums();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("PowerForge manifest exceeds", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("byte limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliRejectsOversizedConfigurationBeforeParsing()
    {
        using var fixture = new PortableFixture();
        using (FileStream stream = new(fixture.ConfigurationPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(DotNetPublishReleaseArtifactVerifier.MaxConfigurationBytes + 1L);
            stream.Position = 0;
            stream.WriteByte((byte)'{');
        }
        fixture.WriteChecksums();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("configuration exceeds", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("byte limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateArchiveEntries_RejectsEntryCountBeforeIndexing()
    {
        using var stream = new MemoryStream();
        using (var writer = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            writer.CreateEntry("one.txt");
            writer.CreateEntry("two.txt");
        }
        stream.Position = 0;
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            PowerForgeReleaseArtifactVerifier.ValidateArchiveEntries(archive, maximumEntries: 1));

        Assert.Contains("entry limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliStreamsLargeArchiveMember()
    {
        using var fixture = new PortableFixture();
        fixture.WriteLargePayload(12 * 1024 * 1024);

        PowerForgeReleaseArtifactEvidence evidence = fixture.CreateVerifier().Verify(fixture.CreateRequest());

        Assert.Equal("valid", evidence.SignatureStatus);
    }

    [Fact]
    public void Verify_PortableCliRejectsArchivePayloadTamperedAfterPublisherInventoryWasSigned()
    {
        using var fixture = new PortableFixture();
        fixture.TamperArchiveEntry("Sample.CLI.exe", "different signed payload");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("publisher-signed payload inventory", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliRejectsDirtySourceStateBoundByPublisherInventory()
    {
        using var fixture = new PortableFixture();
        fixture.SetArchiveInventorySourceDirty();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("dirty source checkout", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliRejectsExtraArchiveEntryOutsideTrustedOutputInventory()
    {
        using var fixture = new PortableFixture();
        fixture.AddUnexpectedArchiveEntry("Injected.dll", "untrusted payload");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("exactly match", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inventory", exception.Message, StringComparison.OrdinalIgnoreCase);
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
    public void Verify_PortableCliArchiveRemainsVerifiableAfterFreshDownloadWithoutProducerOutputDirectory()
    {
        using var fixture = new PortableFixture();
        string downloadRoot = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            string[] releaseAssets =
            {
                fixture.ArchivePath,
                fixture.ChecksumsPath,
                fixture.ManifestPath,
                fixture.ConfigurationPath,
                fixture.SbomPath,
                fixture.SbomSignaturePath
            };
            foreach (string source in releaseAssets)
            {
                string destination = Path.Combine(downloadRoot, Path.GetRelativePath(fixture.Root, source));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination);
            }

            PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
            request.ProjectRoot = downloadRoot;
            request.ArtifactPath = Path.Combine(downloadRoot, Path.GetRelativePath(fixture.Root, fixture.ArchivePath));
            request.ChecksumsPath = Path.Combine(downloadRoot, Path.GetRelativePath(fixture.Root, fixture.ChecksumsPath));
            request.ManifestPath = Path.Combine(downloadRoot, Path.GetRelativePath(fixture.Root, fixture.ManifestPath));
            request.ConfigurationPath = Path.Combine(downloadRoot, Path.GetRelativePath(fixture.Root, fixture.ConfigurationPath));
            request.SignaturePaths = Array.Empty<string>();
            request.SbomPaths = new[] { Path.Combine(downloadRoot, Path.GetRelativePath(fixture.Root, fixture.SbomPath)) };

            PowerForgeReleaseArtifactEvidence evidence = fixture.CreateVerifier().Verify(request);

            Assert.Equal("valid", evidence.SignatureStatus);
            Assert.False(File.Exists(Path.Combine(downloadRoot, Path.GetFileName(fixture.ProjectPath))));
        }
        finally
        {
            if (Directory.Exists(downloadRoot)) Directory.Delete(downloadRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Verify_PortableCliDirectExecutableRemainsVerifiableAfterFreshDownload(bool configureExplicitIdentity)
    {
        using var fixture = new PortableFixture();
        fixture.ConfigureDirectPackaging();
        if (configureExplicitIdentity)
            fixture.ConfigureExplicitExecutableIdentity();
        string downloadRoot = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            string[] releaseAssets =
            {
                fixture.ExecutablePath,
                fixture.DirectInventoryPath,
                fixture.DirectSignaturePath,
                fixture.ChecksumsPath,
                fixture.ManifestPath,
                fixture.ConfigurationPath
            };
            foreach (string source in releaseAssets)
            {
                string destination = Path.Combine(downloadRoot, Path.GetRelativePath(fixture.Root, source));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination);
            }

            PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
            request.ProjectRoot = downloadRoot;
            request.ArtifactPath = Path.Combine(downloadRoot, Path.GetRelativePath(fixture.Root, fixture.ExecutablePath));
            request.ChecksumsPath = Path.Combine(downloadRoot, Path.GetRelativePath(fixture.Root, fixture.ChecksumsPath));
            request.ManifestPath = Path.Combine(downloadRoot, Path.GetRelativePath(fixture.Root, fixture.ManifestPath));
            request.ConfigurationPath = Path.Combine(downloadRoot, Path.GetRelativePath(fixture.Root, fixture.ConfigurationPath));
            request.SignaturePaths = Array.Empty<string>();
            request.SbomPaths = Array.Empty<string>();

            PowerForgeReleaseArtifactEvidence evidence = fixture.CreateVerifier().Verify(request);

            Assert.Equal("valid", evidence.SignatureStatus);
            Assert.Equal(Path.GetFileName(fixture.ExecutablePath), evidence.FileName);
        }
        finally
        {
            if (Directory.Exists(downloadRoot)) Directory.Delete(downloadRoot, recursive: true);
        }
    }

    [Fact]
    public void Verify_PortableCliAcceptsDimensionQualifiedDirectReleaseAsset()
    {
        using var fixture = new PortableFixture();
        fixture.ConfigureExplicitExecutableIdentity();
        string aliasName = DotNetPublishReleaseAssetNaming.CreateDirectMatrixAssetName(
            "Sample.CLI",
            "net10.0",
            "win-x64",
            DotNetPublishStyle.PortableCompat.ToString(),
            DotNetPublishArtefactCategory.Publish,
            bundleId: null,
            fixture.ExecutablePath);
        string aliasPath = Path.Combine(fixture.Root, aliasName);
        File.Copy(fixture.ExecutablePath, aliasPath);
        File.Copy(
            fixture.DirectInventoryPath,
            aliasPath + PowerForgePortablePayloadInventory.DirectInventorySuffix);
        File.Copy(
            fixture.DirectSignaturePath,
            aliasPath + PowerForgePortablePayloadInventory.DirectSignatureSuffix);
        File.AppendAllLines(
            fixture.ChecksumsPath,
            new[]
            {
                $"{fixture.ComputeDigest(aliasPath)} *{aliasName}",
                $"{fixture.ComputeDigest(aliasPath + PowerForgePortablePayloadInventory.DirectInventorySuffix)} *{aliasName}{PowerForgePortablePayloadInventory.DirectInventorySuffix}",
                $"{fixture.ComputeDigest(aliasPath + PowerForgePortablePayloadInventory.DirectSignatureSuffix)} *{aliasName}{PowerForgePortablePayloadInventory.DirectSignatureSuffix}"
            });
        string downloadRoot = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(downloadRoot);
            foreach (string source in new[]
                     {
                         aliasPath,
                         aliasPath + PowerForgePortablePayloadInventory.DirectInventorySuffix,
                         aliasPath + PowerForgePortablePayloadInventory.DirectSignatureSuffix,
                         fixture.ChecksumsPath,
                         fixture.ManifestPath,
                         fixture.ConfigurationPath
                     })
            {
                File.Copy(source, Path.Combine(downloadRoot, Path.GetFileName(source)));
            }

            PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
            request.ProjectRoot = downloadRoot;
            request.ArtifactPath = Path.Combine(downloadRoot, aliasName);
            request.ChecksumsPath = Path.Combine(downloadRoot, Path.GetFileName(fixture.ChecksumsPath));
            request.ManifestPath = Path.Combine(downloadRoot, Path.GetFileName(fixture.ManifestPath));
            request.ConfigurationPath = Path.Combine(downloadRoot, Path.GetFileName(fixture.ConfigurationPath));
            request.SignaturePaths = Array.Empty<string>();
            request.SbomPaths = Array.Empty<string>();

            PowerForgeReleaseArtifactEvidence evidence = fixture.CreateVerifier().Verify(request);

            Assert.Equal("valid", evidence.SignatureStatus);
            Assert.Equal(aliasName, evidence.FileName);
        }
        finally
        {
            if (Directory.Exists(downloadRoot)) Directory.Delete(downloadRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("win-arm64", "net10.0", "PortableCompat")]
    [InlineData("win-x64", "net8.0", "PortableCompat")]
    [InlineData("win-x64", "net10.0", "SelfContained")]
    public void Verify_PortableCliRejectsDirectArtifactWithDifferentPublisherSignedDimensions(
        string runtime,
        string framework,
        string style)
    {
        using var fixture = new PortableFixture();
        fixture.ConfigureDirectPackaging();
        fixture.SetDirectInventoryDimensions(runtime, framework, style);
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.ArtifactPath = fixture.ExecutablePath;
        request.SignaturePaths = Array.Empty<string>();
        request.SbomPaths = Array.Empty<string>();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(request));

        Assert.Contains("publisher-signed direct portable dimensions", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliRejectsPublisherSignedInventoryForDifferentTarget()
    {
        using var fixture = new PortableFixture();
        fixture.SetInventoryTarget("Other.CLI");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("publisher-signed portable payload identity", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliAcceptsTargetAliasBoundToProjectExecutableIdentity()
    {
        using var fixture = new PortableFixture();
        fixture.ConfigureTargetAlias("ProductAlias");
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.ArtifactId = "ProductAlias";
        request.Target = "ProductAlias";

        PowerForgeReleaseArtifactEvidence result = fixture.CreateVerifier().Verify(request);

        Assert.Equal("ProductAlias", result.ArtifactId);
        Assert.Equal("valid", result.SignatureStatus);
    }

    [Fact]
    public void Verify_PortableCliRejectsSameSubjectInventorySignedByDifferentCertificate()
    {
        using var fixture = new PortableFixture();
        fixture.ConfigureSubjectNameSigning();
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.SignThumbprint = null;
        request.SignSubjectName = "CN=Publisher";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier(Thumbprint, VendorThumbprint).Verify(request));

        Assert.Contains("Authenticode publisher certificate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliRejectsUnrelatedDirectExecutableSubstitution()
    {
        using var fixture = new PortableFixture();
        fixture.ConfigureDirectPackaging();
        string unrelatedDirectory = Directory.CreateDirectory(Path.Combine(fixture.Root, "substitution")).FullName;
        string unrelated = Path.Combine(unrelatedDirectory, "Sample.CLI.exe");
        File.WriteAllText(unrelated, "signed unrelated payload");
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.ArtifactPath = unrelated;

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(request));

        Assert.Contains("SHA-256", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed partial class PortableFixture
    {
        internal void AddUnexpectedArchiveEntry(string name, string content)
        {
            using (System.IO.Compression.ZipArchive archive = System.IO.Compression.ZipFile.Open(
                       ArchivePath,
                       System.IO.Compression.ZipArchiveMode.Update))
            {
                System.IO.Compression.ZipArchiveEntry entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
            WriteBoundCycloneDxSbom("Sample.CLI", "1.2.3", ComputeDigest(ArchivePath));
            WriteChecksums();
        }

        internal void WriteLargePayload(int length)
        {
            byte[] payload = Enumerable.Repeat((byte)'x', length).ToArray();
            File.WriteAllBytes(ExecutablePath, payload);
            WritePortableInventory(new[] { ExecutablePath });
            WriteArchiveFromOutput();
            WriteBoundCycloneDxSbom("Sample.CLI", "1.2.3", ComputeDigest(ArchivePath));
            WriteChecksums();
        }

        internal void TamperArchiveEntry(string name, string content)
        {
            using (System.IO.Compression.ZipArchive archive = System.IO.Compression.ZipFile.Open(
                       ArchivePath,
                       System.IO.Compression.ZipArchiveMode.Update))
            {
                System.IO.Compression.ZipArchiveEntry entry = archive.GetEntry(name)!;
                entry.Delete();
                entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
            WriteBoundCycloneDxSbom("Sample.CLI", "1.2.3", ComputeDigest(ArchivePath));
            WriteChecksums();
        }

        internal void SetInventoryTarget(string target)
        {
            WritePortableInventory(new[] { ExecutablePath });
            string inventoryPath = Path.Combine(OutputDirectory, PowerForgePortablePayloadInventory.InventoryFileName);
            PowerForgePortablePayloadInventory inventory = JsonSerializer.Deserialize<PowerForgePortablePayloadInventory>(
                File.ReadAllBytes(inventoryPath))!;
            inventory.ArtifactId = target;
            inventory.Target = target;
            File.WriteAllBytes(inventoryPath, PowerForgePortablePayloadInventoryCms.Serialize(inventory));
            WriteArchiveFromOutput();
            WriteBoundCycloneDxSbom("Sample.CLI", "1.2.3", ComputeDigest(ArchivePath));
            WriteChecksums();
        }

        internal void SetArchiveInventorySourceDirty()
        {
            WritePortableInventory(new[] { ExecutablePath });
            string inventoryPath = Path.Combine(OutputDirectory, PowerForgePortablePayloadInventory.InventoryFileName);
            PowerForgePortablePayloadInventory inventory = JsonSerializer.Deserialize<PowerForgePortablePayloadInventory>(
                File.ReadAllBytes(inventoryPath))!;
            inventory.SourceDirty = true;
            File.WriteAllBytes(inventoryPath, PowerForgePortablePayloadInventoryCms.Serialize(inventory));
            WriteArchiveFromOutput();
            WriteBoundCycloneDxSbom("Sample.CLI", "1.2.3", ComputeDigest(ArchivePath));
            WriteChecksums();
        }

        internal void SetDirectInventoryDimensions(string runtime, string framework, string style)
        {
            WriteDirectInventory(runtime, framework, style);
            WriteChecksums();
        }

        internal void ConfigureExplicitExecutableIdentity()
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
                            ExecutableIdentity = "Sample.CLI",
                            Sign = new { Enabled = true, Thumbprint }
                        }
                    }
                }
            }));
            WriteDirectInventory();
            WriteChecksums();
        }

        internal void ConfigureDirectPackaging()
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
                }
            }));
            WriteDirectInventory();
            WriteChecksums();
        }
    }
}

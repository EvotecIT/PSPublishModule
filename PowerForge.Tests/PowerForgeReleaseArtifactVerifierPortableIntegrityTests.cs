using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseArtifactVerifierTests
{
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
                fixture.SbomPath
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

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier(Thumbprint, VendorThumbprint).Verify(fixture.CreateRequest()));

        Assert.Contains("Authenticode publisher certificate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliRejectsUnrelatedDirectExecutableSubstitution()
    {
        using var fixture = new PortableFixture();
        string unrelatedDirectory = Directory.CreateDirectory(Path.Combine(fixture.Root, "substitution")).FullName;
        string unrelated = Path.Combine(unrelatedDirectory, "Sample.CLI.exe");
        File.WriteAllText(unrelated, "signed unrelated payload");
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.ArtifactPath = unrelated;

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(request));

        Assert.Contains("manifest executable", exception.Message, StringComparison.OrdinalIgnoreCase);
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
    }
}

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
            if (File.Exists(ArchivePath)) File.Delete(ArchivePath);
            using (System.IO.Compression.ZipArchive archive = System.IO.Compression.ZipFile.Open(
                       ArchivePath,
                       System.IO.Compression.ZipArchiveMode.Create))
            {
                System.IO.Compression.ZipArchiveEntry entry = archive.CreateEntry(
                    "Sample.CLI.exe",
                    System.IO.Compression.CompressionLevel.Optimal);
                using Stream output = entry.Open();
                output.Write(payload, 0, payload.Length);
            }
            WriteBoundCycloneDxSbom("Sample.CLI", "1.2.3", ComputeDigest(ArchivePath));
            WriteChecksums();
        }
    }
}

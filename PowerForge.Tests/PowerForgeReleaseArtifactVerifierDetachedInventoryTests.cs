using System.IO.Compression;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseArtifactVerifierTests
{
    [Fact]
    public void Verify_PortableArchiveBindsDetachedInventoryAndCleanPayload()
    {
        using var fixture = new PortableFixture();
        fixture.DetachArchiveInventory();

        PowerForgeReleaseArtifactEvidence result = fixture.CreateVerifier().Verify(fixture.CreateRequest());

        Assert.Equal("valid", result.SignatureStatus);
        Assert.Contains(result.EvidenceFiles, item => item.Role == "portable-inventory" &&
            item.Path == fixture.ArchivePath + PowerForgePortablePayloadInventory.DirectInventorySuffix);
        Assert.Contains(result.EvidenceFiles, item => item.Role == "portable-inventory-signature");
        using ZipArchive archive = ZipFile.OpenRead(fixture.ArchivePath);
        Assert.Equal("Sample.CLI.exe", Assert.Single(archive.Entries).FullName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Verify_PortableArchiveRejectsMissingOrTamperedDetachedEvidence(bool tamper)
    {
        using var fixture = new PortableFixture();
        fixture.DetachArchiveInventory();
        string signaturePath = fixture.ArchivePath + PowerForgePortablePayloadInventory.DirectSignatureSuffix;
        if (tamper) File.AppendAllText(signaturePath, "tampered");
        else File.Delete(signaturePath);

        if (tamper)
            Assert.Throws<InvalidDataException>(() => fixture.CreateVerifier().Verify(fixture.CreateRequest()));
        else
            Assert.Throws<FileNotFoundException>(() => fixture.CreateVerifier().Verify(fixture.CreateRequest()));
    }

    [Fact]
    public void Verify_PortableArchiveRejectsChangedPayloadWithDetachedInventory()
    {
        using var fixture = new PortableFixture();
        fixture.DetachArchiveInventory();
        fixture.AddUnexpectedArchiveEntry("unexpected.txt", "not publisher approved");
        fixture.WriteDetachedChecksums();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));
        Assert.Contains("exactly match", exception.Message);
    }

    private sealed partial class PortableFixture
    {
        internal void DetachArchiveInventory()
        {
            using (ZipArchive archive = ZipFile.Open(ArchivePath, ZipArchiveMode.Update))
            {
                ZipArchiveEntry inventory = archive.GetEntry(PowerForgePortablePayloadInventory.InventoryFileName)!;
                ZipArchiveEntry signature = archive.GetEntry(PowerForgePortablePayloadInventory.SignatureFileName)!;
                inventory.ExtractToFile(ArchivePath + PowerForgePortablePayloadInventory.DirectInventorySuffix);
                signature.ExtractToFile(ArchivePath + PowerForgePortablePayloadInventory.DirectSignatureSuffix);
                inventory.Delete();
                signature.Delete();
            }
            WriteBoundCycloneDxSbom("Sample.CLI", "1.2.3", ComputeDigest(ArchivePath));
            WriteDetachedChecksums();
        }

        internal void WriteDetachedChecksums() => base.WriteChecksums(
            ManifestPath, ConfigurationPath, ArchivePath,
            ArchivePath + PowerForgePortablePayloadInventory.DirectInventorySuffix,
            ArchivePath + PowerForgePortablePayloadInventory.DirectSignatureSuffix);
    }
}

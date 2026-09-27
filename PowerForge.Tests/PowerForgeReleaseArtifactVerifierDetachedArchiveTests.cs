using System.IO.Compression;
using System.Text.Json.Nodes;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseArtifactVerifierTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Verify_PortableCliAcceptsRenamedDetachedArchiveWithManifestIdentity(bool explicitArtifact)
    {
        using var fixture = new PortableFixture();
        fixture.ConvertArchiveToDetachedEvidence();
        string alias = fixture.RenameDetachedArchive("Sample.CLI-1.2.3-win-x64-portable.zip");
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.ArtifactPath = explicitArtifact ? alias : string.Empty;
        request.SignaturePaths = Array.Empty<string>();

        PowerForgeReleaseArtifactEvidence evidence = fixture.CreateVerifier().Verify(request);

        Assert.Equal(Path.GetFullPath(alias), evidence.ArtifactPath);
        Assert.Contains(evidence.EvidenceFiles, item => item.Role == "portable-inventory");
    }

    [Fact]
    public void Verify_PortableCliNewManifestCannotFallBackToEmbeddedEvidence()
    {
        using var fixture = new PortableFixture();
        fixture.ConvertArchiveToDetachedEvidence();
        string inventoryPath = fixture.ArchivePath + PowerForgePortablePayloadInventory.DirectInventorySuffix;
        string signaturePath = fixture.ArchivePath + PowerForgePortablePayloadInventory.DirectSignatureSuffix;
        using (ZipArchive archive = ZipFile.Open(fixture.ArchivePath, ZipArchiveMode.Update))
        {
            foreach (string source in new[] { inventoryPath, signaturePath })
            {
                string name = source == inventoryPath
                    ? PowerForgePortablePayloadInventory.InventoryFileName
                    : PowerForgePortablePayloadInventory.SignatureFileName;
                using Stream output = archive.CreateEntry(name).Open();
                using FileStream input = File.OpenRead(source);
                input.CopyTo(output);
            }
        }
        File.Delete(inventoryPath);
        File.Delete(signaturePath);
        fixture.WriteBoundCycloneDxSbom("Sample.CLI", "1.2.3", fixture.ComputeDigest(fixture.ArchivePath));
        fixture.WriteChecksums();

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));
        Assert.Contains("requires detached release evidence", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Verify_PortableCliAcceptsRenamedDetachedDirectArtifact(bool explicitArtifact)
    {
        using var fixture = new PortableFixture();
        fixture.ConfigureDirectPackaging();
        string alias = fixture.RenameDetachedDirectArtifact("Sample.CLI-1.2.3-win-x64.exe");
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.ArtifactPath = explicitArtifact ? alias : string.Empty;
        request.SignaturePaths = Array.Empty<string>();
        request.SbomPaths = Array.Empty<string>();

        PowerForgeReleaseArtifactEvidence evidence = fixture.CreateVerifier().Verify(request);

        Assert.Equal(Path.GetFullPath(alias), evidence.ArtifactPath);
    }

    private sealed partial class PortableFixture
    {
        internal void ConvertArchiveToDetachedEvidence()
        {
            string inventoryPath = ArchivePath + PowerForgePortablePayloadInventory.DirectInventorySuffix;
            string signaturePath = ArchivePath + PowerForgePortablePayloadInventory.DirectSignatureSuffix;
            using (ZipArchive archive = ZipFile.Open(ArchivePath, ZipArchiveMode.Update))
            {
                foreach ((string name, string path) in new[]
                         {
                             (PowerForgePortablePayloadInventory.InventoryFileName, inventoryPath),
                             (PowerForgePortablePayloadInventory.SignatureFileName, signaturePath)
                         })
                {
                    ZipArchiveEntry entry = archive.GetEntry(name)!;
                    using (Stream input = entry.Open())
                    using (FileStream output = File.Create(path)) input.CopyTo(output);
                    entry.Delete();
                }
            }
            JsonArray manifest = (JsonNode.Parse(File.ReadAllText(ManifestPath)) as JsonArray)!;
            manifest[0]!["EvidencePaths"] = new JsonArray(inventoryPath, signaturePath);
            File.WriteAllText(ManifestPath, manifest.ToJsonString());
            WriteBoundCycloneDxSbom("Sample.CLI", "1.2.3", ComputeDigest(ArchivePath));
            WriteDetachedChecksums(ArchivePath);
        }

        internal string RenameDetachedArchive(string aliasName)
        {
            string alias = Path.Combine(Root, "staged", "portable", aliasName);
            Directory.CreateDirectory(Path.GetDirectoryName(alias)!);
            foreach (string suffix in new[] { "", PowerForgePortablePayloadInventory.DirectInventorySuffix,
                         PowerForgePortablePayloadInventory.DirectSignatureSuffix })
            {
                File.Move(ArchivePath + suffix, alias + suffix);
            }
            WriteDetachedChecksums(alias);
            return alias;
        }

        internal string RenameDetachedDirectArtifact(string aliasName)
        {
            JsonArray manifest = (JsonNode.Parse(File.ReadAllText(ManifestPath)) as JsonArray)!;
            manifest[0]!["ZipPath"] = null;
            manifest[0]!["EvidencePaths"] = new JsonArray(DirectInventoryPath, DirectSignaturePath);
            File.WriteAllText(ManifestPath, manifest.ToJsonString());
            string alias = Path.Combine(Root, "staged", "tools", aliasName);
            Directory.CreateDirectory(Path.GetDirectoryName(alias)!);
            foreach (string suffix in new[] { "", PowerForgePortablePayloadInventory.DirectInventorySuffix,
                         PowerForgePortablePayloadInventory.DirectSignatureSuffix })
                File.Move(ExecutablePath + suffix, alias + suffix);
            base.WriteChecksums(ManifestPath, ConfigurationPath, alias,
                alias + PowerForgePortablePayloadInventory.DirectInventorySuffix,
                alias + PowerForgePortablePayloadInventory.DirectSignatureSuffix);
            return alias;
        }

        private void WriteDetachedChecksums(string archive)
        {
            base.WriteChecksums(ManifestPath, ConfigurationPath, ExecutablePath, DirectInventoryPath,
                DirectSignaturePath, archive, archive + PowerForgePortablePayloadInventory.DirectInventorySuffix,
                archive + PowerForgePortablePayloadInventory.DirectSignatureSuffix);
        }
    }
}

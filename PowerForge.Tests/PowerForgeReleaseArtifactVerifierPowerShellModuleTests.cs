using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseArtifactVerifierTests
{
    [Fact]
    public void Verify_PowerShellModuleReturnsEmbeddedProvenanceAndExternalSbomEvidence()
    {
        using var fixture = new ModuleFixture();

        PowerForgeReleaseArtifactEvidence result = fixture.CreateVerifier().Verify(fixture.CreateRequest());

        Assert.Equal(PowerForgeReleaseArtifactKind.PowerShellModule, result.ArtifactKind);
        Assert.Equal("Sample", result.ArtifactId);
        Assert.Equal("2.3.4", result.Version);
        Assert.Equal(SourceRevision, result.SourceRevision);
        Assert.Equal(Thumbprint, result.SignerThumbprint);
        Assert.Equal("valid", result.SignatureStatus);
        Assert.Contains(result.SignaturePaths, path => path.EndsWith("!Sample/Sample.psm1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.SignaturePaths, path => path.EndsWith("!Sample/Sample.psd1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.EvidenceFiles, item => item.Role == "provenance" && item.Path.Contains("!Sample/PowerForge.ReleaseProvenance.json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.EvidenceFiles, item => item.Role == "signing-policy" && item.Path == fixture.SigningEvidencePath);
        Assert.Contains(result.EvidenceFiles, item => item.Role == "sbom");
    }

    [Fact]
    public void Verify_PowerShellModuleRejectsArchiveFromAnotherRevisionEvenWithUpdatedChecksum()
    {
        using var fixture = new ModuleFixture();
        fixture.WriteArchive(new string('c', 40));
        fixture.WriteChecksums();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("workflow commit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PowerShellModuleRejectsUnexpectedSigner()
    {
        using var fixture = new ModuleFixture();
        PowerForgeReleaseArtifactVerifier verifier = fixture.CreateVerifier(new string('C', 40));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => verifier.Verify(fixture.CreateRequest()));

        Assert.Contains("configured release certificate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PowerShellModuleAcceptsIdentifiedValidThirdPartyDependency()
    {
        using var fixture = new ModuleFixture();
        fixture.PrepareVendorDependency(VendorThumbprint);

        PowerForgeReleaseArtifactEvidence result = fixture.CreateVendorAwareVerifier(VendorThumbprint)
            .Verify(fixture.CreateRequest());

        Assert.Contains(result.Signatures, signature =>
            signature.Ownership == "publisher" && signature.Path.EndsWith("Sample.psd1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Signatures, signature =>
            signature.Ownership == "third-party" &&
            signature.Path.EndsWith("Vendor.dll", StringComparison.OrdinalIgnoreCase) &&
            signature.Thumbprint == VendorThumbprint);
    }

    [Fact]
    public void Verify_PowerShellModuleRejectsMisattributedThirdPartyDependency()
    {
        using var fixture = new ModuleFixture();
        fixture.PrepareVendorDependency(new string('D', 40));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVendorAwareVerifier(VendorThumbprint).Verify(fixture.CreateRequest()));

        Assert.Contains("signer identity does not match", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PowerShellModuleRejectsInvalidThirdPartyDependency()
    {
        using var fixture = new ModuleFixture();
        fixture.PrepareVendorDependency(VendorThumbprint);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVendorAwareVerifier(VendorThumbprint, vendorSignatureValid: false)
                .Verify(fixture.CreateRequest()));

        Assert.Contains("signature is not valid", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PowerShellModuleRejectsThirdPartyOwnershipForRootModule()
    {
        using var fixture = new ModuleFixture();
        fixture.PrepareRootModuleAsVendor(VendorThumbprint);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateRootVendorAwareVerifier(VendorThumbprint).Verify(fixture.CreateRequest()));

        Assert.Contains("RootModule must be owned", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(".psm1")]
    [InlineData(".dll")]
    public void Verify_PowerShellModuleRejectsUncoveredSignableFile(string extension)
    {
        using var fixture = new ModuleFixture();
        fixture.WriteArchive(SourceRevision, unexpectedSignableExtension: extension);
        fixture.WriteChecksums();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("omits signable module file", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PowerShellModuleRejectsDuplicateArchiveEntry()
    {
        using var fixture = new ModuleFixture();
        fixture.WriteArchive(SourceRevision, duplicateManifest: true);
        fixture.WriteChecksums();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("duplicate entry", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PowerShellModuleRejectsTraversalArchiveEntry()
    {
        using var fixture = new ModuleFixture();
        fixture.WriteArchive(SourceRevision, traversalEntry: true);
        fixture.WriteChecksums();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("unsafe entry", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ModuleFixture : FixtureBase
    {
        private bool _hasVendorDependency;

        internal ModuleFixture()
        {
            ArchivePath = Path.Combine(Root, "Sample.2.3.4.nupkg");
            SigningEvidencePath = Path.Combine(Root, "Sample.signing.json");
            WriteArchive(SourceRevision);
            WriteSigningEvidence();
            WriteChecksums();
        }

        internal string ArchivePath { get; }
        internal string SigningEvidencePath { get; }

        internal PowerForgeReleaseArtifactVerificationRequest CreateRequest() => new()
        {
            Kind = PowerForgeReleaseArtifactKind.PowerShellModule,
            ArtifactId = "Sample",
            ProjectRoot = Root,
            ArtifactPath = ArchivePath,
            ChecksumsPath = ChecksumsPath,
            ExpectedSourceRevision = SourceRevision,
            ExpectedVersion = "2.3.4",
            SignThumbprint = Thumbprint,
            SigningEvidencePath = SigningEvidencePath,
            SignaturePaths = _hasVendorDependency
                ? new[] { "Sample/Sample.psd1", "Sample/Sample.psm1", "Sample/lib/Vendor.dll" }
                : new[] { "Sample/Sample.psd1", "Sample/Sample.psm1" },
            SbomPaths = new[] { SbomPath }
        };

        internal void WriteArchive(
            string sourceRevision,
            string? unexpectedSignableExtension = null,
            bool duplicateManifest = false,
            bool traversalEntry = false,
            bool includeVendorDependency = false)
        {
            if (File.Exists(ArchivePath)) File.Delete(ArchivePath);
            using ZipArchive archive = ZipFile.Open(ArchivePath, ZipArchiveMode.Create);
            WriteEntry(archive, "Sample/Sample.psd1", "@{ RootModule = 'Sample.psm1'; ModuleVersion = '2.3.4' }");
            if (duplicateManifest)
                WriteEntry(archive, "Sample/Sample.psd1", "@{ RootModule = 'Sample.psm1'; ModuleVersion = '2.3.4' }");
            WriteEntry(archive, "Sample/Sample.psm1", "# signed module");
            if (!string.IsNullOrWhiteSpace(unexpectedSignableExtension))
                WriteEntry(archive, "Sample/evil" + unexpectedSignableExtension, "unsigned executable payload");
            if (includeVendorDependency)
                WriteEntry(archive, "Sample/lib/Vendor.dll", "valid vendor-signed dependency");
            if (traversalEntry)
                WriteEntry(archive, "../escape.ps1", "# unsafe payload");
            WriteEntry(archive, "Sample/PowerForge.ReleaseProvenance.json", JsonSerializer.Serialize(new
            {
                moduleName = "Sample",
                version = "2.3.4",
                repository = "https://github.com/EvotecIT/Sample",
                commit = sourceRevision
            }));
        }

        internal void WriteChecksums() => base.WriteChecksums(ArchivePath, SigningEvidencePath);

        internal void PrepareVendorDependency(string evidenceThumbprint)
        {
            _hasVendorDependency = true;
            WriteArchive(SourceRevision, includeVendorDependency: true);
            WriteSigningEvidence(evidenceThumbprint);
            WriteChecksums();
        }

        internal void PrepareRootModuleAsVendor(string evidenceThumbprint)
        {
            File.WriteAllText(SigningEvidencePath, JsonSerializer.Serialize(new PowerForgeModuleSigningEvidence
            {
                SchemaVersion = 1,
                ModuleName = "Sample",
                Version = "2.3.4",
                SourceRevision = SourceRevision,
                ManifestPath = "Sample/Sample.psd1",
                SignableFiles = new[] { "Sample/Sample.psd1", "Sample/Sample.psm1" },
                PreservedThirdPartySignatures = new[]
                {
                    new PowerForgeModulePreservedSignature
                    {
                        Path = "Sample/Sample.psm1",
                        Subject = "CN=Vendor",
                        Thumbprint = evidenceThumbprint
                    }
                }
            }));
            WriteChecksums();
        }

        internal PowerForgeReleaseArtifactVerifier CreateVendorAwareVerifier(
            string actualVendorThumbprint,
            bool vendorSignatureValid = true) =>
            new(
                path => Path.GetFileName(path).EndsWith("Vendor.dll", StringComparison.OrdinalIgnoreCase)
                    ? new DotNetPublishReleaseArtifactVerifier.AuthenticodeResult(
                        vendorSignatureValid,
                        vendorSignatureValid ? 0 : unchecked((int)0x800B0100),
                        "CN=Vendor",
                        actualVendorThumbprint)
                    : new DotNetPublishReleaseArtifactVerifier.AuthenticodeResult(true, 0, "CN=Publisher", Thumbprint),
                _ => "1.2.3.0");

        internal PowerForgeReleaseArtifactVerifier CreateRootVendorAwareVerifier(string actualVendorThumbprint) =>
            new(
                path => Path.GetFileName(path).EndsWith("Sample.psm1", StringComparison.OrdinalIgnoreCase)
                    ? new DotNetPublishReleaseArtifactVerifier.AuthenticodeResult(true, 0, "CN=Vendor", actualVendorThumbprint)
                    : new DotNetPublishReleaseArtifactVerifier.AuthenticodeResult(true, 0, "CN=Publisher", Thumbprint),
                _ => "1.2.3.0");

        private void WriteSigningEvidence(string? vendorThumbprint = null)
        {
            File.WriteAllText(SigningEvidencePath, JsonSerializer.Serialize(new PowerForgeModuleSigningEvidence
            {
                SchemaVersion = 1,
                ModuleName = "Sample",
                Version = "2.3.4",
                SourceRevision = SourceRevision,
                ManifestPath = "Sample/Sample.psd1",
                SignableFiles = vendorThumbprint is null
                    ? new[] { "Sample/Sample.psd1", "Sample/Sample.psm1" }
                    : new[] { "Sample/Sample.psd1", "Sample/Sample.psm1", "Sample/lib/Vendor.dll" },
                PreservedThirdPartySignatures = vendorThumbprint is null
                    ? Array.Empty<PowerForgeModulePreservedSignature>()
                    : new[]
                    {
                        new PowerForgeModulePreservedSignature
                        {
                            Path = "Sample/lib/Vendor.dll",
                            Subject = "CN=Vendor",
                            Thumbprint = vendorThumbprint
                        }
                    }
            }));
        }

        private static void WriteEntry(ZipArchive archive, string path, string content)
        {
            ZipArchiveEntry entry = archive.CreateEntry(path);
            using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
    }
}

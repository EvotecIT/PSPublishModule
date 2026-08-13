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
    [InlineData(".ps1xml")]
    [InlineData(".cdxml")]
    [InlineData(".exe")]
    public void Verify_PowerShellModuleAllowsFilesExcludedBySigningSelection(string extension)
    {
        using var fixture = new ModuleFixture();
        fixture.WriteArchive(SourceRevision, unexpectedSignableExtension: extension);
        fixture.WriteChecksums();

        PowerForgeReleaseArtifactEvidence evidence = fixture.CreateVerifier().Verify(fixture.CreateRequest());

        Assert.Equal(2, evidence.SignaturePaths.Length);
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

    [Fact]
    public void Verify_PowerShellModuleRejectsCaseConflictingDuplicateArchiveEntry()
    {
        using var fixture = new ModuleFixture();
        fixture.WriteArchive(SourceRevision, caseDuplicateManifest: true);
        fixture.WriteChecksums();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("duplicate entry", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PowerShellModuleRequiresExactCaseForManifestEntry()
    {
        using var fixture = new ModuleFixture();
        fixture.WriteArchive(SourceRevision, manifestEntryPath: "Sample/sample.psd1");
        fixture.WriteChecksums();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("exactly one 'Sample.psd1'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_PowerShellModuleRequiresExactCaseForRootModuleEntry()
    {
        using var fixture = new ModuleFixture();
        fixture.WriteArchive(SourceRevision, rootModuleValue: "Sample.Psm1");
        fixture.WriteChecksums();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("RootModule", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_PowerShellModuleRequiresExactCaseForSigningEvidencePaths()
    {
        using var fixture = new ModuleFixture();
        fixture.WriteSigningEvidence(manifestPath: "Sample/sample.psd1");
        fixture.WriteChecksums();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("does not identify", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PowerShellModuleRejectsSigningEvidenceWithoutSchemaVersion()
    {
        using var fixture = new ModuleFixture();
        fixture.WriteSigningEvidence(includeSchemaVersion: false);
        fixture.WriteChecksums();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("schema version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Verify_PowerShellModuleAcceptsBomEncodedUtf16Manifest(bool bigEndian)
    {
        using var fixture = new ModuleFixture();
        fixture.WriteArchive(SourceRevision, manifestEncoding: new UnicodeEncoding(bigEndian, true, true));
        fixture.WriteChecksums();

        PowerForgeReleaseArtifactEvidence evidence = fixture.CreateVerifier().Verify(fixture.CreateRequest());

        Assert.Equal("2.3.4", evidence.Version);
    }

    [Theory]
    [InlineData(new byte[] { 0xC3, 0x28 })]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x41 })]
    public void Verify_PowerShellModuleRejectsMalformedManifestEncoding(byte[] manifestBytes)
    {
        using var fixture = new ModuleFixture();
        fixture.WriteArchive(SourceRevision, manifestBytes: manifestBytes);
        fixture.WriteChecksums();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("encoding is malformed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PowerShellModuleRejectsDirtySigningEvidence()
    {
        using var fixture = new ModuleFixture();
        fixture.WriteSigningEvidence(sourceDirty: true);
        fixture.WriteChecksums();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("clean source checkout", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PowerShellModuleRejectsDirtyEmbeddedProvenance()
    {
        using var fixture = new ModuleFixture();
        fixture.WriteArchive(SourceRevision, provenanceDirty: true);
        fixture.WriteChecksums();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("provenance must attest a clean", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PowerShellModuleUsesFullPrereleaseIdentity()
    {
        using var fixture = new ModuleFixture();
        fixture.PreparePrerelease("preview.2");

        PowerForgeReleaseArtifactEvidence result = fixture.CreateVerifier().Verify(fixture.CreateRequest());

        Assert.Equal("2.3.4-preview.2", result.Version);
    }

    [Fact]
    public void Verify_PowerShellModuleRejectsPrereleaseChannelMismatch()
    {
        using var fixture = new ModuleFixture();
        fixture.PreparePrerelease("preview.2");
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.ExpectedVersion = "2.3.4-preview.1";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(request));

        Assert.Contains("does not match expected version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ModuleFixture : FixtureBase
    {
        private bool _hasVendorDependency;
        private string _version = "2.3.4";

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
            ExpectedVersion = _version,
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
            bool includeVendorDependency = false,
            string? prerelease = null,
            bool provenanceDirty = false,
            bool caseDuplicateManifest = false,
            string manifestEntryPath = "Sample/Sample.psd1",
            string rootModuleValue = "Sample.psm1",
            Encoding? manifestEncoding = null,
            byte[]? manifestBytes = null)
        {
            if (File.Exists(ArchivePath)) File.Delete(ArchivePath);
            using ZipArchive archive = ZipFile.Open(ArchivePath, ZipArchiveMode.Create);
            string prereleaseData = string.IsNullOrWhiteSpace(prerelease)
                ? string.Empty
                : $"; PrivateData = @{{ PSData = @{{ Prerelease = '{prerelease}' }} }}";
            string manifestText = $"@{{ RootModule = '{rootModuleValue}'; ModuleVersion = '2.3.4'{prereleaseData} }}";
            WriteEntry(archive, manifestEntryPath, manifestBytes ?? Encode(manifestText, manifestEncoding));
            if (duplicateManifest)
                WriteEntry(archive, "Sample/Sample.psd1", "@{ RootModule = 'Sample.psm1'; ModuleVersion = '2.3.4' }");
            if (caseDuplicateManifest)
                WriteEntry(archive, "Sample/sample.psd1", "@{ RootModule = 'Sample.psm1'; ModuleVersion = '2.3.4' }");
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
                version = string.IsNullOrWhiteSpace(prerelease) ? "2.3.4" : "2.3.4-" + prerelease,
                repository = "https://github.com/EvotecIT/Sample",
                commit = sourceRevision,
                sourceDirty = provenanceDirty
            }));
        }

        internal void WriteChecksums()
        {
            WriteBoundCycloneDxSbom("Sample", _version, ComputeDigest(ArchivePath));
            base.WriteChecksums(ArchivePath, SigningEvidencePath);
        }

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
                SchemaVersion = 2,
                ModuleName = "Sample",
                Version = "2.3.4",
                SourceRevision = SourceRevision,
                SourceDirty = false,
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

        internal void PreparePrerelease(string label)
        {
            _version = "2.3.4-" + label;
            WriteArchive(SourceRevision, prerelease: label);
            WriteSigningEvidence(version: _version);
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

        internal void WriteSigningEvidence(
            string? vendorThumbprint = null,
            string? version = null,
            bool? sourceDirty = false,
            string manifestPath = "Sample/Sample.psd1",
            bool includeSchemaVersion = true)
        {
            var evidence = new PowerForgeModuleSigningEvidence
            {
                SchemaVersion = includeSchemaVersion ? 2 : 0,
                ModuleName = "Sample",
                Version = version ?? _version,
                SourceRevision = SourceRevision,
                SourceDirty = sourceDirty,
                ManifestPath = manifestPath,
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
            };
            string json = JsonSerializer.Serialize(evidence);
            if (!includeSchemaVersion)
            {
                using JsonDocument document = JsonDocument.Parse(json);
                var properties = document.RootElement.EnumerateObject()
                    .Where(property => !property.Name.Equals("SchemaVersion", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(property => property.Name, property => property.Value.Clone());
                json = JsonSerializer.Serialize(properties);
            }
            File.WriteAllText(SigningEvidencePath, json);
        }

        private static void WriteEntry(ZipArchive archive, string path, string content)
        {
            ZipArchiveEntry entry = archive.CreateEntry(path);
            using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        private static void WriteEntry(ZipArchive archive, string path, byte[] content)
        {
            ZipArchiveEntry entry = archive.CreateEntry(path);
            using Stream output = entry.Open();
            output.Write(content, 0, content.Length);
        }

        private static byte[] Encode(string content, Encoding? encoding)
        {
            Encoding selected = encoding ?? new UTF8Encoding(false, true);
            return selected.GetPreamble().Concat(selected.GetBytes(content)).ToArray();
        }
    }
}

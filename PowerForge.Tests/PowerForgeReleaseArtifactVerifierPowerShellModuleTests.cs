using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseArtifactVerifierTests
{
    [Fact]
    public void Verify_PowerShellModuleRejectsOversizedPayloadBeforeHashing()
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string archivePath = Path.Combine(root, "Sample.zip");
            using (ZipArchive created = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = created.CreateEntry("Sample/data.bin");
                using Stream stream = entry.Open();
                stream.Write(new byte[9], 0, 9);
            }
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            Dictionary<string, ZipArchiveEntry> entries = archive.Entries.ToDictionary(
                entry => entry.FullName,
                StringComparer.Ordinal);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
                PowerForgeReleaseArtifactVerifier.ValidateModuleArchiveBounds(
                    entries,
                    maximumEntryBytes: 8,
                    maximumTotalBytes: 16));

            Assert.Contains("exceeds", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("data.bin", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

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
        Assert.Contains(result.SignaturePaths, path => path.EndsWith("!Sample/PowerForge.ReleaseProvenance.psd1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.EvidenceFiles, item => item.Role == "provenance" && item.Path.Contains("!Sample/PowerForge.ReleaseProvenance.json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.EvidenceFiles, item => item.Role == "signed-provenance" && item.Path.Contains("!Sample/PowerForge.ReleaseProvenance.psd1", StringComparison.OrdinalIgnoreCase));
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

        Assert.Contains("trusted publisher certificate", exception.Message, StringComparison.OrdinalIgnoreCase);
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

        Assert.Contains("RootModule", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("owned", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PowerShellModuleRejectsSidecarThatOmitsSignedInventoryEntry()
    {
        using var fixture = new ModuleFixture();
        fixture.PrepareSigningInventoryOmission();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("complete signing inventory", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PowerShellModuleRejectsManifestLoadedFormatOutsideSigningInventory()
    {
        using var fixture = new ModuleFixture();
        fixture.PrepareManifestLoadedFormatOmission();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("manifest-loaded", exception.Message, StringComparison.OrdinalIgnoreCase);
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

        Assert.Equal(3, evidence.SignaturePaths.Length);
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
            ChecksumsSignaturePath = ChecksumsSignaturePath,
            ExpectedSourceRevision = SourceRevision,
            ExpectedVersion = _version,
            SignThumbprint = Thumbprint,
            SigningEvidencePath = SigningEvidencePath,
            SignaturePaths = _hasVendorDependency
                ? new[] { "Sample/Sample.psd1", "Sample/Sample.psm1", "Sample/PowerForge.ReleaseProvenance.psd1", "Sample/lib/Vendor.dll" }
                : new[] { "Sample/Sample.psd1", "Sample/Sample.psm1", "Sample/PowerForge.ReleaseProvenance.psd1" },
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
            byte[]? manifestBytes = null,
            string? signedSourceRevision = null,
            bool includeDependencyProvenance = false,
            bool omitPrimaryProvenance = false,
            bool directoryCollision = false,
            bool includeBoundHelper = false,
            string? vendorThumbprint = null,
            bool includeManifestLoadedFormat = false)
        {
            if (File.Exists(ArchivePath)) File.Delete(ArchivePath);
            using ZipArchive archive = ZipFile.Open(ArchivePath, ZipArchiveMode.Create);
            string prereleaseData = string.IsNullOrWhiteSpace(prerelease)
                ? string.Empty
                : $"; PrivateData = @{{ PSData = @{{ Prerelease = '{prerelease}' }} }}";
            string formatData = includeManifestLoadedFormat ? "; FormatsToProcess = @('Sample.Format.ps1xml')" : string.Empty;
            string manifestText = $"@{{ RootModule = '{rootModuleValue}'; ModuleVersion = '2.3.4'{formatData}{prereleaseData} }}";
            WriteEntry(archive, manifestEntryPath, manifestBytes ?? Encode(manifestText, manifestEncoding));
            if (duplicateManifest)
                WriteEntry(archive, "Sample/Sample.psd1", "@{ RootModule = 'Sample.psm1'; ModuleVersion = '2.3.4' }");
            if (caseDuplicateManifest)
                WriteEntry(archive, "Sample/sample.psd1", "@{ RootModule = 'Sample.psm1'; ModuleVersion = '2.3.4' }");
            if (directoryCollision)
                _ = archive.CreateEntry("Sample/Sample.psm1/");
            WriteEntry(archive, "Sample/Sample.psm1", "# signed module");
            string signedVersion = string.IsNullOrWhiteSpace(prerelease) ? "2.3.4" : "2.3.4-" + prerelease;
            string[] boundSignableFiles = GetSignableFiles(includeVendorDependency, includeBoundHelper);
            PowerForgeModulePreservedSignature[] boundPreservedSignatures = GetPreservedSignatures(vendorThumbprint);
            string signingInventorySha256 = PowerForgeModuleSigningEvidenceWriter.ComputeSigningInventorySha256(
                boundSignableFiles,
                boundPreservedSignatures);
            WriteEntry(archive, "Sample/PowerForge.ReleaseProvenance.psd1", string.Join(Environment.NewLine, new[]
            {
                "@{",
                "    SchemaVersion = '2'",
                "    ModuleName = 'Sample'",
                $"    Version = '{signedVersion}'",
                $"    SourceRevision = '{signedSourceRevision ?? sourceRevision}'",
                "    SourceDirty = 'false'",
                $"    SigningInventorySha256 = '{signingInventorySha256}'",
                "}",
                string.Empty
            }));
            if (!string.IsNullOrWhiteSpace(unexpectedSignableExtension))
                WriteEntry(archive, "Sample/evil" + unexpectedSignableExtension, "unsigned executable payload");
            if (includeVendorDependency)
                WriteEntry(archive, "Sample/lib/Vendor.dll", "valid vendor-signed dependency");
            if (includeBoundHelper)
                WriteEntry(archive, "Sample/Private/Helper.ps1", "function Invoke-Helper { }");
            if (includeManifestLoadedFormat)
                WriteEntry(archive, "Sample/Sample.Format.ps1xml", "<Configuration />");
            if (traversalEntry)
                WriteEntry(archive, "../escape.ps1", "# unsafe payload");
            if (!omitPrimaryProvenance)
            {
                WriteEntry(archive, "Sample/PowerForge.ReleaseProvenance.json", JsonSerializer.Serialize(new
                {
                    moduleName = "Sample",
                    version = string.IsNullOrWhiteSpace(prerelease) ? "2.3.4" : "2.3.4-" + prerelease,
                    repository = "https://github.com/EvotecIT/Sample",
                    commit = sourceRevision,
                    sourceDirty = provenanceDirty
                }));
            }
            if (includeDependencyProvenance)
                WriteEntry(archive, "Sample/Internals/Modules/Dependency/PowerForge.ReleaseProvenance.json", "{}");
            archive.Dispose();
            if (!duplicateManifest)
                BindPayloadInventory();
        }

        internal void WriteChecksums()
        {
            WriteBoundCycloneDxSbom("Sample", _version, ComputeDigest(ArchivePath));
            base.WriteChecksums(ArchivePath, SigningEvidencePath);
        }

        internal void PrepareVendorDependency(string evidenceThumbprint)
        {
            _hasVendorDependency = true;
            WriteArchive(SourceRevision, includeVendorDependency: true, vendorThumbprint: evidenceThumbprint);
            WriteSigningEvidence(evidenceThumbprint);
            WriteChecksums();
        }

        internal void PrepareSigningInventoryOmission()
        {
            WriteArchive(SourceRevision, includeBoundHelper: true);
            WriteSigningEvidence();
            WriteChecksums();
        }

        internal void PrepareManifestLoadedFormatOmission()
        {
            WriteArchive(SourceRevision, includeManifestLoadedFormat: true);
            WriteSigningEvidence();
            WriteChecksums();
        }

        internal void TamperDataPayload()
        {
            using ZipArchive archive = ZipFile.Open(ArchivePath, ZipArchiveMode.Update);
            ZipArchiveEntry entry = archive.CreateEntry("Sample/config/settings.json");
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write("{\"tampered\":true}");
        }

        internal void PrepareRootModuleAsVendor(string evidenceThumbprint)
        {
            File.WriteAllText(SigningEvidencePath, JsonSerializer.Serialize(new PowerForgeModuleSigningEvidence
            {
                SchemaVersion = 3,
                ModuleName = "Sample",
                Version = "2.3.4",
                SourceRevision = SourceRevision,
                SourceDirty = false,
                ManifestPath = "Sample/Sample.psd1",
                SignableFiles = new[] { "Sample/Sample.psd1", "Sample/Sample.psm1", "Sample/PowerForge.ReleaseProvenance.psd1" },
                SigningInventorySha256 = PowerForgeModuleSigningEvidenceWriter.ComputeSigningInventorySha256(
                    new[] { "Sample/Sample.psd1", "Sample/Sample.psm1", "Sample/PowerForge.ReleaseProvenance.psd1" },
                    new[]
                    {
                        new PowerForgeModulePreservedSignature
                        {
                            Path = "Sample/Sample.psm1",
                            Subject = "CN=Vendor",
                            Thumbprint = evidenceThumbprint
                        }
                    }),
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
                _ => "1.2.3.0",
                verifyPortableInventory: (_, _) => new PowerForgePayloadInventorySignature("CN=Publisher", Thumbprint));

        internal PowerForgeReleaseArtifactVerifier CreateRootVendorAwareVerifier(string actualVendorThumbprint) =>
            new(
                path => Path.GetFileName(path).EndsWith("Sample.psm1", StringComparison.OrdinalIgnoreCase)
                    ? new DotNetPublishReleaseArtifactVerifier.AuthenticodeResult(true, 0, "CN=Vendor", actualVendorThumbprint)
                    : new DotNetPublishReleaseArtifactVerifier.AuthenticodeResult(true, 0, "CN=Publisher", Thumbprint),
                _ => "1.2.3.0",
                verifyPortableInventory: (_, _) => new PowerForgePayloadInventorySignature("CN=Publisher", Thumbprint));

        internal void WriteSigningEvidence(
            string? vendorThumbprint = null,
            string? version = null,
            bool? sourceDirty = false,
            string manifestPath = "Sample/Sample.psd1",
            bool includeSchemaVersion = true)
        {
            var evidence = new PowerForgeModuleSigningEvidence
            {
                SchemaVersion = includeSchemaVersion ? 3 : 0,
                ModuleName = "Sample",
                Version = version ?? _version,
                SourceRevision = SourceRevision,
                SourceDirty = sourceDirty,
                ManifestPath = manifestPath,
                SignableFiles = vendorThumbprint is null
                    ? new[] { "Sample/Sample.psd1", "Sample/Sample.psm1", "Sample/PowerForge.ReleaseProvenance.psd1" }
                    : new[] { "Sample/Sample.psd1", "Sample/Sample.psm1", "Sample/PowerForge.ReleaseProvenance.psd1", "Sample/lib/Vendor.dll" },
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
            evidence.SigningInventorySha256 = PowerForgeModuleSigningEvidenceWriter.ComputeSigningInventorySha256(
                evidence.SignableFiles,
                evidence.PreservedThirdPartySignatures);
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

        private static string[] GetSignableFiles(bool includeVendorDependency, bool includeBoundHelper)
        {
            var paths = new List<string>
            {
                "Sample/Sample.psd1",
                "Sample/Sample.psm1",
                "Sample/PowerForge.ReleaseProvenance.psd1"
            };
            if (includeVendorDependency)
                paths.Add("Sample/lib/Vendor.dll");
            if (includeBoundHelper)
                paths.Add("Sample/Private/Helper.ps1");
            return paths.ToArray();
        }

        private static PowerForgeModulePreservedSignature[] GetPreservedSignatures(string? vendorThumbprint) =>
            string.IsNullOrWhiteSpace(vendorThumbprint)
                ? Array.Empty<PowerForgeModulePreservedSignature>()
                : new[]
                {
                    new PowerForgeModulePreservedSignature
                    {
                        Path = "Sample/lib/Vendor.dll",
                        Subject = "CN=Vendor",
                        Thumbprint = vendorThumbprint
                    }
                };

        private void BindPayloadInventory()
        {
            const string provenancePath = "Sample/PowerForge.ReleaseProvenance.psd1";
            string payloadInventory;
            string provenance;
            using (ZipArchive archive = ZipFile.OpenRead(ArchivePath))
            {
                Dictionary<string, ZipArchiveEntry> entries = archive.Entries
                    .Where(entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal) &&
                                    !entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                    .ToDictionary(entry => entry.FullName.Replace('\\', '/'), StringComparer.Ordinal);
                payloadInventory = PowerForgePayloadInventoryHash.ComputeArchive(entries, new[] { provenancePath });
                using StreamReader reader = new(entries[provenancePath].Open(), new UTF8Encoding(false, true));
                provenance = reader.ReadToEnd();
            }

            provenance = provenance
                .Replace("SchemaVersion = '2'", "SchemaVersion = '3'", StringComparison.Ordinal)
                .Replace("}", $"    PayloadInventorySha256 = '{payloadInventory}'{Environment.NewLine}}}", StringComparison.Ordinal);
            using ZipArchive update = ZipFile.Open(ArchivePath, ZipArchiveMode.Update);
            update.GetEntry(provenancePath)!.Delete();
            WriteEntry(update, provenancePath, provenance);
        }
    }
}

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
    public void Verify_PortableCliAcceptsAzureCertificateRotationWithSameTrustedSubject()
    {
        using var fixture = new PortableFixture();
        fixture.ConfigureSubjectNameSigning(azureArtifactSigning: true);
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.SignThumbprint = null;
        request.SignSubjectName = "CN=Publisher";

        PowerForgeReleaseArtifactEvidence result = fixture
            .CreateVerifier(Thumbprint, VendorThumbprint)
            .Verify(request);

        Assert.Equal("valid", result.SignatureStatus);
    }

    [Fact]
    public void Verify_PortableCliRejectsSigningProviderChangedAfterInventoryWasSigned()
    {
        using var fixture = new PortableFixture();
        fixture.ConfigureSubjectNameSigning();
        fixture.ConfigureSubjectNameSigning(
            azureArtifactSigning: true,
            rewritePortableEvidence: false);
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.SignThumbprint = null;
        request.SignSubjectName = "CN=Publisher";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(request));

        Assert.Contains("configuration policy", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliAcceptsAzureCertificateRotationForDirectInventory()
    {
        using var fixture = new PortableFixture();
        fixture.ConfigureSubjectNameSigning(azureArtifactSigning: true, zip: false);
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.ArtifactPath = fixture.ExecutablePath;
        request.SignThumbprint = null;
        request.SignSubjectName = "CN=Publisher";
        request.SignaturePaths = Array.Empty<string>();
        request.SbomPaths = Array.Empty<string>();

        PowerForgeReleaseArtifactEvidence result = fixture
            .CreateVerifier(Thumbprint, VendorThumbprint)
            .Verify(request);

        Assert.Equal("valid", result.SignatureStatus);
        Assert.Single(result.Signatures);
        Assert.Equal(Thumbprint, result.SignerThumbprint);
    }

    [Fact]
    public void Verify_PortableCliRejectsUntrustedDirectInventoryCertificateDuringAzureRotation()
    {
        using var fixture = new PortableFixture();
        fixture.ConfigureSubjectNameSigning(azureArtifactSigning: true, zip: false);
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.ArtifactPath = fixture.ExecutablePath;
        request.SignThumbprint = null;
        request.SignSubjectName = "CN=Publisher";
        request.SignaturePaths = Array.Empty<string>();
        request.SbomPaths = Array.Empty<string>();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => fixture
            .CreateVerifier(
                Thumbprint,
                VendorThumbprint,
                inventoryCertificateTrusted: false)
            .Verify(request));

        Assert.Contains("trusted code-signing certificate chain", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliAcceptsAzureCertificateRotationAcrossPayloadFiles()
    {
        using var fixture = new PortableFixture();
        string dependency = fixture.AddSignedDependency();
        fixture.EnableDllSigning(dependency);
        fixture.ConfigureSubjectNameSigning(azureArtifactSigning: true, includeDlls: true);
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.SignThumbprint = null;
        request.SignSubjectName = "CN=Publisher";
        request.SignaturePaths = new[] { fixture.ExecutablePath, dependency };

        PowerForgeReleaseArtifactEvidence result = fixture
            .CreateVerifier(
                Thumbprint,
                VendorThumbprint,
                path => path.EndsWith("Dependency.dll", StringComparison.OrdinalIgnoreCase)
                    ? VendorThumbprint
                    : Thumbprint)
            .Verify(request);

        Assert.Equal("valid", result.SignatureStatus);
        Assert.Equal(2, result.Signatures.Length);
        Assert.Empty(result.SignerThumbprint);
        Assert.Equal(
            new[] { Thumbprint, VendorThumbprint },
            result.Signatures.Select(signature => signature.Thumbprint).OrderBy(value => value, StringComparer.Ordinal));
    }

    [Fact]
    public void Verify_PortableCliRejectsUntrustedInventoryCertificateDuringAzureRotation()
    {
        using var fixture = new PortableFixture();
        string dependency = fixture.AddSignedDependency();
        fixture.EnableDllSigning(dependency);
        fixture.ConfigureSubjectNameSigning(azureArtifactSigning: true, includeDlls: true);
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.SignThumbprint = null;
        request.SignSubjectName = "CN=Publisher";
        request.SignaturePaths = new[] { fixture.ExecutablePath, dependency };

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => fixture
            .CreateVerifier(
                Thumbprint,
                VendorThumbprint,
                path => path.EndsWith("Dependency.dll", StringComparison.OrdinalIgnoreCase)
                    ? VendorThumbprint
                    : Thumbprint,
                inventoryCertificateTrusted: false)
            .Verify(request));

        Assert.Contains("trusted code-signing certificate chain", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliRejectsTrustedArchiveInventoryFromDifferentPublisherDuringAzureRotation()
    {
        using var fixture = new PortableFixture();
        fixture.ConfigureSubjectNameSigning(azureArtifactSigning: true);
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.SignThumbprint = null;
        request.SignSubjectName = "CN=Publisher";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => fixture
            .CreateVerifier(
                Thumbprint,
                VendorThumbprint,
                inventorySignerSubject: "CN=Other Publisher")
            .Verify(request));

        Assert.Contains("does not match", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("publisher", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateRotatedInventoryPublisher_RejectsTrustedDifferentSubject()
    {
        var inventorySigner = new PowerForgePayloadInventorySignature(
            "CN=Other Publisher",
            VendorThumbprint,
            certificateTrusted: true);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            PowerForgeReleaseArtifactVerifier.ValidateRotatedInventoryPublisher(
                inventorySigner,
                "CN=Publisher",
                "Portable payload inventory"));

        Assert.Contains("does not match the Authenticode publisher subject", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliRebasesRelocatedManifestArtifactPathsFromChecksums()
    {
        using var fixture = new PortableFixture();
        fixture.WriteRelocatedManifestPaths();
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.ArtifactPath = string.Empty;

        PowerForgeReleaseArtifactEvidence result = fixture.CreateVerifier().Verify(request);

        Assert.Equal(Path.GetFullPath(fixture.ArchivePath), result.ArtifactPath);
    }

    [Fact]
    public void Verify_PortableCliRebasesMissingRelativeManifestArtifactPathsFromChecksums()
    {
        using var fixture = new PortableFixture();
        fixture.WriteMissingRelativeManifestPaths();
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.ArtifactPath = string.Empty;

        PowerForgeReleaseArtifactEvidence result = fixture.CreateVerifier().Verify(request);

        Assert.Equal(Path.GetFullPath(fixture.ArchivePath), result.ArtifactPath);
    }

    [Fact]
    public void Verify_PortableCliRejectsRelocatedDirectExecutableFromDifferentMatrixEntry()
    {
        using var fixture = new PortableFixture();
        fixture.WriteRelocatedDirectExecutableForDifferentMatrixEntry();
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.ArtifactPath = string.Empty;
        request.SignaturePaths = Array.Empty<string>();
        request.SbomPaths = Array.Empty<string>();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(request));

        Assert.Contains("runtime, framework, and style", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliRecoversMatrixNamedDirectExecutableFromCustomOutputPath()
    {
        using var fixture = new PortableFixture();
        fixture.ConfigureDirectPackaging();
        string relocatedExecutable = fixture.WriteRelocatedMatrixNamedDirectExecutableFromCustomOutputPath();
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.ArtifactPath = string.Empty;
        request.SignaturePaths = Array.Empty<string>();
        request.SbomPaths = Array.Empty<string>();

        PowerForgeReleaseArtifactEvidence result = fixture.CreateVerifier().Verify(request);

        Assert.Equal(Path.GetFullPath(relocatedExecutable), result.ArtifactPath);
    }

    [Fact]
    public void Verify_PortableCliRecoversMatrixNamedDirectBundleFromCustomOutputPath()
    {
        using var fixture = new PortableFixture();
        const string bundleId = "package";
        fixture.ConfigureBundle(bundleId, bundleZip: false);
        string relocatedExecutable = fixture.WriteRelocatedMatrixNamedDirectBundleFromCustomOutputPath(bundleId);
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.BundleId = bundleId;
        request.ArtifactPath = string.Empty;
        request.SignaturePaths = Array.Empty<string>();
        request.SbomPaths = Array.Empty<string>();

        PowerForgeReleaseArtifactEvidence result = fixture.CreateVerifier().Verify(request);

        Assert.Equal(Path.GetFullPath(relocatedExecutable), result.ArtifactPath);
    }

    [Fact]
    public void Verify_PortableCliRejectsExplicitDirectExecutableFromDifferentMatrixEntry()
    {
        using var fixture = new PortableFixture();
        fixture.ConfigureDirectPackaging();
        string differentMatrixExecutable = fixture.WriteRelocatedDirectExecutableForDifferentMatrixEntry();
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.ArtifactPath = differentMatrixExecutable;
        request.SignaturePaths = Array.Empty<string>();
        request.SbomPaths = Array.Empty<string>();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(request));

        Assert.Contains("release-asset identity", exception.Message, StringComparison.OrdinalIgnoreCase);
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
        internal void WriteRelocatedManifestPaths()
        {
            string retiredRoot = Path.Combine(Path.GetPathRoot(Root)!, "retired-runner", Guid.NewGuid().ToString("N"));
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
                    OutputDir = Path.Combine(retiredRoot, "Artifacts", "Sample.CLI"),
                    ZipPath = Path.Combine(retiredRoot, Path.GetRelativePath(Root, ArchivePath)),
                    ExePath = Path.Combine(retiredRoot, Path.GetRelativePath(Root, ExecutablePath)),
                    SignedFiles = 1,
                    SignedFilePaths = new[] { Path.Combine(retiredRoot, Path.GetRelativePath(Root, ExecutablePath)) },
                    SourceRevision,
                    SourceDirty = false
                }
            }));
            WriteChecksums();
        }

        internal void WriteMissingRelativeManifestPaths()
        {
            string missingRoot = Path.Combine("retired-runner", Guid.NewGuid().ToString("N"));
            string missingDirectory = Path.Combine(
                missingRoot,
                "Artifacts",
                "Sample.CLI",
                "win-x64",
                "net10.0",
                "PortableCompat");
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
                    OutputDir = missingDirectory,
                    ZipPath = Path.Combine(missingRoot, Path.GetFileName(ArchivePath)),
                    ExePath = Path.Combine(missingDirectory, Path.GetFileName(ExecutablePath)),
                    SignedFiles = 1,
                    SignedFilePaths = new[] { Path.Combine(missingDirectory, Path.GetFileName(ExecutablePath)) },
                    SourceRevision,
                    SourceDirty = false
                }
            }));
            WriteChecksums();
        }

        internal string WriteRelocatedDirectExecutableForDifferentMatrixEntry()
        {
            string differentMatrixDirectory = Directory.CreateDirectory(Path.Combine(
                Root,
                "Artifacts",
                "Sample.CLI",
                "linux-x64",
                "net10.0",
                "PortableCompat")).FullName;
            string differentMatrixExecutable = Path.Combine(differentMatrixDirectory, Path.GetFileName(ExecutablePath));
            File.Copy(ExecutablePath, differentMatrixExecutable, overwrite: true);
            File.Delete(ExecutablePath);
            string retiredExecutable = Path.Combine(
                "retired-runner",
                "win-x64",
                "net10.0",
                "PortableCompat",
                Path.GetFileName(ExecutablePath));
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
                    OutputDir = Path.GetDirectoryName(retiredExecutable)!,
                    ZipPath = string.Empty,
                    ExePath = retiredExecutable,
                    SignedFiles = 1,
                    SignedFilePaths = new[] { retiredExecutable },
                    SourceRevision,
                    SourceDirty = false
                },
                new
                {
                    Category = "Publish",
                    Target = "Sample.CLI",
                    Kind = "Cli",
                    Runtime = "linux-x64",
                    Framework = "net10.0",
                    Style = "PortableCompat",
                    OutputDir = differentMatrixDirectory,
                    ZipPath = string.Empty,
                    ExePath = differentMatrixExecutable,
                    SignedFiles = 1,
                    SignedFilePaths = new[] { differentMatrixExecutable },
                    SourceRevision,
                    SourceDirty = false
                }
            }));
            base.WriteChecksums(ManifestPath, ConfigurationPath, differentMatrixExecutable);
            return differentMatrixExecutable;
        }

        internal string WriteRelocatedMatrixNamedDirectExecutableFromCustomOutputPath()
        {
            string aliasName = DotNetPublishReleaseAssetNaming.CreateDirectMatrixAssetName(
                "Sample.CLI",
                "net10.0",
                "win-x64",
                "PortableCompat",
                DotNetPublishArtefactCategory.Publish,
                bundleId: null,
                ExecutablePath);
            string relocatedDirectory = Directory.CreateDirectory(Path.Combine(Root, "release-assets")).FullName;
            string relocatedExecutable = Path.Combine(relocatedDirectory, aliasName);
            File.Copy(ExecutablePath, relocatedExecutable, overwrite: true);
            string relocatedInventory = relocatedExecutable + PowerForgePortablePayloadInventory.DirectInventorySuffix;
            string relocatedSignature = relocatedExecutable + PowerForgePortablePayloadInventory.DirectSignatureSuffix;
            File.Copy(DirectInventoryPath, relocatedInventory, overwrite: true);
            File.Copy(DirectSignaturePath, relocatedSignature, overwrite: true);
            File.Delete(ExecutablePath);
            string retiredExecutable = Path.Combine("custom-output", Path.GetFileName(ExecutablePath));
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
                    OutputDir = Path.GetDirectoryName(retiredExecutable)!,
                    ZipPath = string.Empty,
                    ExePath = retiredExecutable,
                    SignedFiles = 1,
                    SignedFilePaths = new[] { retiredExecutable },
                    SourceRevision,
                    SourceDirty = false
                }
            }));
            base.WriteChecksums(
                ManifestPath,
                ConfigurationPath,
                relocatedExecutable,
                relocatedInventory,
                relocatedSignature);
            return relocatedExecutable;
        }

        internal string WriteRelocatedMatrixNamedDirectBundleFromCustomOutputPath(string bundleId)
        {
            WriteDirectInventory(bundleId: bundleId);
            string aliasName = DotNetPublishReleaseAssetNaming.CreateDirectMatrixAssetName(
                "Sample.CLI",
                "net10.0",
                "win-x64",
                "PortableCompat",
                DotNetPublishArtefactCategory.Bundle,
                bundleId,
                ExecutablePath);
            string relocatedDirectory = Directory.CreateDirectory(Path.Combine(Root, "release-assets")).FullName;
            string relocatedExecutable = Path.Combine(relocatedDirectory, aliasName);
            File.Copy(ExecutablePath, relocatedExecutable, overwrite: true);
            string relocatedInventory = relocatedExecutable + PowerForgePortablePayloadInventory.DirectInventorySuffix;
            string relocatedSignature = relocatedExecutable + PowerForgePortablePayloadInventory.DirectSignatureSuffix;
            File.Copy(DirectInventoryPath, relocatedInventory, overwrite: true);
            File.Copy(DirectSignaturePath, relocatedSignature, overwrite: true);
            File.Delete(ExecutablePath);
            string retiredExecutable = Path.Combine("custom-output", Path.GetFileName(ExecutablePath));
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
                    OutputDir = Path.GetDirectoryName(retiredExecutable)!,
                    ZipPath = string.Empty,
                    ExePath = retiredExecutable,
                    SignedFiles = 1,
                    SignedFilePaths = new[] { retiredExecutable },
                    SourceRevision,
                    SourceDirty = false
                }
            }));
            base.WriteChecksums(
                ManifestPath,
                ConfigurationPath,
                relocatedExecutable,
                relocatedInventory,
                relocatedSignature);
            return relocatedExecutable;
        }

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

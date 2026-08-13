namespace PowerForge.Tests;

public sealed class PowerForgeModuleSigningEvidenceWriterTests
{
    private const string SourceRevision = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void Create_CompleteSuccessfulSigningResultReturnsNormalizedEvidence()
    {
        using var fixture = new SigningFixture();
        var signingResult = new ModuleSigningResult
        {
            TotalMatched = 3,
            TotalAfterExclude = 3,
            SignedNew = 3,
            VerifiedFilePaths = new[] { fixture.ModulePath, fixture.ManifestPath, fixture.SourceAttestationPath }
        };
        fixture.BindSigningInventory(signingResult);

        PowerForgeModuleSigningEvidence evidence = PowerForgeModuleSigningEvidenceWriter.Create(
            fixture.Root,
            "Sample",
            "2.3.4",
            SourceRevision,
            sourceDirty: false,
            fixture.ManifestPath,
            signingResult);

        Assert.Equal("Sample/Sample.psd1", evidence.ManifestPath);
        Assert.Equal(
            new[] { "Sample/PowerForge.ReleaseProvenance.psd1", "Sample/Sample.psd1", "Sample/Sample.psm1" },
            evidence.SignableFiles);
        Assert.Equal(SourceRevision, evidence.SourceRevision);
        Assert.False(evidence.SourceDirty ?? true);
        Assert.Equal(3, evidence.SchemaVersion);
        Assert.Equal(64, evidence.SigningInventorySha256.Length);
    }

    [Fact]
    public void WriteFromSignedSourceAttestation_UsesSignedIdentityAndWritesSidecar()
    {
        using var fixture = new SigningFixture();
        string outputPath = Path.Combine(fixture.Root, "Sample.zip.signing.json");
        var signingResult = new ModuleSigningResult
        {
            TotalMatched = 3,
            TotalAfterExclude = 3,
            SignedNew = 3,
            VerifiedFilePaths = new[] { fixture.ModulePath, fixture.ManifestPath, fixture.SourceAttestationPath }
        };
        fixture.BindSigningInventory(signingResult);

        string written = PowerForgeModuleSigningEvidenceWriter.WriteFromSignedSourceAttestation(
            outputPath,
            fixture.Root,
            "Sample",
            "2.3.4",
            fixture.ManifestPath,
            signingResult);

        Assert.Equal(Path.GetFullPath(outputPath), written);
        Assert.Contains(SourceRevision, File.ReadAllText(written), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_PreservedThirdPartySignatureCarriesNormalizedIdentity()
    {
        using var fixture = new SigningFixture(includeVendorDependency: true);
        var signingResult = new ModuleSigningResult
        {
            TotalMatched = 4,
            TotalAfterExclude = 4,
            SignedNew = 3,
            AlreadySignedOther = 1,
            VerifiedFilePaths = new[] { fixture.ManifestPath, fixture.ModulePath, fixture.SourceAttestationPath, fixture.VendorPath! },
            PreservedThirdPartySignatures = new[]
            {
                new ModuleSigningPreservedSignature
                {
                    FilePath = fixture.VendorPath!,
                    Subject = "CN=Vendor",
                    Thumbprint = "cc cc cc cc cc cc cc cc cc cc cc cc cc cc cc cc cc cc cc cc"
                }
            }
        };
        fixture.BindSigningInventory(signingResult);

        PowerForgeModuleSigningEvidence evidence = PowerForgeModuleSigningEvidenceWriter.Create(
            fixture.Root,
            "Sample",
            "2.3.4",
            SourceRevision,
            sourceDirty: false,
            fixture.ManifestPath,
            signingResult);

        PowerForgeModulePreservedSignature preserved = Assert.Single(evidence.PreservedThirdPartySignatures);
        Assert.Equal("Sample/lib/Vendor.dll", preserved.Path);
        Assert.Equal("CN=Vendor", preserved.Subject);
        Assert.Equal("CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC", preserved.Thumbprint);
    }

    [Fact]
    public void Create_IncompleteVerifiedFileSetFailsClosed()
    {
        using var fixture = new SigningFixture();
        var signingResult = new ModuleSigningResult
        {
            TotalMatched = 3,
            TotalAfterExclude = 3,
            SignedNew = 3,
            VerifiedFilePaths = new[] { fixture.ManifestPath }
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeModuleSigningEvidenceWriter.Create(
                fixture.Root,
                "Sample",
                "2.3.4",
                SourceRevision,
                sourceDirty: false,
                fixture.ManifestPath,
                signingResult));

        Assert.Contains("every file selected", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_PolicyExcludedDependencyIsNotAddedToSigningEvidence()
    {
        using var fixture = new SigningFixture(includeVendorDependency: true);
        var signingResult = new ModuleSigningResult
        {
            TotalMatched = 3,
            TotalAfterExclude = 3,
            SignedNew = 3,
            VerifiedFilePaths = new[] { fixture.ManifestPath, fixture.ModulePath, fixture.SourceAttestationPath }
        };
        fixture.BindSigningInventory(signingResult);

        PowerForgeModuleSigningEvidence evidence = PowerForgeModuleSigningEvidenceWriter.Create(
            fixture.Root,
            "Sample",
            "2.3.4",
            SourceRevision,
            sourceDirty: false,
            fixture.ManifestPath,
            signingResult);

        Assert.DoesNotContain(evidence.SignableFiles, path => path.EndsWith("Vendor.dll", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Create_DirtySourceFailsClosed()
    {
        using var fixture = new SigningFixture();
        var signingResult = new ModuleSigningResult
        {
            TotalMatched = 3,
            TotalAfterExclude = 3,
            SignedNew = 3,
            VerifiedFilePaths = new[] { fixture.ManifestPath, fixture.ModulePath, fixture.SourceAttestationPath }
        };
        fixture.BindSigningInventory(signingResult);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeModuleSigningEvidenceWriter.Create(
                fixture.Root,
                "Sample",
                "2.3.4",
                SourceRevision,
                sourceDirty: true,
                fixture.ManifestPath,
                signingResult));

        Assert.Contains("dirty source", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_BenignRootModuleDotSegmentUsesCanonicalPackedPath()
    {
        using var fixture = new SigningFixture();
        File.WriteAllText(fixture.ManifestPath, "@{ ModuleVersion = '2.3.4'; RootModule = './Sample.psm1' }");
        var signingResult = new ModuleSigningResult
        {
            TotalMatched = 3,
            TotalAfterExclude = 3,
            SignedNew = 3,
            VerifiedFilePaths = new[] { fixture.ManifestPath, fixture.ModulePath, fixture.SourceAttestationPath }
        };
        fixture.BindSigningInventory(signingResult);

        PowerForgeModuleSigningEvidence evidence = PowerForgeModuleSigningEvidenceWriter.Create(
            fixture.Root,
            "Sample",
            "2.3.4",
            SourceRevision,
            sourceDirty: false,
            fixture.ManifestPath,
            signingResult);

        Assert.Contains("Sample/Sample.psm1", evidence.SignableFiles);
    }

    [Fact]
    public void Create_EscapingRootModuleFailsClosed()
    {
        using var fixture = new SigningFixture();
        File.WriteAllText(fixture.ManifestPath, "@{ ModuleVersion = '2.3.4'; RootModule = '../Sample.psm1' }");
        var signingResult = new ModuleSigningResult
        {
            TotalMatched = 3,
            TotalAfterExclude = 3,
            SignedNew = 3,
            VerifiedFilePaths = new[] { fixture.ManifestPath, fixture.ModulePath, fixture.SourceAttestationPath }
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeModuleSigningEvidenceWriter.Create(
                fixture.Root,
                "Sample",
                "2.3.4",
                SourceRevision,
                sourceDirty: false,
                fixture.ManifestPath,
                signingResult));

        Assert.Contains("must stay under", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SigningFixture : IDisposable
    {
        public SigningFixture(bool includeVendorDependency = false)
        {
            Root = Path.Combine(Path.GetTempPath(), "PowerForgeSigningEvidence-" + Guid.NewGuid().ToString("N"));
            string moduleRoot = Path.Combine(Root, "Sample");
            Directory.CreateDirectory(moduleRoot);
            ManifestPath = Path.Combine(moduleRoot, "Sample.psd1");
            ModulePath = Path.Combine(moduleRoot, "Sample.psm1");
            VendorPath = includeVendorDependency ? Path.Combine(moduleRoot, "lib", "Vendor.dll") : null;
            File.WriteAllText(ManifestPath, "@{ ModuleVersion = '2.3.4'; RootModule = 'Sample.psm1' }");
            File.WriteAllText(ModulePath, "function Get-Sample { 'ok' }");
            SourceAttestationPath = PowerForgeModuleSourceAttestationWriter.Write(
                ManifestPath,
                "Sample",
                "2.3.4",
                SourceRevision,
                sourceDirty: false);
            if (VendorPath is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(VendorPath)!);
                File.WriteAllText(VendorPath, "vendor");
            }
        }

        public string Root { get; }

        public string ManifestPath { get; }

        public string ModulePath { get; }

        public string SourceAttestationPath { get; }

        public string? VendorPath { get; }

        public void BindSigningInventory(ModuleSigningResult signingResult)
        {
            PowerForgeModuleSourceAttestationWriter.BindSigningInventory(
                ManifestPath,
                Root,
                signingResult);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}

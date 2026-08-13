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
            TotalMatched = 2,
            TotalAfterExclude = 2,
            SignedNew = 2,
            VerifiedFilePaths = new[] { fixture.ModulePath, fixture.ManifestPath }
        };

        PowerForgeModuleSigningEvidence evidence = PowerForgeModuleSigningEvidenceWriter.Create(
            fixture.Root,
            "Sample",
            "2.3.4",
            SourceRevision,
            sourceDirty: false,
            fixture.ManifestPath,
            signingResult);

        Assert.Equal("Sample/Sample.psd1", evidence.ManifestPath);
        Assert.Equal(new[] { "Sample/Sample.psd1", "Sample/Sample.psm1" }, evidence.SignableFiles);
        Assert.Equal(SourceRevision, evidence.SourceRevision);
        Assert.False(evidence.SourceDirty ?? true);
    }

    [Fact]
    public void Create_PreservedThirdPartySignatureCarriesNormalizedIdentity()
    {
        using var fixture = new SigningFixture(includeVendorDependency: true);
        var signingResult = new ModuleSigningResult
        {
            TotalMatched = 3,
            TotalAfterExclude = 3,
            SignedNew = 2,
            AlreadySignedOther = 1,
            VerifiedFilePaths = new[] { fixture.ManifestPath, fixture.ModulePath, fixture.VendorPath! },
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
            TotalMatched = 2,
            TotalAfterExclude = 2,
            SignedNew = 2,
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
    public void Create_BundledRequiredModuleMissingFromSigningResultFailsClosed()
    {
        using var fixture = new SigningFixture(includeVendorDependency: true);
        var signingResult = new ModuleSigningResult
        {
            TotalMatched = 2,
            TotalAfterExclude = 2,
            SignedNew = 2,
            VerifiedFilePaths = new[] { fixture.ManifestPath, fixture.ModulePath }
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

        Assert.Contains("bundled required modules", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_DirtySourceFailsClosed()
    {
        using var fixture = new SigningFixture();
        var signingResult = new ModuleSigningResult
        {
            TotalMatched = 2,
            TotalAfterExclude = 2,
            SignedNew = 2,
            VerifiedFilePaths = new[] { fixture.ManifestPath, fixture.ModulePath }
        };

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
            if (VendorPath is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(VendorPath)!);
                File.WriteAllText(VendorPath, "vendor");
            }
        }

        public string Root { get; }

        public string ManifestPath { get; }

        public string ModulePath { get; }

        public string? VendorPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}

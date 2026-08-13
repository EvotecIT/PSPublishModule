using System.Text;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseArtifactVerifierTests
{
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
    public void Verify_PowerShellModuleRejectsFileDirectoryCaseCollision()
    {
        using var fixture = new ModuleFixture();
        fixture.WriteArchive(SourceRevision, directoryCollision: true);
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
    public void Verify_PowerShellModuleAllowsDependencyProvenanceWithoutSelectingIt()
    {
        using var fixture = new ModuleFixture();
        fixture.WriteArchive(SourceRevision, includeDependencyProvenance: true);
        fixture.WriteChecksums();

        PowerForgeReleaseArtifactEvidence evidence = fixture.CreateVerifier().Verify(fixture.CreateRequest());

        Assert.Contains(evidence.EvidenceFiles, item =>
            item.Role == "provenance" &&
            item.Path.EndsWith("!Sample/PowerForge.ReleaseProvenance.json", StringComparison.Ordinal));
    }

    [Fact]
    public void Verify_PowerShellModuleDoesNotSubstituteDependencyProvenanceForPrimaryModule()
    {
        using var fixture = new ModuleFixture();
        fixture.WriteArchive(SourceRevision, includeDependencyProvenance: true, omitPrimaryProvenance: true);
        fixture.WriteChecksums();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("beside its manifest", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PowerShellModuleRegeneratedCatalogCannotRebindSignedPayload()
    {
        using var fixture = new ModuleFixture();
        fixture.WriteArchive(SourceRevision, signedSourceRevision: new string('c', 40));
        fixture.WriteChecksums();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("source provenance", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

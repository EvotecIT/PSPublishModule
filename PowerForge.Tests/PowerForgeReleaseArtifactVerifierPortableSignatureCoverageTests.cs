namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseArtifactVerifierTests
{
    [Fact]
    public void Verify_PortableCliRejectsRequestedSignatureSetThatDiffersFromPublisherInventory()
    {
        using var fixture = new PortableFixture();
        string dependencyPath = fixture.AddSignedDependency();
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.SignaturePaths = new[] { dependencyPath };

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(request));

        Assert.Contains("requested portable signature paths", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliVerifiesEveryConfiguredSignedFileWithoutRequestOverrides()
    {
        using var fixture = new PortableFixture();
        string dependencyPath = fixture.AddSignedDependency();
        fixture.EnableDllSigning(dependencyPath);
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.SignaturePaths = Array.Empty<string>();

        PowerForgeReleaseArtifactEvidence evidence = fixture.CreateVerifier().Verify(request);

        Assert.Equal(2, evidence.Signatures.Length);
        Assert.Contains(evidence.SignaturePaths, path => path.EndsWith("Dependency.dll", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Verify_PortableCliAcceptsRequestedSignatureSetMatchingPublisherInventory()
    {
        using var fixture = new PortableFixture();
        string dependencyPath = fixture.AddSignedDependency();
        fixture.EnableDllSigning(dependencyPath);
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.SignaturePaths = new[] { dependencyPath, fixture.ExecutablePath };

        PowerForgeReleaseArtifactEvidence evidence = fixture.CreateVerifier().Verify(request);

        Assert.Equal(2, evidence.Signatures.Length);
    }

    [Fact]
    public void Verify_PortableCliRejectsSignedFileCountThatDiffersFromConfiguredSelection()
    {
        using var fixture = new PortableFixture();
        string dependencyPath = fixture.AddSignedDependency();
        fixture.EnableDllSigning(dependencyPath, signedFileCount: 1);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("signed-file count", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliRejectsManifestThatOmitsConfiguredSignedFile()
    {
        using var fixture = new PortableFixture();
        string dependencyPath = fixture.AddSignedDependency();
        fixture.EnableDllSigning(dependencyPath, omitDependencyFromManifest: true);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("signed-file count", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliRejectsDllOmittedFromConfiguredSigningCoverage()
    {
        using var fixture = new PortableFixture();
        string dependencyPath = fixture.AddSignedDependency();
        fixture.EnableDllSigning(dependencyPath, signedFileCount: 1, omitDependencyFromManifest: true);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("every required executable or DLL", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliRejectsInvalidSignatureOnAdditionalConfiguredFile()
    {
        using var fixture = new PortableFixture();
        string dependencyPath = fixture.AddSignedDependency();
        fixture.EnableDllSigning(dependencyPath);
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.SignaturePaths = Array.Empty<string>();
        PowerForgeReleaseArtifactVerifier verifier = new(
            path => path.EndsWith("Dependency.dll", StringComparison.OrdinalIgnoreCase)
                ? new DotNetPublishReleaseArtifactVerifier.AuthenticodeResult(false, unchecked((int)0x800B0100), string.Empty, string.Empty)
                : new DotNetPublishReleaseArtifactVerifier.AuthenticodeResult(true, 0, "CN=Publisher", Thumbprint),
            _ => "1.2.3+" + SourceRevision,
            _ => "Sample.CLI",
            (_, _) => new PowerForgePayloadInventorySignature("CN=Publisher", Thumbprint));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            verifier.Verify(request));

        Assert.Contains("signature is not valid", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliAcceptsDirectExecutableFromMultiFileSigningOutput()
    {
        using var fixture = new PortableFixture();
        string dependencyPath = fixture.AddSignedDependency();
        fixture.EnableDllSigning(dependencyPath, zip: false);
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.ArtifactPath = fixture.ExecutablePath;
        request.SignaturePaths = Array.Empty<string>();
        request.SbomPaths = Array.Empty<string>();

        PowerForgeReleaseArtifactEvidence evidence = fixture.CreateVerifier().Verify(request);

        Assert.Equal("valid", evidence.SignatureStatus);
        Assert.Single(evidence.Signatures);
    }

    [Fact]
    public void Verify_PortableCliAcceptsDirectExecutableAsRequestedSignaturePath()
    {
        using var fixture = new PortableFixture();
        fixture.ConfigureDirectPackaging();
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.ArtifactPath = fixture.ExecutablePath;
        request.SignaturePaths = new[] { fixture.ExecutablePath };
        request.SbomPaths = Array.Empty<string>();

        PowerForgeReleaseArtifactEvidence evidence = fixture.CreateVerifier().Verify(request);

        Assert.Equal("valid", evidence.SignatureStatus);
        Assert.Single(evidence.Signatures);
    }

    [Fact]
    public void Verify_PortableCliRejectsRequestedSignaturePathOtherThanDirectExecutable()
    {
        using var fixture = new PortableFixture();
        fixture.ConfigureDirectPackaging();
        string unrelatedPath = Path.Combine(fixture.Root, "unrelated.exe");
        File.WriteAllText(unrelatedPath, "unrelated signed file");
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.ArtifactPath = fixture.ExecutablePath;
        request.SignaturePaths = new[] { unrelatedPath };
        request.SbomPaths = Array.Empty<string>();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(request));

        Assert.Contains("verified direct executable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

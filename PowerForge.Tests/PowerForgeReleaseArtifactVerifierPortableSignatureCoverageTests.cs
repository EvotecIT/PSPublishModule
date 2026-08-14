namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseArtifactVerifierTests
{
    [Fact]
    public void Verify_PortableCliUsesPublisherSignedSelectionInsteadOfRequestOverrides()
    {
        using var fixture = new PortableFixture();
        string dependencyPath = fixture.AddSignedDependency();
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.SignaturePaths = new[] { dependencyPath };

        PowerForgeReleaseArtifactEvidence evidence = fixture.CreateVerifier().Verify(request);

        Assert.Single(evidence.Signatures);
        Assert.Contains(evidence.SignaturePaths, path => path.EndsWith("Sample.CLI.exe", StringComparison.OrdinalIgnoreCase));
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
    public void Verify_PortableCliRejectsMultiFileSelectionForDirectExecutableArtifact()
    {
        using var fixture = new PortableFixture();
        string dependencyPath = fixture.AddSignedDependency();
        fixture.EnableDllSigning(dependencyPath);
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.ArtifactPath = fixture.ExecutablePath;
        request.SignaturePaths = Array.Empty<string>();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(request));

        Assert.Contains("direct portable executable", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ZIP", exception.Message, StringComparison.Ordinal);
    }
}

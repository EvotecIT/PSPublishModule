namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseArtifactVerifierTests
{
    [Fact]
    public void Verify_PortableCliRequiresExactPublisherSubject()
    {
        using var fixture = new PortableFixture();
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.SignThumbprint = null;
        request.SignSubjectName = "CN=Publisher";
        PowerForgeReleaseArtifactVerifier verifier = new(
            _ => new DotNetPublishReleaseArtifactVerifier.AuthenticodeResult(
                true,
                0,
                "CN=Publisher Malware LLC",
                new string('D', 40)),
            _ => "1.2.3+" + SourceRevision,
            _ => "Sample.CLI",
            (_, _) => new PowerForgePayloadInventorySignature("CN=Publisher Malware LLC", new string('D', 40)));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => verifier.Verify(request));

        Assert.Contains("certificate subject", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliAcceptsExactPublisherSubject()
    {
        using var fixture = new PortableFixture();
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.SignThumbprint = null;
        request.SignSubjectName = "CN=Publisher";

        PowerForgeReleaseArtifactEvidence evidence = fixture.CreateVerifier().Verify(request);

        Assert.Equal("CN=Publisher", evidence.SignerSubject);
    }

    [Fact]
    public void Verify_PortableCliAcceptsExactSimplePublisherCommonName()
    {
        using var fixture = new PortableFixture();
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.SignThumbprint = null;
        request.SignSubjectName = "Publisher";

        PowerForgeReleaseArtifactEvidence evidence = fixture.CreateVerifier().Verify(request);

        Assert.Equal("CN=Publisher", evidence.SignerSubject);
    }

    [Fact]
    public void Verify_PortableCliRejectsPartialSimplePublisherCommonName()
    {
        using var fixture = new PortableFixture();
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.SignThumbprint = null;
        request.SignSubjectName = "Publish";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(request));

        Assert.Contains("certificate subject", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliRejectsReplaceableConfigurationAsPublisherTrust()
    {
        using var fixture = new PortableFixture();
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.SignThumbprint = null;
        request.SignSubjectName = null;

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(request));

        Assert.Contains("out-of-band publisher", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("configuration cannot establish", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliRejectsArtifactIdThatDiffersFromSelectedTarget()
    {
        using var fixture = new PortableFixture();
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.Target = "Other.CLI";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(request));

        Assert.Contains("artifact ID must match", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliPreservesPrereleaseIdentityAndIgnoresBuildMetadata()
    {
        using var fixture = new PortableFixture();
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.ExpectedVersion = "1.2.3-preview.1+expected";
        fixture.SetPortableVersion("1.2.3-preview.1+inventory");
        fixture.WriteBoundCycloneDxSbom("Sample.CLI", "1.2.3-preview.1", fixture.ComputeDigest(fixture.ArchivePath));
        fixture.WriteChecksums();
        PowerForgeReleaseArtifactVerifier verifier = new(
            _ => new DotNetPublishReleaseArtifactVerifier.AuthenticodeResult(true, 0, "CN=Publisher", Thumbprint),
            _ => "1.2.3-preview.1+actual." + SourceRevision,
            _ => "Sample.CLI",
            (_, _) => new PowerForgePayloadInventorySignature("CN=Publisher", Thumbprint));

        PowerForgeReleaseArtifactEvidence evidence = verifier.Verify(request);

        Assert.Equal("1.2.3-preview.1", evidence.Version);
    }

    [Fact]
    public void Verify_PortableCliDoesNotAdmitPrereleaseAsStableVersion()
    {
        using var fixture = new PortableFixture();
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.ExpectedVersion = "1.2.3";
        PowerForgeReleaseArtifactVerifier verifier = new(
            _ => new DotNetPublishReleaseArtifactVerifier.AuthenticodeResult(true, 0, "CN=Publisher", Thumbprint),
            _ => "1.2.3-preview.1+sha." + SourceRevision,
            _ => "Sample.CLI",
            (_, _) => new PowerForgePayloadInventorySignature("CN=Publisher", Thumbprint));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => verifier.Verify(request));

        Assert.Contains("does not match expected version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliRequiresFullExpectedSourceRevision()
    {
        using var fixture = new PortableFixture();
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.ExpectedSourceRevision = SourceRevision.Substring(0, 12);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(request));

        Assert.Contains("full valid expected source revision", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PortableCliRegeneratedCatalogCannotRebindSignedPayload()
    {
        using var fixture = new PortableFixture();
        PowerForgeReleaseArtifactVerifier verifier = new(
            _ => new DotNetPublishReleaseArtifactVerifier.AuthenticodeResult(true, 0, "CN=Publisher", Thumbprint),
            _ => "1.2.3+" + new string('c', 40),
            _ => "Sample.CLI",
            (_, _) => new PowerForgePayloadInventorySignature("CN=Publisher", Thumbprint));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            verifier.Verify(fixture.CreateRequest()));

        Assert.Contains("publisher-signed portable product version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DefaultVerifier_DeclaresWindowsOnlyAuthenticodeBoundary()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.NotNull(new PowerForgeReleaseArtifactVerifier());
            return;
        }

        PlatformNotSupportedException exception = Assert.Throws<PlatformNotSupportedException>(
            () => new PowerForgeReleaseArtifactVerifier());
        Assert.Contains("Windows", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PowerShellModuleRequiresExactPublisherSubject()
    {
        using var fixture = new ModuleFixture();
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.SignThumbprint = null;
        request.SignSubjectName = "CN=Publisher";
        PowerForgeReleaseArtifactVerifier verifier = new(
            _ => new DotNetPublishReleaseArtifactVerifier.AuthenticodeResult(
                true,
                0,
                "CN=Publisher Malware LLC",
                new string('D', 40)),
            _ => "1.2.3+" + SourceRevision);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => verifier.Verify(request));

        Assert.Contains("certificate subject", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PowerShellModuleRejectsArtifactIdThatDiffersFromSelectedTarget()
    {
        using var fixture = new ModuleFixture();
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.Target = "Other";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(request));

        Assert.Contains("artifact ID must match", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_PowerShellModuleRequiresFullExpectedSourceRevision()
    {
        using var fixture = new ModuleFixture();
        PowerForgeReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.ExpectedSourceRevision = SourceRevision.Substring(0, 12);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(request));

        Assert.Contains("full valid expected source revision", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed partial class DotNetPublishReleaseArtifactVerifierTests
{
    [Fact]
    public void Verify_RejectsReplaceableConfigurationAsPublisherTrust()
    {
        using var fixture = new ReleaseFixture();
        DotNetPublishReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.SignThumbprint = null;
        request.SignSubjectName = null;

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(request));

        Assert.Contains("out-of-band publisher", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("configuration cannot establish", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_RequiresExactTrustedPublisherSubject()
    {
        using var fixture = new ReleaseFixture();
        DotNetPublishReleaseArtifactVerificationRequest request = fixture.CreateRequest();
        request.SignSubjectName = "CN=Test Publisher";
        request.SignThumbprint = null;
        DotNetPublishReleaseArtifactVerifier verifier = new(
            _ => fixture.ReadPackageMetadata(),
            _ => new DotNetPublishReleaseArtifactVerifier.AuthenticodeResult(
                true,
                0,
                "CN=Test Publisher Malware LLC",
                new string('B', 40)));

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => verifier.Verify(request));

        Assert.Contains("certificate subject", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

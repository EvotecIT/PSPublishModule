namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseArtifactVerifierTests
{
    [Fact]
    public void Verify_PortableCliRejectsArchiveWhoseSignedPayloadDiffersFromChecksummedOutput()
    {
        using var fixture = new PortableFixture();
        fixture.WriteArchive("different signed payload");
        fixture.WriteChecksums();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            fixture.CreateVerifier().Verify(fixture.CreateRequest()));

        Assert.Contains("different bytes", exception.Message, StringComparison.OrdinalIgnoreCase);
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
}

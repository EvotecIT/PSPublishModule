namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseArtifactVerifierTests
{
    [Fact]
    public void Verify_PortableCliAcceptsGnuTextModeChecksumCatalog()
    {
        using var fixture = new PortableFixture();
        RewriteChecksumCatalogAsTextMode(fixture.ChecksumsPath);

        PowerForgeReleaseArtifactEvidence evidence = fixture.CreateVerifier().Verify(fixture.CreateRequest());

        Assert.Equal("valid", evidence.SignatureStatus);
    }

    [Fact]
    public void Verify_PowerShellModuleAcceptsGnuTextModeChecksumCatalog()
    {
        using var fixture = new ModuleFixture();
        RewriteChecksumCatalogAsTextMode(fixture.ChecksumsPath);

        PowerForgeReleaseArtifactEvidence evidence = fixture.CreateVerifier().Verify(fixture.CreateRequest());

        Assert.Equal("valid", evidence.SignatureStatus);
    }

    private static void RewriteChecksumCatalogAsTextMode(string path)
    {
        string[] lines = File.ReadAllLines(path)
            .Select(line => line.Length > 65 && line[64] == ' ' && line[65] == '*'
                ? line.Substring(0, 65) + " " + line.Substring(66)
                : line)
            .ToArray();
        File.WriteAllLines(path, lines);
    }
}

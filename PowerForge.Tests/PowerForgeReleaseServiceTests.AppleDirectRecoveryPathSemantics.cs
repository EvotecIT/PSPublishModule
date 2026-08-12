namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void DirectNotarizationResume_UsesOwningVolumeArtifactContainment()
    {
        var projectRoot = CreateSandbox();
        try
        {
            var exportRoot = Directory.CreateDirectory(Path.Combine(projectRoot, "Exports"));
            Directory.CreateDirectory(Path.Combine(exportRoot.FullName, "CasaRay.app"));
            var alternateArtifactPath = Path.Combine(projectRoot, "exports", "casaray.app");
            var caseInsensitive = FrameworkCompatibility.GetPathStringComparisonForPath(exportRoot.FullName) ==
                                  StringComparison.OrdinalIgnoreCase;

            Assert.Equal(
                caseInsensitive,
                AppleReleaseArtifactService.IsWithinRoot(alternateArtifactPath, exportRoot.FullName));
        }
        finally
        {
            TryDelete(projectRoot);
        }
    }
}

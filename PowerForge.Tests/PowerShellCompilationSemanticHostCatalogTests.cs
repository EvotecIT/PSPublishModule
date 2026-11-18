using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [Fact]
    public void CatalogPinsSupportedHostFamiliesAndUpstreamSourceEvidence()
    {
        var windows = PowerShellCompilationSemanticOracleCatalog.Get(
            PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId);
        var current = PowerShellCompilationSemanticOracleCatalog.Get(
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId);

        Assert.Equal("Desktop", windows.PowerShellEdition);
        Assert.Equal(5, windows.PowerShellMajorVersion);
        Assert.Equal("Windows", windows.OperatingSystem);
        Assert.Empty(windows.UpstreamCommit);
        Assert.Equal("Core", current.PowerShellEdition);
        Assert.Equal(7, current.PowerShellMajorVersion);
        Assert.Equal("f260eb9c31ec72c5282f98e5ea24d9be4f8d7536", current.UpstreamCommit);
        Assert.All(PowerShellCompilationSemanticOracleCatalog.Profiles, profile =>
        {
            Assert.NotEmpty(profile.ProfileId);
            Assert.NotEmpty(profile.VersionRange);
            Assert.NotEmpty(profile.DocumentationUri);
        });
    }

    [Fact]
    public void HostPinCatalogCoversEveryProfileAndPromotedCaseWithImmutableExactEvidence()
    {
        Assert.Equal(
            PowerShellCompilationSemanticOracleCatalog.Profiles.Count,
            PowerShellCompilationSemanticHostArtifactPinCatalog.Pins.Count);
        Assert.All(PowerShellCompilationSemanticHostArtifactPinCatalog.Pins, pin =>
        {
            var profile = PowerShellCompilationSemanticOracleCatalog.Get(pin.ProfileId);
            var artifact = pin.GetHostArtifact();
            PowerShellCompilationSemanticHostArtifactService.EnsureMatchesProfile(artifact, profile, artifact.Culture);
            Assert.Equal(profile.UpstreamCommit, pin.UpstreamCommit, ignoreCase: true);
            Assert.Equal(64, pin.HostArtifactIdentitySha256.Length);
            Assert.Equal(
                PowerShellCompilationSemanticOracleCaseCatalog.Cases
                    .Where(item => item.ProfileIds.Contains(pin.ProfileId))
                    .Select(static item => item.CaseId)
                    .OrderBy(static caseId => caseId, StringComparer.Ordinal),
                pin.ReviewedCaseIds);
            Assert.Equal(pin.HostArtifactIdentitySha256,
                PowerShellCompilationSemanticHostArtifactPinCatalog.ExpectedHostArtifactIdentities[pin.ProfileId]);
            artifact.IdentitySha256 = new string('0', 64);
            Assert.Equal(pin.HostArtifactIdentitySha256, pin.GetHostArtifact().IdentitySha256);
            if (profile.Family == PowerShellCompilationSemanticHostFamily.PowerShell7)
            {
                Assert.StartsWith("v7.", pin.ReleaseTag, StringComparison.Ordinal);
                Assert.StartsWith("https://github.com/PowerShell/PowerShell/releases/", pin.ReleaseAssetUri, StringComparison.Ordinal);
                Assert.Equal(64, pin.ReleaseAssetSha256.Length);
            }
        });
    }

    [Fact]
    public void CatalogCarriesOwnedPinnedProvenanceAndRealCasesForEveryPromotedFamilyAndProfile()
    {
        var repositoryRoot = FindRepositoryRoot();
        var evidence = PowerShellCompilationSemanticOracleCatalog.FeatureProvenance;
        var families = evidence.Select(static item => item.FeatureId).Distinct(StringComparer.Ordinal).ToArray();

        Assert.True(families.Length >= 18);
        Assert.Equal(
            families.Length * PowerShellCompilationSemanticOracleCatalog.Profiles.Count,
            evidence.Count);
        Assert.All(evidence, item =>
        {
            Assert.Equal("1.0", item.ContractVersion);
            Assert.NotEmpty(item.OwningComponent);
            Assert.True(
                File.Exists(Path.Combine(repositoryRoot, item.OwningComponent.Replace('/', Path.DirectorySeparatorChar))),
                $"Semantic owner '{item.OwningComponent}' does not resolve from the repository root.");
            Assert.NotEmpty(item.UpstreamTests);
            Assert.NotEmpty(item.DocumentationUris);
            Assert.NotEmpty(item.CaseIds);
            Assert.Contains(PowerShellCompilationSemanticOracleCatalog.Profiles, profile => profile.ProfileId == item.ProfileId);
            Assert.All(item.CaseIds, caseId =>
            {
                var semanticCase = PowerShellCompilationSemanticOracleCaseCatalog.Get(caseId);
                Assert.Equal(item.FeatureId, semanticCase.FeatureId);
                Assert.Contains(item.ProfileId, semanticCase.ProfileIds);
                Assert.False(string.IsNullOrWhiteSpace(PowerShellCompilationSemanticOracleCaseCatalog.ReadSource(caseId)));
            });
        });
    }

    [Fact]
    public void FeatureProvenancePreservesLegacyConstructorContract()
    {
        var signature = new[]
        {
            typeof(string), typeof(string), typeof(string), typeof(IEnumerable<string>),
            typeof(IEnumerable<string>), typeof(string), typeof(string), typeof(string)
        };
        Assert.NotNull(typeof(PowerShellCompilationSemanticFeatureProvenance).GetConstructor(signature));

        var legacy = new PowerShellCompilationSemanticFeatureProvenance(
            "feature",
            "profile",
            "commit",
            new[] { "upstream-test" },
            new[] { "https://example.test/docs" },
            expectedVersionDifference: "expected",
            contractVersion: "1.0",
            owningComponent: "owner");
        Assert.Empty(legacy.CaseIds);

        const string legacyJson = """
        {
          "FeatureId": "feature",
          "ProfileId": "profile",
          "UpstreamCommit": "commit",
          "UpstreamTests": ["upstream-test"],
          "DocumentationUris": ["https://example.test/docs"],
          "ExpectedVersionDifference": "expected",
          "ContractVersion": "1.0",
          "OwningComponent": "owner"
        }
        """;
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<PowerShellCompilationSemanticFeatureProvenance>(legacyJson);
        Assert.NotNull(deserialized);
        Assert.Empty(deserialized.CaseIds);
    }

}

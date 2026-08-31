using PowerForge;
using System.Reflection;
using System.Runtime.Loader;
using Xunit;

namespace PowerForge.Tests;

public sealed class PinnedSemanticHostFactAttribute : FactAttribute
{
    public PinnedSemanticHostFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("POWERFORGE_REQUIRE_PINNED_SEMANTIC_HOSTS"),
                "true",
                StringComparison.OrdinalIgnoreCase))
            Skip = "Set POWERFORGE_REQUIRE_PINNED_SEMANTIC_HOSTS=true and both exact pwsh paths to run the pinned 60-observation matrix.";
    }
}

[Trait("Category", "PowerShellCompilation")]
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
        Assert.Equal("7acb29279dd64e646d821f75d1cc8ad59455a9a6", current.UpstreamCommit);
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

    [Fact]
    public void MinimizedCasesExecuteOnDefaultCompatiblePowerShellProfiles()
    {
        var runner = new PowerShellCompilationSemanticOracleRunner();
        var profiles = new List<(string ProfileId, string? HostPath)>
        {
            (PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId, null),
            (PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, null)
        };

        ExecuteCases(runner, profiles, requirePinnedHosts: false);
    }

    [PinnedSemanticHostFact]
    public void MinimizedCasesExecuteOnConfiguredExactPowerShellProfiles()
    {
        var powerShell74Path = Environment.GetEnvironmentVariable("POWERFORGE_PWSH74_PATH");
        var powerShell76Path = Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH");
        Assert.False(string.IsNullOrWhiteSpace(powerShell74Path), "POWERFORGE_PWSH74_PATH is required by the pinned semantic-host lane.");
        Assert.False(string.IsNullOrWhiteSpace(powerShell76Path), "POWERFORGE_PWSH76_PATH is required by the pinned semantic-host lane.");
        var profiles = new List<(string ProfileId, string? HostPath)>
        {
            (PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId, null),
            (PowerShellCompilationSemanticOracleCatalog.PowerShell74ProfileId, powerShell74Path),
            (PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, powerShell76Path)
        };
        Assert.Equal(
            PowerShellCompilationSemanticHostArtifactPinCatalog.Pins.Select(static pin => pin.ProfileId),
            profiles.Select(static profile => profile.ProfileId).OrderBy(static profileId => profileId, StringComparer.Ordinal));

        ExecuteCases(new PowerShellCompilationSemanticOracleRunner(), profiles, requirePinnedHosts: true);
    }

    private static void ExecuteCases(
        PowerShellCompilationSemanticOracleRunner runner,
        IReadOnlyList<(string ProfileId, string? HostPath)> profiles,
        bool requirePinnedHosts)
    {

        foreach (var semanticCase in PowerShellCompilationSemanticOracleCaseCatalog.Cases)
        foreach (var profile in profiles.Where(item => semanticCase.ProfileIds.Contains(item.ProfileId)))
        {
            using var fixture = OracleFixture.Create(PowerShellCompilationSemanticOracleCaseCatalog.ReadSource(semanticCase.CaseId));
            var observation = runner.Observe(new PowerShellCompilationSemanticOracleRequest(profile.ProfileId, fixture.ScriptPath)
            {
                Arguments = semanticCase.Arguments.ToArray(),
                ObservedPropertyNames = semanticCase.ObservedPropertyNames.ToArray(),
                Culture = "en-US",
                HostExecutablePath = profile.HostPath,
                ExpectedHostArtifactSha256 = requirePinnedHosts
                    ? PowerShellCompilationSemanticHostArtifactPinCatalog.Get(profile.ProfileId).HostArtifactIdentitySha256
                    : null
            });
            var value = Assert.Single(observation.Success);
            Assert.True(
                semanticCase.ExpectedValue.Equals(value.Value, StringComparison.Ordinal) &&
                semanticCase.ExpectedTypeName.Equals(value.TypeName, StringComparison.Ordinal),
                $"Case '{semanticCase.CaseId}' under '{profile.ProfileId}' returned '{value.Value}' ({value.TypeName}).");
            Assert.Empty(observation.ErrorRecords);
        }
    }

    [Fact]
    public void UpstreamMonitorProposesAffectedContractsWithoutMovingPinnedProfile()
    {
        var profileId = PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId;
        var pinned = PowerShellCompilationSemanticOracleCatalog.Get(profileId).UpstreamCommit;
        var changes = PowerShellCompilationSemanticOracleCatalog.ReviewUpstreamChanges(
            new Dictionary<string, string> { [profileId] = new string('a', 40) });

        var change = Assert.Single(changes);
        Assert.Equal(profileId, change.ProfileId);
        Assert.Equal(pinned, change.PinnedCommit);
        Assert.NotEmpty(change.AffectedFeatureIds);
        Assert.Equal(pinned, PowerShellCompilationSemanticOracleCatalog.Get(profileId).UpstreamCommit);
        Assert.Empty(PowerShellCompilationSemanticOracleCatalog.ReviewUpstreamChanges(
            new Dictionary<string, string> { [profileId] = pinned }));
        Assert.Throws<KeyNotFoundException>(() => PowerShellCompilationSemanticOracleCatalog.ReviewUpstreamChanges(
            new Dictionary<string, string> { ["unknown"] = pinned }));
    }

    [Fact]
    public void ComparerRejectsUnexplainedDifferenceAndAcceptsNamedPathOnly()
    {
        var expected = Envelope("42");
        var actual = Envelope("43");

        var rejected = PowerShellCompilationSemanticOracleComparer.Compare(expected, actual);
        var difference = Assert.Single(rejected);
        Assert.Equal("Success", difference.Path);
        Assert.Empty(PowerShellCompilationSemanticOracleComparer.Compare(expected, actual, new[] { "Success" }));
        Assert.Single(PowerShellCompilationSemanticOracleComparer.Compare(expected, actual, new[] { "Warnings" }));
    }

    [Fact]
    public void PromotionGateRequiresPinnedHostProvenanceAndIndependentSurfaces()
    {
        var interpreted = PromotableEnvelope("Interpreted", "42", includeHostArtifact: true);
        var strict = PromotableEnvelope("Strict", "42", includeHostArtifact: false);
        var artifactIdentity = interpreted.HostArtifact!.IdentitySha256;

        Assert.Empty(PowerShellCompilationSemanticOraclePromotionGate.EnsurePromotable(
            PowerShellCompilationFeatureIds.ParameterType,
            new[] { interpreted, strict },
            new Dictionary<string, string>
            {
                [PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId] = artifactIdentity
            }));
        Assert.Throws<InvalidOperationException>(() => PowerShellCompilationSemanticOraclePromotionGate.EnsurePromotable(
            PowerShellCompilationFeatureIds.ParameterType,
            new[] { interpreted, strict },
            new Dictionary<string, string>()));
        Assert.Throws<InvalidOperationException>(() => PowerShellCompilationSemanticOraclePromotionGate.EnsurePromotable(
            "Unknown.Feature",
            new[] { interpreted, strict },
            new Dictionary<string, string>
            {
                [PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId] = artifactIdentity
            }));
    }

    [Fact]
    public void PromotionGateUsesCanonicalImmutableHostPinsByDefault()
    {
        var interpreted = PromotableEnvelope("Interpreted", "42", includeHostArtifact: false);
        interpreted.HostArtifact = PowerShellCompilationSemanticHostArtifactPinCatalog
            .Get(PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId)
            .GetHostArtifact();
        interpreted.HostVersion = interpreted.HostArtifact.HostVersion;
        interpreted.PowerShellEdition = interpreted.HostArtifact.PowerShellEdition;
        interpreted.OperatingSystem = interpreted.HostArtifact.OperatingSystem;
        interpreted.Architecture = interpreted.HostArtifact.Architecture;
        interpreted.Culture = interpreted.HostArtifact.Culture;
        var strict = PromotableEnvelope("Strict", "42", includeHostArtifact: false);

        Assert.Empty(PowerShellCompilationSemanticOraclePromotionGate.EnsurePromotable(
            PowerShellCompilationFeatureIds.ParameterType,
            new[] { interpreted, strict }));
    }

    [Fact]
    public void PromotionGateRejectsUnexplainedOrUnjustifiedDifferences()
    {
        var interpreted = PromotableEnvelope("Interpreted", "42", includeHostArtifact: true);
        var strict = PromotableEnvelope("Strict", "43", includeHostArtifact: false);
        var pins = new Dictionary<string, string>
        {
            [PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId] = interpreted.HostArtifact!.IdentitySha256
        };

        Assert.Throws<InvalidOperationException>(() => PowerShellCompilationSemanticOraclePromotionGate.EnsurePromotable(
            PowerShellCompilationFeatureIds.ParameterType,
            new[] { interpreted, strict },
            pins));
        Assert.Throws<InvalidOperationException>(() => PowerShellCompilationSemanticOraclePromotionGate.EnsurePromotable(
            PowerShellCompilationFeatureIds.ParameterType,
            new[] { interpreted, strict },
            pins,
            new[] { "Success" }));
        var differences = PowerShellCompilationSemanticOraclePromotionGate.EnsurePromotable(
            PowerShellCompilationFeatureIds.ParameterType,
            new[] { interpreted, strict },
            pins,
            new[] { "Success" },
            "The pinned semantic profile intentionally records this version-specific result.");
        Assert.Equal("Success", Assert.Single(differences).Path);
    }

    [Fact]
    public void PromotionGateRequiresCorrespondingHostBackedAndRuntimeFreeLanes()
    {
        var interpreted = PromotableEnvelope("Interpreted", "42", includeHostArtifact: true);
        var hybrid = PromotableEnvelope("Hybrid", "42", includeHostArtifact: true);
        var strict = PromotableEnvelope("Strict", "42", includeHostArtifact: false);
        var handWritten = PromotableEnvelope("HandWrittenClr", "42", includeHostArtifact: false);
        var pins = new Dictionary<string, string>
        {
            [PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId] = interpreted.HostArtifact!.IdentitySha256
        };

        Assert.Throws<InvalidOperationException>(() => PowerShellCompilationSemanticOraclePromotionGate.EnsurePromotable(
            PowerShellCompilationFeatureIds.ParameterType,
            new[] { interpreted, hybrid },
            pins));
        Assert.Throws<InvalidOperationException>(() => PowerShellCompilationSemanticOraclePromotionGate.EnsurePromotable(
            PowerShellCompilationFeatureIds.ParameterType,
            new[] { strict, handWritten },
            new Dictionary<string, string>()));
        Assert.Empty(PowerShellCompilationSemanticOraclePromotionGate.EnsurePromotable(
            PowerShellCompilationFeatureIds.ParameterType,
            new[] { interpreted, handWritten },
            pins));

        handWritten.ExecutionSurface = "InventedLane";
        Assert.Throws<InvalidOperationException>(() => PowerShellCompilationSemanticOraclePromotionGate.EnsurePromotable(
            PowerShellCompilationFeatureIds.ParameterType,
            new[] { interpreted, handWritten },
            pins));
    }

    [Fact]
    public void HostArtifactIdentityUsesUnambiguousCanonicalFieldFraming()
    {
        var first = CreateHostArtifact("en-US", new[] { "A", "B" });
        var formerlyAmbiguous = CreateHostArtifact("en-US\nA", new[] { "B" });

        Assert.NotEqual(first.IdentitySha256, formerlyAmbiguous.IdentitySha256);
    }

    [Fact]
    public void ExternalWindowsPowerShellAndPowerShell7ProduceSamePortableSemantics()
    {
        using var fixture = OracleFixture.Create("param([int] $Value)\n$Value + 1");
        var runner = new PowerShellCompilationSemanticOracleRunner();
        var windows = runner.Observe(new PowerShellCompilationSemanticOracleRequest(
            PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId,
            fixture.ScriptPath)
        {
            Arguments = new[] { "41" },
            Culture = "en-US"
        });
        var current = runner.Observe(new PowerShellCompilationSemanticOracleRequest(
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            fixture.ScriptPath)
        {
            Arguments = new[] { "41" },
            Culture = "en-US"
        });

        Assert.StartsWith("5.1.", windows.HostVersion, StringComparison.Ordinal);
        Assert.StartsWith("7.6.", current.HostVersion, StringComparison.Ordinal);
        Assert.Empty(PowerShellCompilationSemanticOracleComparer.Compare(windows, current, new[] { "Encoding" }));
        Assert.NotEqual(
            System.Text.Json.JsonSerializer.Serialize(windows.Encoding),
            System.Text.Json.JsonSerializer.Serialize(current.Encoding));
        Assert.True(current.Success.Length == 1, System.Text.Json.JsonSerializer.Serialize(current));
        var value = Assert.Single(current.Success);
        Assert.Equal("42", value.Value);
        Assert.Equal("System.Int32", value.TypeName);
    }

    [Fact]
    public void ExternalHostsAcceptRequirementsOwnedByTheirExactProfiles()
    {
        using var desktopFixture = OracleFixture.Create("#requires -Version 5.1\n#requires -PSEdition Desktop\n'Desktop'");
        using var coreFixture = OracleFixture.Create("#requires -Version 7.6\n#requires -PSEdition Core\n'Core'");
        var runner = new PowerShellCompilationSemanticOracleRunner();

        var desktop = runner.Observe(new PowerShellCompilationSemanticOracleRequest(
            PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId,
            desktopFixture.ScriptPath));
        var core = runner.Observe(new PowerShellCompilationSemanticOracleRequest(
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            coreFixture.ScriptPath));

        Assert.Equal("Desktop", Assert.Single(desktop.Success).Value);
        Assert.Equal("Core", Assert.Single(core.Success).Value);
        Assert.Empty(desktop.Errors);
        Assert.Empty(core.Errors);
    }

    [Fact]
    public void ExternalOracleCapturesSelectedPropertiesStreamsAndIsolatedFileEffects()
    {
        using var fixture = OracleFixture.Create("""
param([string] $Root)
Set-Content -LiteralPath (Join-Path $Root 'created.txt') -Value 'created'
Write-Warning 'careful'
Write-Information 'noted' -InformationAction Continue
[pscustomobject]@{ Name = 'sample'; Count = 2 }
""");
        var observation = new PowerShellCompilationSemanticOracleRunner().Observe(
            new PowerShellCompilationSemanticOracleRequest(
                PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
                fixture.ScriptPath)
            {
                Arguments = new[] { fixture.EffectsPath },
                ObservedPropertyNames = new[] { "Count", "Name" },
                FileSystemRoot = fixture.EffectsPath,
                Culture = "en-US"
            });

        var value = Assert.Single(observation.Success);
        Assert.Equal(new[] { "Name", "Count" }, value.Properties.Select(static property => property.Name));
        Assert.Contains("careful", observation.Warnings);
        Assert.Contains("noted", observation.Information);
        Assert.Single(observation.FileSystemEffects, static effect => effect.StartsWith("Added:created.txt:", StringComparison.Ordinal));
    }

    [Fact]
    public void ExternalOracleDistinguishesNoSuccessOutput()
    {
        using var fixture = OracleFixture.Create("return");
        var observation = new PowerShellCompilationSemanticOracleRunner().Observe(
            new PowerShellCompilationSemanticOracleRequest(
                PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
                fixture.ScriptPath));

        Assert.Equal("NoOutput", observation.SuccessState);
        Assert.True(observation.NoSuccessOutput);
        Assert.Equal(0, observation.SuccessCardinality);
    }

    [Fact]
    public void ExternalOraclePreservesExplicitNullSuccessState()
    {
        using var fixture = OracleFixture.Create("$null");
        var observation = new PowerShellCompilationSemanticOracleRunner().Observe(
            new PowerShellCompilationSemanticOracleRequest(
                PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
                fixture.ScriptPath));

        Assert.Equal("Output", observation.SuccessState);
        var value = Assert.Single(observation.Success);
        Assert.True(value.IsNull);
        Assert.False(value.IsAutomationNull);
        Assert.Equal("Null", value.ValueState);
    }

    [Theory]
    [InlineData("PowerForge.Oracle.WindowsPowerShell/5.1")]
    [InlineData("PowerForge.Oracle.PowerShell/7.6")]
    public void ExternalOraclePreservesExplicitAutomationNullPropertyIdentity(string profileId)
    {
        using var fixture = OracleFixture.Create("""
[pscustomobject] [ordered] @{
    Sentinel = [System.Management.Automation.Internal.AutomationNull]::Value
    NullValue = $null
}
""");
        var observation = new PowerShellCompilationSemanticOracleRunner().Observe(
            new PowerShellCompilationSemanticOracleRequest(
                profileId,
                fixture.ScriptPath)
            {
                ObservedPropertyNames = new[] { "Sentinel", "NullValue" }
            });

        var value = Assert.Single(observation.Success);
        var sentinel = Assert.Single(value.Properties, static property => property.Name == "Sentinel");
        Assert.True(sentinel.IsAutomationNull, $"State={sentinel.ValueState}; IsNull={sentinel.IsNull}; Type={sentinel.TypeName}; Value={sentinel.Value}");
        Assert.False(sentinel.IsNull);
        Assert.Equal("AutomationNull", sentinel.ValueState);
        Assert.Equal("System.Management.Automation.Internal.AutomationNull", sentinel.TypeName);
        var nullValue = Assert.Single(value.Properties, static property => property.Name == "NullValue");
        Assert.True(nullValue.IsNull);
        Assert.False(nullValue.IsAutomationNull);
        Assert.Equal("Null", nullValue.ValueState);
    }

    [Fact]
    public void ExternalOracleIntegrityBindsAndReplaysTheExactHostArtifact()
    {
        using var fixture = OracleFixture.Create("42");
        var runner = new PowerShellCompilationSemanticOracleRunner();
        var first = runner.Observe(new PowerShellCompilationSemanticOracleRequest(
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            fixture.ScriptPath));

        var artifact = Assert.IsType<PowerShellCompilationSemanticHostArtifact>(first.HostArtifact);
        Assert.Equal(64, artifact.ExecutableSha256.Length);
        Assert.Equal(64, artifact.IdentitySha256.Length);
        Assert.True(artifact.ExecutableLength > 0);
        Assert.Equal(first.HostVersion, artifact.HostVersion);
        Assert.DoesNotContain(Path.DirectorySeparatorChar, artifact.ExecutableName);

        var replay = runner.Observe(new PowerShellCompilationSemanticOracleRequest(
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            fixture.ScriptPath)
        {
            ExpectedHostArtifactSha256 = artifact.IdentitySha256
        });
        Assert.Equal(artifact.IdentitySha256, replay.HostArtifact!.IdentitySha256);

        var mismatch = Assert.Throws<InvalidOperationException>(() => runner.Observe(
            new PowerShellCompilationSemanticOracleRequest(
                PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
                fixture.ScriptPath)
            {
                ExpectedHostArtifactSha256 = new string('0', 64)
            }));
        Assert.Contains("does not match", mismatch.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExternalOraclePreservesStructuredCrossStreamOrderAndErrorIdentity()
    {
        using var fixture = OracleFixture.Create("""
Write-Warning 'warning-one'
Write-Information 'information-two' -Tags 'semantic','ordered' -InformationAction Continue
Write-Verbose 'verbose-three' -Verbose
Write-Debug 'debug-four' -Debug
Write-Error 'error-five' -ErrorId 'PowerForge.Semantic.Test' -Category InvalidData
'success-six'
""");
        var observation = new PowerShellCompilationSemanticOracleRunner().Observe(
            new PowerShellCompilationSemanticOracleRequest(
                PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
                fixture.ScriptPath));

        Assert.Equal(new[] { "Warning", "Information", "Verbose", "Debug" },
            observation.StreamRecords.Select(static record => record.Stream));
        Assert.Equal(new[] { 1, 2, 3, 4 }, observation.StreamRecords.Select(static record => record.Sequence));
        Assert.Equal(new[] { "semantic", "ordered" }, observation.StreamRecords[1].Tags);
        var error = Assert.Single(observation.ErrorRecords);
        Assert.Equal(5, error.Sequence);
        Assert.Contains("PowerForge.Semantic.Test", error.FullyQualifiedErrorId, StringComparison.Ordinal);
        Assert.Equal("InvalidData", error.Category);
        Assert.False(error.IsTerminating);
        Assert.Equal(6, Assert.Single(observation.Success).Sequence);
    }

    [Fact]
    public void ExternalOracleCapturesTerminatingErrorIdentity()
    {
        using var fixture = OracleFixture.Create("throw 'terminal-error'");
        var observation = new PowerShellCompilationSemanticOracleRunner().Observe(
            new PowerShellCompilationSemanticOracleRequest(
                PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
                fixture.ScriptPath));

        var error = Assert.Single(observation.ErrorRecords);
        Assert.True(error.IsTerminating);
        Assert.Contains("terminal-error", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(observation.Success);
    }

    [Fact]
    public void ExternalOracleRejectsRuntimeFreeExecutionSurfaceLabels()
    {
        using var fixture = OracleFixture.Create("42");
        var request = new PowerShellCompilationSemanticOracleRequest(
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            fixture.ScriptPath)
        {
            ExecutionSurface = "Strict"
        };

        Assert.Throws<ArgumentException>(() => new PowerShellCompilationSemanticOracleRunner().Observe(request));
    }

    [Fact]
    public void SamePortableEnvelopeComparesInterpretedAndStrictClrExecution()
    {
        using var fixture = StrictOracleFixture.Create();
        var interpreted = new PowerShellCompilationSemanticOracleRunner().Observe(
            new PowerShellCompilationSemanticOracleRequest(
                PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
                fixture.OracleScriptPath));
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.SourcePath,
            fixture.OutputPath,
            "OracleStrict",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true));

        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        using var stream = File.OpenRead(build.ArtifactPath!);
        var context = new AssemblyLoadContext("OracleStrict-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            var method = context.LoadFromStream(stream)
                .GetType("PowerForge.Compiled.OracleStrictMethods", throwOnError: true)!
                .GetMethod("Get_OracleValue", BindingFlags.Public | BindingFlags.Static)!;
            var value = method.Invoke(null, new object[] { 41 });
            var strict = PowerShellCompilationSemanticRuntimeFreeObserver.Observe(
                interpreted.ProfileId,
                PowerShellCompilationSemanticExecutionSurface.Strict,
                value,
                culture: System.Globalization.CultureInfo.GetCultureInfo("en-US"));
            Assert.Empty(PowerShellCompilationSemanticOracleComparer.Compare(interpreted, strict, new[] { "Encoding" }));
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void RuntimeFreeObserverAppliesPipelineEnumerationAndSuppressesNullItems()
    {
        var observation = PowerShellCompilationSemanticRuntimeFreeObserver.Observe(
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            PowerShellCompilationSemanticExecutionSurface.HandWrittenClr,
            new object?[] { 40, null, 2 },
            culture: System.Globalization.CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal(3, observation.SchemaVersion);
        Assert.Equal("Output", observation.SuccessState);
        Assert.Equal(new[] { "40", "2" }, observation.Success.Select(static value => value.Value));
        Assert.All(observation.Success, static value => Assert.Equal("Scalar", value.EnumerationState));
        Assert.Null(observation.HostArtifact);
    }

    [Fact]
    public void RuntimeFreeObserverUsesValidDefaultCaseCulture()
    {
        var observation = PowerShellCompilationSemanticRuntimeFreeObserver.Observe(
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            PowerShellCompilationSemanticExecutionSurface.HandWrittenClr,
            42);

        Assert.Equal("en-US", observation.Culture);
        Assert.Equal("42", Assert.Single(observation.Success).Value);
    }

    [Fact]
    public void RuntimeFreeObserverRejectsUnboundedSuccessOutput()
    {
        var values = Enumerable.Range(0, PowerShellCompilationSemanticOracleEnvelopeValidator.MaximumObservationItems + 1);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PowerShellCompilationSemanticRuntimeFreeObserver.Observe(
                PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
                PowerShellCompilationSemanticExecutionSurface.HandWrittenClr,
                values,
                culture: System.Globalization.CultureInfo.GetCultureInfo("en-US")));

        Assert.Contains("item observation limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeFreeObserverCountsNullItemsAndBoundsPropertyNameSources()
    {
        var nulls = Enumerable.Repeat<object?>(null, PowerShellCompilationSemanticOracleEnvelopeValidator.MaximumObservationItems + 1);
        Assert.Throws<InvalidOperationException>(() => PowerShellCompilationSemanticRuntimeFreeObserver.Observe(
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            PowerShellCompilationSemanticExecutionSurface.HandWrittenClr,
            nulls));

        var propertyNames = Enumerable.Repeat(string.Empty, PowerShellCompilationSemanticOracleEnvelopeValidator.MaximumObservationItems + 1);
        Assert.Throws<ArgumentException>(() => PowerShellCompilationSemanticRuntimeFreeObserver.Observe(
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            PowerShellCompilationSemanticExecutionSurface.HandWrittenClr,
            42,
            propertyNames));
    }

    [Fact]
    public async Task ProcessRunnerBoundsRetainedOutputAndMarksEvidenceInvalid()
    {
        var request = new ProcessRunRequest(
            "dotnet",
            Path.GetTempPath(),
            new[] { "--info" },
            TimeSpan.FromSeconds(30))
        {
            MaxCapturedOutputCharacters = 32
        };

        var result = await new ProcessRunner().RunAsync(request);

        Assert.True(result.StandardOutputLimitExceeded);
        Assert.Equal(32, result.StdOut.Length);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void PromotionGateRejectsContradictoryOrMismatchedRuntimeFreeEvidence()
    {
        var interpreted = PromotableEnvelope("Interpreted", "42", includeHostArtifact: true);
        var strict = PromotableEnvelope("Strict", "42", includeHostArtifact: false);
        var pins = new Dictionary<string, string>
        {
            [PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId] = interpreted.HostArtifact!.IdentitySha256
        };

        strict.SuccessState = "NoOutput";
        Assert.Throws<InvalidOperationException>(() => PowerShellCompilationSemanticOraclePromotionGate.EnsurePromotable(
            PowerShellCompilationFeatureIds.ParameterType,
            new[] { interpreted, strict },
            pins));

        strict.SuccessState = "Output";
        strict.Culture = "pl-PL";
        Assert.Throws<InvalidOperationException>(() => PowerShellCompilationSemanticOraclePromotionGate.EnsurePromotable(
            PowerShellCompilationFeatureIds.ParameterType,
            new[] { interpreted, strict },
            pins));
    }

    [Theory]
    [InlineData("Password")]
    [InlineData("Credential")]
    [InlineData("AccessToken")]
    [InlineData("CimSessionId")]
    public void ExternalOracleRejectsSensitiveAndLiveRuntimeProperties(string propertyName)
    {
        using var fixture = OracleFixture.Create("1");
        var request = new PowerShellCompilationSemanticOracleRequest(
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            fixture.ScriptPath)
        {
            ObservedPropertyNames = new[] { propertyName }
        };

        var exception = Assert.Throws<ArgumentException>(() => new PowerShellCompilationSemanticOracleRunner().Observe(request));
        Assert.Contains(propertyName, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PowerShellCompilationSemanticOracleEnvelope Envelope(string value)
        => new()
        {
            ProfileId = "profile",
            ExecutionSurface = "Strict",
            Success = new[]
            {
                new PowerShellCompilationSemanticValueObservation
                {
                    Value = value,
                    TypeName = "System.Int32"
                }
            }
        };

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PSPublishModule.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Unable to locate repository root.");
    }

    private static PowerShellCompilationBuildResult BuildStrictOracleExecutable(OracleFixture fixture, string name)
        => new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            Path.Combine(fixture.RootPath, "strict"),
            name,
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0",
            SemanticProfileId = PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            SingleFile = false
        });

    private static PowerShellCompilationSemanticOracleEnvelope PromotableEnvelope(
        string executionSurface,
        string value,
        bool includeHostArtifact)
    {
        PowerShellCompilationSemanticHostArtifact? artifact = null;
        if (includeHostArtifact)
            artifact = CreateHostArtifact(
                "en-US",
                PowerShellCompilationSemanticOracleCatalog.Get(
                    PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId).FeatureSwitches);
        return new PowerShellCompilationSemanticOracleEnvelope
        {
            ProfileId = PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            ExecutionSurface = executionSurface,
            HostVersion = artifact?.HostVersion ?? string.Empty,
            PowerShellEdition = artifact?.PowerShellEdition ?? string.Empty,
            OperatingSystem = "Windows",
            Architecture = "X64",
            Culture = "en-US",
            HostArtifact = artifact,
            SuccessState = "Output",
            Success = new[]
            {
                new PowerShellCompilationSemanticValueObservation
                {
                    Sequence = 1,
                    Value = value,
                    TypeName = "System.Int32"
                }
            }
        };
    }

    private static PowerShellCompilationSemanticHostArtifact CreateHostArtifact(
        string uiCulture,
        IEnumerable<string> featureSwitches)
        => PowerShellCompilationSemanticHostArtifactService.Normalize(new PowerShellCompilationSemanticHostArtifact
        {
            ExecutableName = "pwsh.exe",
            ExecutableSha256 = new string('1', 64),
            ExecutableLength = 100,
            ExecutableFileVersion = "7.6.4.0",
            ExecutableProductVersion = "7.6.4",
            HostVersion = "7.6.4",
            GitCommitId = "7.6.4",
            PowerShellEdition = "Core",
            OperatingSystem = "Windows",
            OperatingSystemVersion = "Microsoft Windows 10.0",
            Architecture = "X64",
            Culture = "en-US",
            UICulture = uiCulture,
            FeatureSwitches = featureSwitches.ToArray()
        });

    private sealed class OracleFixture : IDisposable
    {
        private OracleFixture(string rootPath, string scriptPath, string effectsPath)
        {
            RootPath = rootPath;
            ScriptPath = scriptPath;
            EffectsPath = effectsPath;
        }

        public string RootPath { get; }
        public string ScriptPath { get; }
        public string EffectsPath { get; }

        public static OracleFixture Create(string source)
        {
            var root = Path.Combine(Path.GetTempPath(), "PowerForgeSemanticOracleTests", Guid.NewGuid().ToString("N"));
            var effects = Path.Combine(root, "effects");
            Directory.CreateDirectory(effects);
            var script = Path.Combine(root, "case.ps1");
            File.WriteAllText(script, source);
            return new OracleFixture(root, script, effects);
        }

        public void Dispose()
        {
            try { Directory.Delete(RootPath, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class StrictOracleFixture : IDisposable
    {
        private StrictOracleFixture(string rootPath, string sourcePath, string oracleScriptPath, string outputPath)
        {
            RootPath = rootPath;
            SourcePath = sourcePath;
            OracleScriptPath = oracleScriptPath;
            OutputPath = outputPath;
        }

        public string RootPath { get; }
        public string SourcePath { get; }
        public string OracleScriptPath { get; }
        public string OutputPath { get; }

        public static StrictOracleFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "PowerForgeStrictOracleTests", Guid.NewGuid().ToString("N"));
            var output = Path.Combine(root, "out");
            Directory.CreateDirectory(output);
            var source = Path.Combine(root, "source.ps1");
            var oracle = Path.Combine(root, "oracle.ps1");
            const string function = "function Get-OracleValue { param([int] $Value); [int] $result = $Value; $result += 1; return $result }";
            File.WriteAllText(source, function);
            File.WriteAllText(oracle, function + "; Get-OracleValue 41");
            return new StrictOracleFixture(root, source, oracle, output);
        }

        public void Dispose()
        {
            try { Directory.Delete(RootPath, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

using PowerForge;
using System.Reflection;
using System.Runtime.Loader;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed class PowerShellCompilationSemanticOracleTests
{
    [Fact]
    public void CatalogPinsSupportedHostFamiliesAndUpstreamSourceEvidence()
    {
        var windows = PowerShellCompilationSemanticOracleCatalog.Get(
            PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId);
        var current = PowerShellCompilationSemanticOracleCatalog.Get(
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId);

        Assert.Equal("Desktop", windows.PowerShellEdition);
        Assert.Equal("Windows", windows.OperatingSystem);
        Assert.Empty(windows.UpstreamCommit);
        Assert.Equal("Core", current.PowerShellEdition);
        Assert.Equal("7acb29279dd64e646d821f75d1cc8ad59455a9a6", current.UpstreamCommit);
        Assert.All(PowerShellCompilationSemanticOracleCatalog.Profiles, profile =>
        {
            Assert.NotEmpty(profile.ProfileId);
            Assert.NotEmpty(profile.VersionRange);
            Assert.NotEmpty(profile.DocumentationUri);
        });
    }

    [Fact]
    public void CatalogCarriesOwnedPinnedProvenanceForEveryPromotedFamilyAndProfile()
    {
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
            Assert.NotEmpty(item.UpstreamTests);
            Assert.NotEmpty(item.DocumentationUris);
            Assert.Contains(PowerShellCompilationSemanticOracleCatalog.Profiles, profile => profile.ProfileId == item.ProfileId);
        });
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
        Assert.Empty(PowerShellCompilationSemanticOracleComparer.Compare(windows, current));
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
            var strict = new PowerShellCompilationSemanticOracleEnvelope
            {
                ProfileId = interpreted.ProfileId,
                ExecutionSurface = "Strict",
                Success = new[]
                {
                    new PowerShellCompilationSemanticValueObservation
                    {
                        Sequence = 1,
                        Value = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!,
                        TypeName = value!.GetType().FullName!
                    }
                }
            };
            Assert.Empty(PowerShellCompilationSemanticOracleComparer.Compare(interpreted, strict));
        }
        finally
        {
            context.Unload();
        }
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
            HostArtifact = artifact,
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

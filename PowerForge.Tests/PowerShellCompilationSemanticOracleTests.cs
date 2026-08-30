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
        Assert.Equal(new[] { "Count", "Name" }, value.Properties.Select(static property => property.Name));
        Assert.Contains("careful", observation.Warnings);
        Assert.Contains("noted", observation.Information);
        Assert.Single(observation.FileSystemEffects, static effect => effect.StartsWith("Added:created.txt:", StringComparison.Ordinal));
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

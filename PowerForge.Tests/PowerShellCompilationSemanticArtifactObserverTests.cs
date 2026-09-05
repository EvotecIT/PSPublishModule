using PowerForge;
using System.Globalization;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [Fact]
    public void RuntimeFreeArtifactObserverRejectsHostedArtifactsBeforeProcessLaunch()
    {
        var runner = new RejectingSemanticProcessRunner();
        var observer = new PowerShellCompilationSemanticRuntimeFreeArtifactObserver(runner);
        var build = new PowerShellCompilationBuildResult
        {
            Succeeded = true,
            ArtifactPath = "hosted-artifact.exe",
            Manifest = new PowerShellCompilationArtifactManifest
            {
                Kind = PowerShellCompilationArtifactKind.Executable,
                Mode = PowerShellCompilationMode.Hybrid
            }
        };

        var exception = Assert.Throws<InvalidOperationException>(() => observer.Observe(
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            build));

        Assert.Contains("Strict executable", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, runner.InvocationCount);
    }

    [Fact]
    public void RuntimeFreeArtifactObserverRejectsArtifactReplacementBeforeProcessLaunch()
    {
        using var fixture = OracleFixture.Create("#requires -Version 5.1" + Environment.NewLine + "42");
        var build = BuildStrictOracleExecutable(fixture, "ReplacedStrictOracle");
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        File.AppendAllText(build.ArtifactPath!, "replacement");
        var runner = new RejectingSemanticProcessRunner();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new PowerShellCompilationSemanticRuntimeFreeArtifactObserver(runner).Observe(
                PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
                build));

        Assert.Contains("differs from its compiler evidence", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, runner.InvocationCount);
    }

    [Fact]
    public void RuntimeFreeArtifactObserverPreservesFramedMultilineString()
    {
        using var fixture = OracleFixture.Create("\"line-one`nline-two\"");
        var build = BuildStrictOracleExecutable(fixture, "StringStrictOracle");
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);

        var observation = new PowerShellCompilationSemanticRuntimeFreeArtifactObserver().Observe(
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            build);

        var value = Assert.Single(observation.Success);
        Assert.Equal("System.String", value.TypeName);
        Assert.Equal("line-one\nline-two", value.Value);
    }

    [Fact]
    public void RuntimeFreeArtifactObserverDistinguishesFramedNullableValueFromNoOutput()
    {
        using var fixture = OracleFixture.Create("[Nullable[int]] $null");
        var build = BuildStrictOracleExecutable(fixture, "NullableStringStrictOracle");
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);

        var observation = new PowerShellCompilationSemanticRuntimeFreeArtifactObserver().Observe(
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            build);

        Assert.Equal("Output", observation.SuccessState);
        var value = Assert.Single(observation.Success);
        Assert.True(value.IsNull);
        Assert.Equal("Null", value.ValueState);
    }

    [Fact]
    public void RuntimeFreeArtifactObserverUsesRequestedCultureForFramedValues()
    {
        using var fixture = OracleFixture.Create("[decimal] 1234.5");
        var build = BuildStrictOracleExecutable(fixture, "CultureStrictOracle");
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);

        var observation = new PowerShellCompilationSemanticRuntimeFreeArtifactObserver().Observe(
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            build,
            culture: CultureInfo.GetCultureInfo("fr-FR"));

        var value = Assert.Single(observation.Success);
        Assert.Equal("System.Decimal", value.TypeName);
        Assert.Equal("1234,5", value.Value);
    }

    [Fact]
    public void RuntimeFreeArtifactObserverPreservesFramedNoOutput()
    {
        using var fixture = OracleFixture.Create("return");
        var build = BuildStrictOracleExecutable(fixture, "NoOutputStrictOracle");
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);

        var observation = new PowerShellCompilationSemanticRuntimeFreeArtifactObserver().Observe(
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            build);

        Assert.Equal("NoOutput", observation.SuccessState);
        Assert.Empty(observation.Success);
    }

    [Fact]
    public void RuntimeFreeArtifactObserverRejectsUnframedSuccessOutput()
    {
        using var fixture = OracleFixture.Create("42");
        var build = BuildStrictOracleExecutable(fixture, "UnframedStrictOracle");
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        var runner = new UnframedSemanticProcessRunner();

        var exception = Assert.Throws<InvalidDataException>(() =>
            new PowerShellCompilationSemanticRuntimeFreeArtifactObserver(runner).Observe(
                PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
                build));

        Assert.Contains("framed semantic observation", exception.Message, StringComparison.Ordinal);
        Assert.Equal("PowerForge.StrictObservation/1", runner.Protocol);
        Assert.Equal("en-US", runner.Culture);
    }

    [Fact]
    public void RuntimeFreeArtifactObserverRejectsInvalidUtf8FramePayload()
    {
        using var fixture = OracleFixture.Create("42");
        var build = BuildStrictOracleExecutable(fixture, "InvalidUtf8StrictOracle");
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        var runner = new InvalidUtf8SemanticProcessRunner();

        var exception = Assert.Throws<InvalidDataException>(() =>
            new PowerShellCompilationSemanticRuntimeFreeArtifactObserver(runner).Observe(
                PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
                build));

        Assert.Contains("invalid UTF-8", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeFreeArtifactObserverRejectsOversizedOutputFromCustomRunner()
    {
        using var fixture = OracleFixture.Create("42");
        var build = BuildStrictOracleExecutable(fixture, "OversizedStrictOracle");
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        var runner = new OversizedSemanticProcessRunner();

        var exception = Assert.Throws<InvalidDataException>(() =>
            new PowerShellCompilationSemanticRuntimeFreeArtifactObserver(runner).Observe(
                PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
                build));

        Assert.Contains("oversized process output", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, runner.InvocationCount);
    }

    [PinnedSemanticHostFact]
    public void RuntimeFreeArtifactObserverQualifiesRequiresDirectiveCaseAgainstPinnedHost()
    {
        var semanticCase = PowerShellCompilationSemanticOracleCaseCatalog.Get("PowerForge.Semantic/requires-directive");
        using var fixture = OracleFixture.Create(PowerShellCompilationSemanticOracleCaseCatalog.ReadSource(semanticCase.CaseId));
        var pin = PowerShellCompilationSemanticHostArtifactPinCatalog.Get(
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId);
        var interpreted = new PowerShellCompilationSemanticOracleRunner().Observe(
            new PowerShellCompilationSemanticOracleRequest(pin.ProfileId, fixture.ScriptPath)
            {
                HostExecutablePath = Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH"),
                ExpectedHostArtifactSha256 = pin.HostArtifactIdentitySha256
            });
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            Path.Combine(fixture.RootPath, "strict"),
            "RequiresDirectiveOracle",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0",
            SemanticProfileId = pin.ProfileId,
            SingleFile = false
        });

        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        var strict = new PowerShellCompilationSemanticRuntimeFreeArtifactObserver().Observe(pin.ProfileId, build);
        var allowed = new[] { "Encoding", "ExitCode" };
        Assert.Empty(PowerShellCompilationSemanticOracleComparer.Compare(interpreted, strict, allowed));
        var differences = PowerShellCompilationSemanticOraclePromotionGate.EnsurePromotable(
            semanticCase.FeatureId,
            new[] { interpreted, strict },
            allowed,
            "The interpreted script has no enclosing process exit contract and host encoding differs from the Strict UTF-8 executable contract.");
        Assert.Equal(
            new[] { "Encoding", "ExitCode" },
            differences.Select(static difference => difference.Path).OrderBy(static path => path, StringComparer.Ordinal));
    }

    private sealed class RejectingSemanticProcessRunner : IProcessRunner
    {
        public int InvocationCount { get; private set; }

        public Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            throw new InvalidOperationException("The process boundary must not be reached.");
        }
    }

    private sealed class OversizedSemanticProcessRunner : IProcessRunner
    {
        public int InvocationCount { get; private set; }

        public Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return Task.FromResult(new ProcessRunResult(
                0,
                new string('x', PowerShellCompilationSemanticOracleEnvelopeValidator.MaximumObservationTextCharacters + 1),
                string.Empty,
                request.FileName,
                TimeSpan.Zero,
                timedOut: false));
        }
    }

    private sealed class UnframedSemanticProcessRunner : IProcessRunner
    {
        internal string Protocol { get; private set; } = string.Empty;
        internal string Culture { get; private set; } = string.Empty;

        public Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            Protocol = request.EnvironmentVariables?["POWERFORGE_SEMANTIC_OBSERVATION_PROTOCOL"] ?? string.Empty;
            Culture = request.EnvironmentVariables?["POWERFORGE_SEMANTIC_OBSERVATION_CULTURE"] ?? string.Empty;
            return Task.FromResult(new ProcessRunResult(
                0,
                "42" + Environment.NewLine,
                string.Empty,
                request.FileName,
                TimeSpan.FromMilliseconds(1),
                timedOut: false));
        }
    }

    private sealed class InvalidUtf8SemanticProcessRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            var type = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("System.Int32"));
            var output = string.Join(Environment.NewLine, new[]
            {
                "PowerForge.StrictObservation/1|BEGIN||",
                $"PowerForge.StrictObservation/1|VALUE|{type}|/w==",
                "PowerForge.StrictObservation/1|END||MQ==",
                string.Empty
            });
            return Task.FromResult(new ProcessRunResult(
                0,
                output,
                string.Empty,
                request.FileName,
                TimeSpan.FromMilliseconds(1),
                timedOut: false));
        }
    }
}

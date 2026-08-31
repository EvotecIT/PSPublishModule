using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [Fact]
    public void JobObjectLaunchAccountingFailsClosedOnMissingOrSurplusPackets()
    {
        Assert.False(PowerShellCompilationSemanticWindowsProcessObserver.IsCompleteLaunchHistory(2, 3));
        Assert.True(PowerShellCompilationSemanticWindowsProcessObserver.IsCompleteLaunchHistory(3, 3));
        Assert.Throws<InvalidOperationException>(() =>
            PowerShellCompilationSemanticWindowsProcessObserver.IsCompleteLaunchHistory(4, 3));
    }

    [Fact]
    public void JobObjectAuthoredBoundaryRejectsEqualTimestamps()
    {
        Assert.False(PowerShellCompilationSemanticWindowsProcessObserver.IsAuthoredLaunch(99, 100));
        Assert.True(PowerShellCompilationSemanticWindowsProcessObserver.IsAuthoredLaunch(101, 100));
        Assert.Throws<InvalidOperationException>(() =>
            PowerShellCompilationSemanticWindowsProcessObserver.IsAuthoredLaunch(100, 100));
    }

    [Theory]
    [InlineData(PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId)]
    [InlineData(PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId)]
    public void ExternalOraclePreservesNullCollectionEncodingAndProcessEffects(string profileId)
    {
        using var fixture = OracleFixture.Create("""
$null = & $env:ComSpec /d /c 'exit 7'
$null = & $env:ComSpec /d /c 'exit 3'
[pscustomobject] [ordered] @{
    NullValue = $null
    EmptyCollection = [object[]] @()
    Pair = [object[]] @(1, 'two')
}
""");
        var observation = new PowerShellCompilationSemanticOracleRunner().Observe(
            new PowerShellCompilationSemanticOracleRequest(profileId, fixture.ScriptPath)
            {
                ObservedPropertyNames = new[] { "Pair", "NullValue", "EmptyCollection" }
            });

        Assert.Equal(3, observation.SchemaVersion);
        Assert.Equal("Output", observation.SuccessState);
        var value = Assert.Single(observation.Success);
        Assert.Equal(new[] { "NullValue", "EmptyCollection", "Pair" },
            value.Properties.Select(static property => property.Name));
        var nullValue = Assert.Single(value.Properties, static property => property.Name == "NullValue");
        Assert.True(nullValue.IsNull);
        Assert.Equal("Null", nullValue.ValueState);
        var empty = Assert.Single(value.Properties, static property => property.Name == "EmptyCollection");
        Assert.Equal("Collection", empty.EnumerationState);
        Assert.Equal(0, empty.CollectionCardinality);
        var pair = Assert.Single(value.Properties, static property => property.Name == "Pair");
        Assert.Equal(2, pair.CollectionCardinality);
        Assert.Equal(new[] { "System.Int32", "System.String" }, pair.ElementTypeNames);
        Assert.False(string.IsNullOrWhiteSpace(observation.Encoding.ConsoleOutput));
        Assert.False(string.IsNullOrWhiteSpace(observation.Encoding.PowerShellOutput));
        Assert.Equal(3, observation.ProcessState.LastExitCode);
        AssertDirectCommandEffects(observation, 7, 3);
        Assert.Null(observation.ExitCode);
    }

    [Fact]
    public void SchemaThreeRejectsInferredOrContradictoryProcessEffects()
    {
        var envelope = PromotableEnvelope("Strict", "42", includeHostArtifact: false);
        envelope.ProcessEffects = new[]
        {
            new PowerShellCompilationSemanticProcessEffectObservation
            {
                Sequence = 1,
                Invocation = 1,
                Kind = "NativeProcessLaunch",
                Executable = "tool.exe",
                ObservationSource = "LASTEXITCODE"
            }
        };

        var source = Assert.Throws<InvalidOperationException>(() =>
            PowerShellCompilationSemanticOracleEnvelopeValidator.Validate(envelope));
        Assert.Contains("observation source", source.Message, StringComparison.OrdinalIgnoreCase);

        envelope.ProcessEffects[0].ObservationSource = "Windows.JobObject.ProcessTree/1";
        envelope.ProcessEffects[0].ExitCode = 7;
        var shape = Assert.Throws<InvalidOperationException>(() =>
            PowerShellCompilationSemanticOracleEnvelopeValidator.Validate(envelope));
        Assert.Contains("launch cannot carry an exit code", shape.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExternalOracleCleansUpAStillRunningChildTreeAfterObservation()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = OracleFixture.Create("""
$process = Start-Process -FilePath $env:ComSpec -ArgumentList @('/d', '/c', 'ping -n 30 127.0.0.1 > nul') -PassThru
$process.Id
""");

        var observation = new PowerShellCompilationSemanticOracleRunner().Observe(
            new PowerShellCompilationSemanticOracleRequest(
                PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
                fixture.ScriptPath));

        var processId = int.Parse(Assert.Single(observation.Success).Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains(observation.ProcessEffects, static effect =>
            effect.Kind == "NativeProcessLaunch" &&
            effect.Executable.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase));
        Assert.True(SpinWait.SpinUntil(() => !IsProcessRunning(processId), TimeSpan.FromSeconds(3)));
    }

    [PinnedSemanticHostFact]
    public void DirectProcessEffectsExecuteOnConfiguredExactPowerShellProfiles()
    {
        var powerShell74Path = Environment.GetEnvironmentVariable("POWERFORGE_PWSH74_PATH");
        var powerShell76Path = Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH");
        Assert.False(string.IsNullOrWhiteSpace(powerShell74Path));
        Assert.False(string.IsNullOrWhiteSpace(powerShell76Path));
        var profiles = new[]
        {
            (PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId, (string?)null),
            (PowerShellCompilationSemanticOracleCatalog.PowerShell74ProfileId, powerShell74Path),
            (PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, powerShell76Path)
        };

        foreach (var profile in profiles)
        {
            using var fixture = OracleFixture.Create("$null = & $env:ComSpec /d /c 'exit 7'; 42");
            var pin = PowerShellCompilationSemanticHostArtifactPinCatalog.Get(profile.Item1);
            var observation = new PowerShellCompilationSemanticOracleRunner().Observe(
                new PowerShellCompilationSemanticOracleRequest(profile.Item1, fixture.ScriptPath)
                {
                    HostExecutablePath = profile.Item2,
                    ExpectedHostArtifactSha256 = pin.HostArtifactIdentitySha256
                });

            AssertDirectCommandEffects(observation, 7);
            Assert.Equal(7, observation.ProcessState.LastExitCode);
        }
    }

    private static void AssertDirectCommandEffects(
        PowerShellCompilationSemanticOracleEnvelope observation,
        params int[] exitCodes)
    {
        Assert.Equal(
            Enumerable.Range(1, observation.ProcessEffects.Length),
            observation.ProcessEffects.Select(static effect => effect.Sequence));
        Assert.All(observation.ProcessEffects, static effect =>
            Assert.Equal("Windows.JobObject.ProcessTree/1", effect.ObservationSource));
        var commands = observation.ProcessEffects
            .Where(static effect => effect.Executable.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Equal(exitCodes.Length * 2, commands.Length);
        for (var index = 0; index < exitCodes.Length; index++)
        {
            var launch = commands[index * 2];
            var exit = commands[index * 2 + 1];
            Assert.Equal("NativeProcessLaunch", launch.Kind);
            Assert.Null(launch.ExitCode);
            Assert.Equal("NativeProcessExit", exit.Kind);
            Assert.Equal(exitCodes[index], exit.ExitCode);
            Assert.Equal(launch.Invocation, exit.Invocation);
            Assert.True(launch.Sequence < exit.Sequence);
        }
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

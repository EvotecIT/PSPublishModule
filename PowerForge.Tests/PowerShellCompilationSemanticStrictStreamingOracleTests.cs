using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [PinnedSemanticHostFact]
    public void StrictStreamingFilterAndMapMatchBothPinnedHosts()
    {
        CompareStrictStreamingCases(
            ("FilterAndMap", FilterMap("-2, 0, 1, 3, 5", "$_ -gt 0", "[int] ($_ * 2)")),
            ("EmptyInput", FilterMap("@()", "$_ -ge 0", "$_")),
            ("NoRetainedRecords", FilterMap("-4, -3, 0", "$_ -gt 0", "$_")),
            ("SignedProductsStayInt32", FilterMap("-5, 4, -2, 4", "$_ -ne 0", "[int] ($PSItem * -2)")),
            ("ExplicitStageParameters", """
                [int[]] $values = 1, 2, 3
                $values | Where-Object -FilterScript { $_ -ge 2 } | ForEach-Object -Process { $_ + 3 }
                """),
            ("MultipleMaps", """
                1, 2, 3, 4 | ForEach-Object { $_ + 1 } | Where-Object { $PSItem -gt 3 } | ForEach-Object { 2 - $_ }
                """),
            ("NullableReferenceSource", """
                [string[]] $values = $null
                $values | Where-Object { $_ -eq $null } | ForEach-Object { 'matched' }
                """),
            ("LocalStreamingCommand", """
                function Get-Positive {
                    [int[]] $values = -1, 4, 2, -5
                    $values | Where-Object { $_ -gt 0 }
                }
                Get-Positive
                """),
            ("TrailingTypedRecords", StreamWithArrayTail("11, 12")),
            ("TrailingEmptyArray", StreamWithArrayTail("@()")),
            ("TrailingNullArray", StreamWithArrayTail("$null")),
            ("TrailingLocalArrayCall", """
                function Get-Tail {
                    [int[]] $tail = 11, 12
                    $tail
                }
                1, 2 | Where-Object { $_ -gt 0 }
                Get-Tail
                """));
    }

    [PinnedSemanticHostFact]
    public void StrictStreamingTakeStopsUpstreamOnBothPinnedHosts()
    {
        CompareStrictStreamingCases(
            ("TakeAndObserve", TakeMap("1, 2, 3, 4", 2)),
            ("TakeSingle", TakeMap("-3, 7, -2", 1)),
            ("FewerThanRequested", TakeMap("4, 7", 8)),
            ("EmptyInput", TakeMap("@()", 2)),
            ("DuplicateValuesAreSeparateRecords", TakeMap("1, 1, 1, 1", 2)),
            ("FilterBeforeCounter", TakeMap("-1, 2, 0, 4, 6", 2, filterBefore: "Where-Object { $_ -gt 0 } | ")),
            ("FilterAfterCounter", TakeMap("-1, 2, 0, 4, 6", 2, filterAfter: " | Where-Object { $_ -gt 0 }")),
            ("SelectDiscardsNullRecords", """
                [string[]] $values = $null
                $values | Select-Object -First 1
                """),
            ("NullDoesNotConsumeQuota", """
                [string[]] $values = 'placeholder', 'kept', 'later'
                [Array]::Clear($values, 0, 1)
                $values | Select-Object -First 1
                """));
    }

    [PinnedSemanticHostFact]
    public void StrictStreamingFilterAndLifecycleMatchBothPinnedHosts()
    {
        CompareStrictStreamingCases(
            ("FilterAndAggregate", FilterAggregate("-2, 0, 1, 3, 5", "$_ -gt 0")),
            ("EmptyStillCallsBeginAndEnd", FilterAggregate("@()", "$_ -gt 0", initial: 6)),
            ("AllFilteredStillCallsBeginAndEnd", FilterAggregate("-3, -2, 0", "$_ -gt 0", initial: 7)),
            ("OneRetainedRecord", FilterAggregate("-4, 8, 0", "$_ -gt 0", initial: 2)),
            ("NegativeRetainedRecords", FilterAggregate("-2, 0, -4, 9", "$PSItem -lt 0")),
            ("RetainsInputOrder", FilterAggregate("1, 3, -2, 2", "$_ -gt 0", orderSensitive: true)),
            ("RepeatedValuesReachProcess", FilterAggregate("7, 7, 7, -1", "$_ -gt 0")),
            ("MultipleFilters", FilterAggregate("1, 2, 3, 5, 8", "$_ -gt 1", downstream: "Where-Object { $_ -lt 8 } | ")));
    }

    [PinnedSemanticHostFact]
    public void StrictStreamingRejectsOverflowThatContinuesOnBothPinnedHosts()
    {
        var source = FilterAggregate("1, -2", "$true", initial: int.MaxValue);
        using var fixture = OracleFixture.Create(source);
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var interpreted = ObservePinnedScript(fixture, pin);
            Assert.Single(interpreted.ErrorRecords);
            Assert.False(interpreted.ErrorRecords[0].IsTerminating);
            Assert.Equal("2147483645", Assert.Single(interpreted.Success).Value);
            var build = BuildStrictStreamingFixture(fixture, "RejectOverflowingPipeline", pin.ProfileId);
            Assert.False(build.Succeeded);
            Assert.Contains("proven total", build.Error, StringComparison.Ordinal);
        }
    }

    [PinnedSemanticHostFact]
    public async Task StrictStreamingPreservesPriorOutputAndFailureExitOnBothPinnedHosts()
    {
        const string message = "pipeline failure boundary";
        using var fixture = OracleFixture.Create(FilterMap("1, 2, 3", "$_ -lt 3", "$_ * 2") +
            Environment.NewLine + "throw [System.InvalidOperationException]::new('" + message + "')");
        var runner = new ProcessRunner();
        var timeout = TimeSpan.FromSeconds(60);
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var observation = ObservePinnedScript(fixture, pin);
            Assert.True(Assert.Single(observation.ErrorRecords).IsTerminating);
            var build = BuildStrictStreamingFixture(fixture, "ThrowAfterStrictPipeline", pin.ProfileId);
            Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
            Assert.False(build.Manifest!.RequiresPowerShellRuntime);
            Assert.True(build.Manifest.DependencyClosureVerified);
            var hostPath = StrictStreamingHostPath(pin) ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            var expected = await runner.RunAsync(new ProcessRunRequest(hostPath, fixture.RootPath,
                new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath }, timeout));
            var actual = await runner.RunAsync(new ProcessRunRequest(build.ArtifactPath!, fixture.RootPath,
                Array.Empty<string>(), timeout));
            Assert.False(expected.TimedOut);
            Assert.False(actual.TimedOut);
            Assert.Equal(1, expected.ExitCode);
            Assert.Equal(expected.ExitCode, actual.ExitCode);
            Assert.Equal("2" + Environment.NewLine + "4", expected.StdOut.Trim());
            Assert.Equal(expected.StdOut.Trim(), actual.StdOut.Trim());
            Assert.Contains(message, expected.StdErr, StringComparison.Ordinal);
            Assert.Contains(message, actual.StdErr, StringComparison.Ordinal);
        }
    }

    [PinnedSemanticHostFact]
    public void StrictStreamingRejectsUnrepresentedSourceAndOutputEffectsOnBothPinnedHosts()
    {
        const string streamingCommand = """
            function Get-One {
                [CmdletBinding()]
                [OutputType([int])]
                param()
                1, 2 | Select-Object -First 1
            }
            """;
        var cases = new[]
        {
            ("RedirectedStage", "1, 2 | ForEach-Object { ($_ > $null) }", "stream redirection require native invocation binding", Array.Empty<string>()),
            ("SideEffectInArraySource", """
                [int] $seen = 0
                [int]($seen = 2147483647), 1 | ForEach-Object { $seen++; $_ } | Select-Object -First 1
                $seen
                """, "closed inline source", new[] { "2147483647", "2147483647" }),
            ("CaptureLocalStream", streamingCommand + Environment.NewLine +
                "[int] $result = (Get-One)\n$result", "consumed as a value", new[] { "1" }),
            ("DiscardLocalStream", streamingCommand + Environment.NewLine +
                "[void](Get-One)\n10", "writes success records", new[] { "10" })
        };
        foreach (var (name, source, diagnostic, expected) in cases)
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            using var fixture = OracleFixture.Create(source);
            var interpreted = ObservePinnedScript(fixture, pin);
            Assert.Equal(expected, interpreted.Success.Select(static item => item.Value));
            var build = BuildStrictStreamingFixture(fixture, name, pin.ProfileId);
            Assert.False(build.Succeeded, $"{name} ({pin.ProfileId}) must stay outside Strict.");
            Assert.True(build.Error?.Contains(diagnostic, StringComparison.Ordinal) == true,
                $"{name} ({pin.ProfileId}) should report '{diagnostic}'. {build.Error}");
        }

        using var nested = OracleFixture.Create("""
            1, 2 | Where-Object { $_ -gt 0 }
            (3, 4), (5, 6)
            """);
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var interpreted = ObservePinnedScript(nested, pin);
            Assert.Equal(new[] { "System.Int32", "System.Int32", "System.Object[]", "System.Object[]" },
                interpreted.Success.Select(static item => item.TypeName));
            var build = BuildStrictStreamingFixture(nested, "NestedArrayRecord", pin.ProfileId);
            Assert.False(build.Succeeded);
            Assert.Contains("Implicit streamed output currently requires stable scalar records", build.Error, StringComparison.Ordinal);
        }
    }

    private static string StreamWithArrayTail(string tail)
        => $$"""
            [int[]] $tail = {{tail}}
            1, 2 | Where-Object { $_ -gt 0 }
            $tail
            """;

    private static string FilterMap(string input, string predicate, string map)
        => $$"""
            [int[]] $values = {{input}}
            $values | Microsoft.PowerShell.Core\Where-Object { {{predicate}} } | Microsoft.PowerShell.Core\ForEach-Object { {{map}} }
            """;

    private static string TakeMap(string input, int count, string filterBefore = "", string filterAfter = "")
        => $$"""
            [int[]] $values = {{input}}
            [int] $seen = 0
            $values | {{filterBefore}}Microsoft.PowerShell.Core\ForEach-Object { $seen++; [int] ($_ * 2) }{{filterAfter}} | Microsoft.PowerShell.Utility\Select-Object -First {{count}}
            $seen
            """;

    private static string FilterAggregate(string input, string predicate, int initial = 0, bool orderSensitive = false, string downstream = "")
        => $$"""
            function Measure-PositiveTotal {
                [CmdletBinding()]
                param([Parameter(ValueFromPipeline)][int] $Value)
                begin { [int] $total = {{initial}} }
                process { {{(orderSensitive ? "$total *= 10; " : "")}}$total += $Value }
                end { $total }
            }
            [int[]] $values = {{input}}
            $values | Microsoft.PowerShell.Core\Where-Object { {{predicate}} } | {{downstream}}Measure-PositiveTotal
            """;

    private static void CompareStrictStreamingCases(params (string Name, string Source)[] cases)
    {
        var observer = new PowerShellCompilationSemanticRuntimeFreeArtifactObserver();
        foreach (var scenario in cases)
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            using var fixture = OracleFixture.Create(scenario.Source);
            var interpreted = ObservePinnedScript(fixture, pin);

            var build = BuildStrictStreamingFixture(fixture, scenario.Name, pin.ProfileId);
            var context = $"{scenario.Name} ({pin.ProfileId})";
            Assert.True(build.Succeeded, context + Environment.NewLine + build.Error + Environment.NewLine + build.BuildOutput);
            Assert.Empty(interpreted.ErrorRecords);
            // The observer verifies the artifact and its dependency inventory before launching it.
            var strict = observer.Observe(pin.ProfileId, build);
            Assert.Equal(0, strict.ExitCode);
            var differences = PowerShellCompilationSemanticOracleComparer.Compare(interpreted, strict, new[] { "Encoding", "ExitCode" });
            Assert.True(differences.Count == 0, context + Environment.NewLine + string.Join(Environment.NewLine, differences));
        }
    }

    private static PowerShellCompilationSemanticOracleEnvelope ObservePinnedScript(OracleFixture fixture,
        PowerShellCompilationSemanticHostArtifactPin pin)
        => new PowerShellCompilationSemanticOracleRunner().Observe(new PowerShellCompilationSemanticOracleRequest(pin.ProfileId, fixture.ScriptPath)
        {
            HostExecutablePath = StrictStreamingHostPath(pin),
            ExpectedHostArtifactSha256 = PowerShellCompilationSemanticTestHost.GetIdentity(pin)
        });

    private static string? StrictStreamingHostPath(PowerShellCompilationSemanticHostArtifactPin pin)
    {
        if (pin.ProfileId == PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId)
            return null;
        var path = Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH");
        Assert.False(string.IsNullOrWhiteSpace(path));
        return path;
    }

    private static PowerShellCompilationBuildResult BuildStrictStreamingFixture(OracleFixture fixture, string name, string profileId)
        => new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, Path.Combine(fixture.RootPath, "strict"), name,
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0",
            SemanticProfileId = profileId,
            SingleFile = false
        });
}

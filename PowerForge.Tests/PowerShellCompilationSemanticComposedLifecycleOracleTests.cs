using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [PinnedSemanticHostFact]
    public async Task StrictComposedLifecycleMatchesBothPinnedHosts()
    {
        const string source = """
            param([switch] $Take, [switch] $EmitLeft, [switch] $EmitRight, [switch] $FailLeft,
                  [switch] $FailRight, [switch] $FailRecord, [switch] $Empty, [switch] $Direct, [switch] $Repeat,
                  [switch] $Count, [switch] $CountDirect, [switch] $Helper, [switch] $HelperFinally,
                  [switch] $HelperNull, [switch] $RightHelper, [switch] $FilterFinallyRecord)

            function Send-Pair {
                [OutputType([int])]
                param([int] $Value, [switch] $EmitFinally, [switch] $EmitNull)
                try {
                    if ($EmitNull) { Write-Output $null }
                    $Value
                    if ($Value -eq 2) { 12 } else { 11 }
                    [IO.File]::AppendAllText('trace.txt', 'helper:resumed;')
                } catch {
                    [IO.File]::AppendAllText('trace.txt', 'helper:caught;')
                } finally {
                    [IO.File]::AppendAllText('trace.txt', 'helper:finally;')
                    if ($EmitFinally) {
                        Write-Output 99
                        [IO.File]::AppendAllText('trace.txt', 'helper:finally-resumed;')
                    }
                }
            }

            function Send-Record {
                [CmdletBinding()]
                [OutputType([int])]
                param([switch] $Emit, [Parameter(ValueFromPipeline)][int] $Record, [switch] $Fail, [switch] $Pair,
                      [switch] $PairFinally, [switch] $PairNull)
                begin {
                    [int] $seen = 0
                    [IO.File]::AppendAllText('trace.txt', 'left:begin;')
                    if ($Fail) { throw [InvalidOperationException]::new('left begin failure') }
                    if ($Emit) { 10 }
                    [IO.File]::AppendAllText('trace.txt', 'left:begin-resumed;')
                }
                process {
                    $seen++
                    [IO.File]::AppendAllText('trace.txt', "left:before:${Record}:${seen};")
                    try {
                        if ($Pair) { Send-Pair $Record -EmitFinally:$PairFinally -EmitNull:$PairNull } else { $Record }
                        [IO.File]::AppendAllText('trace.txt', 'left:resumed;')
                    } catch {
                        [IO.File]::AppendAllText('trace.txt', 'left:caught;')
                    } finally {
                        [IO.File]::AppendAllText('trace.txt', 'left:finally;')
                    }
                }
                end {
                    [IO.File]::AppendAllText('trace.txt', "left:end:${seen};")
                    30
                }
                # LEFT_CLEAN
            }

            function Receive-Record {
                [CmdletBinding()]
                [OutputType([int])]
                param([Parameter(ValueFromPipeline)][int] $Record, [switch] $Emit,
                      [switch] $FailBegin, [switch] $FailProcess, [switch] $Pair, [switch] $PairFinally,
                      [switch] $FilterFinallyRecord)
                begin {
                    [int] $seen = 0
                    [IO.File]::AppendAllText('trace.txt', 'right:begin;')
                    if ($FailBegin) { throw [InvalidOperationException]::new('right begin failure') }
                    if ($Emit) { 20 }
                    [IO.File]::AppendAllText('trace.txt', 'right:begin-resumed;')
                }
                process {
                    $seen++
                    [IO.File]::AppendAllText('trace.txt', "right:before:${Record}:${seen};")
                    if ($FailProcess) { throw [InvalidOperationException]::new('right process failure') }
                    if ($Pair) { Send-Pair $Record -EmitFinally:$PairFinally }
                    elseif (!$FilterFinallyRecord -or $Record -ne 99) { $Record }
                    [IO.File]::AppendAllText('trace.txt', 'right:resumed;')
                }
                end {
                    [IO.File]::AppendAllText('trace.txt', "right:end:${seen};")
                    40
                }
                # RIGHT_CLEAN
            }

            function Measure-RecordCount {
                [CmdletBinding()]
                [OutputType([int])]
                param([Parameter(ValueFromPipeline)][int] $Record)
                begin { [int] $total = 0 }
                process {
                    [IO.File]::AppendAllText('trace.txt', "measure:[${Record}];")
                    $total++
                }
                end { $total }
            }

            [int[]] $records = 1,2
            if ($Empty) { $records = @() }
            try {
                if ($Take) {
                    return $records | Send-Record -Emit:$EmitLeft -Fail:$FailLeft -Pair:$Helper -PairFinally:$HelperFinally -PairNull:$HelperNull |
                        Receive-Record -Emit:$EmitRight -FailBegin:$FailRight -Pair:$RightHelper -PairFinally:$HelperFinally -FilterFinallyRecord:$FilterFinallyRecord |
                        Select-Object -First 1
                }
                if ($Direct) { return $records | Send-Record -Emit:$EmitLeft | Select-Object -First 1 }
                if ($Repeat) { return $records | Send-Record | Send-Record }
                if ($Count) { return $records | Send-Record | Measure-RecordCount }
                if ($CountDirect) { return $records | Measure-RecordCount }
                return $records | Send-Record -Emit:$EmitLeft -Fail:$FailLeft -Pair:$Helper |
                    Receive-Record -FailBegin:$FailRight -FailProcess:$FailRecord
            } catch {
                [IO.File]::AppendAllText('trace.txt', 'outer:caught;')
                exit 7
            }
            """;

        var cases = new (string[] Options, int Code, string[] Records)[]
        {
            (Array.Empty<string>(), 0, new[] { "1", "2", "30", "40" }),
            (new[] { "Empty" }, 0, new[] { "30", "40" }),
            (new[] { "Take" }, 0, new[] { "1" }),
            (new[] { "Take", "EmitLeft" }, 0, new[] { "10" }),
            (new[] { "Take", "EmitRight" }, 0, new[] { "20" }),
            (new[] { "FailLeft" }, 7, Array.Empty<string>()),
            (new[] { "FailRight" }, 7, Array.Empty<string>()),
            (new[] { "FailRecord" }, 7, Array.Empty<string>()),
            (new[] { "Direct" }, 0, new[] { "1" }),
            (new[] { "Repeat" }, 0, new[] { "1", "2", "30", "30" }),
            (new[] { "Count" }, 0, new[] { "3" }),
            (new[] { "CountDirect" }, 0, new[] { "2" }),
            (new[] { "Helper" }, 0, new[] { "1", "11", "2", "12", "30", "40" }),
            (new[] { "Take", "Helper" }, 0, new[] { "1" }),
            (new[] { "Take", "Helper", "HelperFinally" }, 0, new[] { "1" }),
            (new[] { "Take", "RightHelper", "HelperFinally" }, 0, new[] { "1" }),
            (new[] { "Take", "Helper", "HelperFinally", "FilterFinallyRecord" }, 0, new[] { "1" }),
            (new[] { "Take", "Helper", "HelperNull" }, 0, new[] { "0" }),
            (new[] { "Helper", "FailRecord" }, 7, Array.Empty<string>())
        };
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var supportsClean = pin.ProfileId == PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId;
            using var fixture = OracleFixture.Create(supportsClean
                ? source.Replace("# LEFT_CLEAN", Cleanup("left"), StringComparison.Ordinal)
                    .Replace("# RIGHT_CLEAN", Cleanup("right"), StringComparison.Ordinal)
                : source);
            var build = BuildStrictStreamingFixture(fixture, "StrictComposedLifecycle", pin.ProfileId);
            Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
            Assert.False(build.Manifest!.RequiresPowerShellRuntime);
            Assert.True(build.Manifest.DependencyClosureVerified);
            Assert.DoesNotContain(build.Manifest.PublicAbi!.Methods.SelectMany(static method => method.Parameters),
                static parameter => parameter.TypeName.Contains("PowerShellStrictPipelineStage", StringComparison.Ordinal));
            var host = StrictStreamingHostPath(pin) ??
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var tracePath = Path.Combine(fixture.RootPath, "trace.txt");
            foreach (var scenario in cases)
            {
                var nativeArgs = new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath }
                    .Concat(scenario.Options.Select(static option => "-" + option)).ToArray();
                var native = await ObserveExitedConsole(host, fixture.RootPath, nativeArgs, string.Empty);
                Assert.Equal(scenario.Code, native.Code);
                Assert.Equal(scenario.Records, native.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
                Assert.Empty(native.Error);
                var trace = File.ReadAllText(tracePath);
                File.Delete(tracePath);
                var compiled = await ObserveExitedConsole(build.ArtifactPath!, fixture.RootPath,
                    scenario.Options.Select(static option => "--" + option).ToArray(), string.Empty);
                Assert.Equal(native, compiled);
                Assert.Equal(trace, File.ReadAllText(tracePath));
                File.Delete(tracePath);
            }
        }

        static string Cleanup(string side)
            => $$"""
                clean {
                    try { [IO.File]::AppendAllText('trace.txt', '{{side}}:clean;') } catch {}
                    99
                }
                """;
    }

    [PinnedSemanticHostFact]
    public async Task StrictComposedLifecycleBindsNullBeforeForwardingTypedScalarRecords()
    {
        using var fixture = OracleFixture.Create("""
            function Send-Record {
                [CmdletBinding()]
                param([Parameter(ValueFromPipeline)][string] $Record)
                begin { [IO.File]::AppendAllText('trace.txt', 'send:begin;') }
                process {
                    $null
                    $Record
                    [IO.File]::AppendAllText('trace.txt', 'send:resumed;')
                }
                end {}
            }
            function Measure-Record {
                [CmdletBinding()]
                [OutputType([int])]
                param([Parameter(ValueFromPipeline)][string] $Record)
                begin { [IO.File]::AppendAllText('trace.txt', 'measure:begin;') }
                process {
                    [IO.File]::AppendAllText('trace.txt', "measure:input:[${Record}];")
                    $Record.Length
                }
                end {}
            }
            [string[]] $data = 'abc','longer'
            return $data | Send-Record | Measure-Record | Select-Object -First 2
            """);
        var tracePath = Path.Combine(fixture.RootPath, "trace.txt");
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var build = BuildStrictStreamingFixture(fixture, "StrictComposedScalarTypes", pin.ProfileId);
            Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
            var host = StrictStreamingHostPath(pin) ??
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var original = await ObserveExitedConsole(host, fixture.RootPath,
                new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath }, string.Empty);
            Assert.Equal((0, $"0{Environment.NewLine}3{Environment.NewLine}", string.Empty), original);
            Assert.Equal("send:begin;measure:begin;measure:input:[];measure:input:[abc];", File.ReadAllText(tracePath));
            File.Delete(tracePath);
            var compiled = await ObserveExitedConsole(build.ArtifactPath!, fixture.RootPath, Array.Empty<string>(), string.Empty);
            Assert.Equal(original, compiled);
            Assert.Equal("send:begin;measure:begin;measure:input:[];measure:input:[abc];", File.ReadAllText(tracePath));
            File.Delete(tracePath);
            var observer = new PowerShellCompilationSemanticRuntimeFreeArtifactObserver();
            var observed = observer.Observe(pin.ProfileId, build, Array.Empty<string>());
            Assert.Equal(2, observed.Success.Length);
            Assert.All(observed.Success, static record => Assert.Equal("System.Int32", record.TypeName));
        }
    }

    [PinnedSemanticHostFact]
    public async Task StrictComposedLifecycleDescribesNullRecordsAndSkipsThemForFirst()
    {
        using var fixture = OracleFixture.Create("""
            param([string] $Mode = 'all')
            function Send-Text {
                [CmdletBinding()]
                param([Parameter(ValueFromPipeline)][string] $Value)
                begin {}
                process { $Value }
                end {}
            }
            function Receive-Text {
                [CmdletBinding()]
                [OutputType([string])]
                param([Parameter(ValueFromPipeline)][string] $Value)
                begin {}
                process { $null; $Value }
                end {}
            }
            function Invoke-Composition {
                [OutputType([string])]
                param([string] $Selection)
                [string[]] $values = @('record')
                if ($Selection -eq 'first') { return $values | Send-Text | Receive-Text | Select-Object -First 1 }
                return $values | Send-Text | Receive-Text
            }
            return Invoke-Composition $Mode
            """);
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var build = BuildStrictStreamingFixture(fixture, "StrictComposedNullResult", pin.ProfileId);
            Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
            var observer = new PowerShellCompilationSemanticRuntimeFreeArtifactObserver();
            var native = new PowerShellCompilationSemanticOracleRunner();
            foreach (var mode in new[] { "all", "first" })
            {
                var reference = native.Observe(new PowerShellCompilationSemanticOracleRequest(pin.ProfileId, fixture.ScriptPath)
                {
                    Arguments = new[] { mode },
                    HostExecutablePath = StrictStreamingHostPath(pin),
                    ExpectedHostArtifactSha256 = PowerShellCompilationSemanticTestHost.GetIdentity(pin)
                });
                var observed = observer.Observe(pin.ProfileId, build, new[] { mode });
                var differences = PowerShellCompilationSemanticOracleComparer.Compare(reference, observed, new[] { "Encoding", "ExitCode" });
                Assert.True(differences.Count == 0, string.Join(Environment.NewLine,
                    differences.Select(static difference => $"{difference.Path}: {difference.Expected} != {difference.Actual}")));
                Assert.Equal(mode == "all" ? 2 : 1, observed.Success.Length);
            }
        }
    }

    [PinnedSemanticHostFact]
    public async Task StrictComposedLifecyclePreservesDirectCallsWhenCompositionIsUnreachable()
    {
        const string declarations = """
            function Send-Number {
                [CmdletBinding()]
                [OutputType([int])]
                param([switch] $Echo, [Parameter(ValueFromPipeline)][int] $Value)
                begin {}
                process { if ($Echo) { $Value } }
                end { 7 }
            }
            function Receive-Number {
                [CmdletBinding()]
                [OutputType([int])]
                param([Parameter(ValueFromPipeline)][int] $Value)
                begin {}
                process { $Value }
                end {}
            }
            """;
        using var fixture = OracleFixture.Create(declarations + """

            if ($false) { 1,2 | Send-Number -Echo | Receive-Number }
            return 1,2 | Send-Number -Echo | Select-Object -First 1
            """);
        using var mismatch = OracleFixture.Create(declarations.Replace("process { if ($Echo) { $Value } }", "process {}",
                StringComparison.Ordinal).Replace("end { 7 }", "end { [long] 7 }", StringComparison.Ordinal) +
            Environment.NewLine + "return 1,2 | Send-Number | Receive-Number");
        using var quiet = OracleFixture.Create(declarations.Replace("process { if ($Echo) { $Value } }",
                "process { if ($false) { $Value } }", StringComparison.Ordinal).Replace("end { 7 }", "end {}", StringComparison.Ordinal) + """

            if ($false) { 1,2 | Send-Number -Echo | Receive-Number }
            return 1,2 | Send-Number -Echo | Select-Object -First 1
            """);
        using var collecting = OracleFixture.Create(declarations + """

            1,2 | Send-Number -Echo | Receive-Number
            return 3,4 | Send-Number -Echo
            """);
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var build = BuildStrictStreamingFixture(fixture, "StrictDeadComposition", pin.ProfileId);
            Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
            var host = StrictStreamingHostPath(pin) ??
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var native = await ObserveExitedConsole(host, fixture.RootPath,
                new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath }, string.Empty);
            Assert.Equal((0, "1" + Environment.NewLine, string.Empty), native);
            Assert.Equal(native, await ObserveExitedConsole(build.ArtifactPath!, fixture.RootPath, Array.Empty<string>(), string.Empty));
            var rejected = BuildStrictStreamingFixture(mismatch, "StrictMismatchedComposition", pin.ProfileId);
            Assert.False(rejected.Succeeded);
            Assert.Contains("homogeneous stable-scalar", rejected.Error, StringComparison.Ordinal);
            var quietBuild = BuildStrictStreamingFixture(quiet, "StrictQuietComposition", pin.ProfileId);
            Assert.True(quietBuild.Succeeded, quietBuild.Error + Environment.NewLine + quietBuild.BuildOutput);
            var nativeQuiet = await ObserveExitedConsole(host, quiet.RootPath,
                new[] { "-NoProfile", "-NonInteractive", "-File", quiet.ScriptPath }, string.Empty);
            Assert.Equal((0, string.Empty, string.Empty), nativeQuiet);
            Assert.Equal(nativeQuiet,
                await ObserveExitedConsole(quietBuild.ArtifactPath!, quiet.RootPath, Array.Empty<string>(), string.Empty));
            var collectingBuild = BuildStrictStreamingFixture(collecting, "StrictCollectingComposition", pin.ProfileId);
            Assert.True(collectingBuild.Succeeded, collectingBuild.Error + Environment.NewLine + collectingBuild.BuildOutput);
            var method = Assert.Single(collectingBuild.Manifest!.PublicAbi!.Methods,
                static method => method.PowerShellName == "Send-Number");
            Assert.Equal(typeof(int[]).FullName, method.ReturnType);
            var originalCollecting = await ObserveExitedConsole(host, collecting.RootPath,
                new[] { "-NoProfile", "-NonInteractive", "-File", collecting.ScriptPath }, string.Empty);
            Assert.Equal(new[] { "1", "2", "7", "3", "4", "7" },
                originalCollecting.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
            Assert.Equal(originalCollecting,
                await ObserveExitedConsole(collectingBuild.ArtifactPath!, collecting.RootPath, Array.Empty<string>(), string.Empty));
        }
    }
}

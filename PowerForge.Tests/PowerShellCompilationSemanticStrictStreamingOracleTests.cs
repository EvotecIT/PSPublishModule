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
    public void StrictStreamingInterpolatesScalarSubexpressionsOnBothPinnedHosts()
    {
        CompareStrictStreamingCases(
            ("ScalarInterpolation", """
                [string] $label = 'example'
                return "length=$($label.Length) empty=[$($null)] computed=$(1 + 2)"
                """));
    }

    [PinnedSemanticHostFact]
    public void StrictStreamingRejectsOpenWriteOutputArgumentBinding()
    {
        var cases = new[]
        {
            ("Splat", "[int[]] $values = 1, 2; Write-Output @values"),
            ("AttachedParameterWithAnExtraArgument", "Write-Output -InputObject:first second")
        };
        foreach (var scenario in cases)
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            using var fixture = OracleFixture.Create(scenario.Item2);
            var build = BuildStrictStreamingFixture(fixture, scenario.Item1, pin.ProfileId);
            Assert.False(build.Succeeded);
            Assert.Contains("Write-Output", build.Error, StringComparison.OrdinalIgnoreCase);
        }
    }

    [PinnedSemanticHostFact]
    public void StrictStreamingAllowsExplicitExitOnAnOtherwiseValueReturningPath()
    {
        CompareStrictStreamingCases(
            ("ConditionalExit", """
                param([int] $Status = 0)
                if ($Status -ne 0) { exit $Status } else { return 7 }
                """),
            ("ValueReturnedAfterCleanupKeepsCallableAbi", """
                function Get-Value {
                    param([int] $Status)
                    [int] $value = 0
                    try { $value = 7 }
                    finally { if ($Status -ne 0) { exit $Status } }
                    return $value
                }
                [int] $result = Get-Value 0
                return $result
                """));
    }

    [PinnedSemanticHostFact]
    public void StrictStreamingWriteOutputEnumeratesTypedValuesOnBothPinnedHosts()
    {
        CompareStrictStreamingCases(
            ("ExplicitTypedOutputRecords", """
                [string] $name = 'title'
                Microsoft.PowerShell.Utility\Write-Output -InputObject "header=$name"
                Write-Output ''
                Write-Output @()
                [string[]] $values = 'before', 'placeholder', 'after'
                [Array]::Clear($values, 1, 1)
                Write-Output $values
                Write-Output $null
                """),
            ("EmptyInputProducesNoRecords", "Write-Output @()"),
            ("EmptyOutputPreservesOtherContinuations", """
                function Get-One { return 1 }
                Get-One
                Write-Output @()
                return 2
                """),
            ("NullInputProducesOneNullRecord", "Write-Output $null"));
    }

    [PinnedSemanticHostFact]
    public void StrictStreamingProjectsRecursiveFileSourceOnBothPinnedHosts()
    {
        using var fixture = OracleFixture.Create("""
            param([string] $Path)
            Get-ChildItem -Path $Path -Recurse -Attributes !Directory |
                Select-Object -ExpandProperty FullName | Select-Object -First 2
            """);
        var tree = Directory.CreateDirectory(Path.Combine(fixture.RootPath, "tree")).FullName;
        var nested = Directory.CreateDirectory(Path.Combine(tree, "alpha")).FullName;
        File.WriteAllText(Path.Combine(tree, "c.txt"), "root c");
        File.WriteAllText(Path.Combine(tree, "A.txt"), "root a");
        File.WriteAllText(Path.Combine(nested, "inner.txt"), "nested");
        var hidden = Path.Combine(tree, "b.txt");
        File.WriteAllText(hidden, "hidden");
        File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        var observer = new PowerShellCompilationSemanticRuntimeFreeArtifactObserver();
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var build = BuildStrictStreamingFixture(fixture, "StrictFileProjection", pin.ProfileId);
            Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
            foreach (var path in new[] { tree, Path.Combine(tree, "*.txt"), Path.Combine(tree, "missing") })
            {
                var expected = new PowerShellCompilationSemanticOracleRunner().Observe(
                    new PowerShellCompilationSemanticOracleRequest(pin.ProfileId, fixture.ScriptPath)
                    {
                        Arguments = new[] { path },
                        HostExecutablePath = StrictStreamingHostPath(pin),
                        ExpectedHostArtifactSha256 = PowerShellCompilationSemanticTestHost.GetIdentity(pin)
                    });
                var artifact = observer.Observe(pin.ProfileId, build, new[] { path });
                Assert.Empty(expected.ErrorRecords);
                Assert.Equal(0, artifact.ExitCode);
                var differences = PowerShellCompilationSemanticOracleComparer.Compare(expected, artifact, new[] { "Encoding", "ExitCode" });
                Assert.True(differences.Count == 0, pin.ProfileId + Environment.NewLine + string.Join(Environment.NewLine, differences));
            }
        }
    }

    [PinnedSemanticHostFact]
    public async Task StrictStreamingPreservesOrderedFilePropertyRecordsAndConsoleOutput()
    {
        using var fixture = OracleFixture.Create("""
            param([string] $Path, [int] $ExitAfter = 0)
            Get-ChildItem -Path $Path -Recurse | Select-Object -Property Name,FullName
            if ($ExitAfter -eq 1) { exit 0 }
            if ($ExitAfter -ge 7) { 'complete' }
            if ($ExitAfter -eq 7) { exit 7 }
            if ($ExitAfter -eq 9) {
                Get-ChildItem -Path $Path -Recurse | Select-Object -Property Name,FullName
            }
            """);
        var tree = Directory.CreateDirectory(Path.Combine(fixture.RootPath, "items")).FullName;
        Directory.CreateDirectory(Path.Combine(tree, "nested"));
        File.WriteAllText(Path.Combine(tree, "a.txt"), "one");
        File.WriteAllText(Path.Combine(tree, "longer.txt"), "two");
        var observer = new PowerShellCompilationSemanticRuntimeFreeArtifactObserver();
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var build = BuildStrictStreamingFixture(fixture, "StrictPropertyRecords", pin.ProfileId);
            Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
            var original = new PowerShellCompilationSemanticOracleRunner().Observe(
                new PowerShellCompilationSemanticOracleRequest(pin.ProfileId, fixture.ScriptPath)
                {
                    Arguments = new[] { tree },
                    ObservedPropertyNames = new[] { "Name", "FullName" },
                    HostExecutablePath = StrictStreamingHostPath(pin),
                    ExpectedHostArtifactSha256 = PowerShellCompilationSemanticTestHost.GetIdentity(pin)
                });
            Assert.Empty(original.ErrorRecords);
            Assert.Equal(3, original.Success.Length);
            var artifact = observer.Observe(pin.ProfileId, build, new[] { tree });
            var differences = PowerShellCompilationSemanticOracleComparer.Compare(original, artifact, new[] { "Encoding", "ExitCode" });
            Assert.True(differences.Count == 0, pin.ProfileId + Environment.NewLine + string.Join(Environment.NewLine, differences));
            var mixed = observer.Observe(pin.ProfileId, build, new[] { tree, "7" });
            Assert.Equal(7, mixed.ExitCode);
            Assert.Equal(4, mixed.Success.Length);
            Assert.Equal(original.Success.Select(static row => row.TypeName), mixed.Success.Take(3).Select(static row => row.TypeName));
            Assert.Equal("System.String", mixed.Success[3].TypeName);
            var host = StrictStreamingHostPath(pin) ??
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            foreach (var status in new[] { "0", "1", "7", "8", "9" })
            {
                var native = await ObserveExitedConsole(host, fixture.RootPath,
                    new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath, tree, status }, string.Empty);
                var compiled = await ObserveExitedConsole(build.ArtifactPath!, fixture.RootPath, new[] { tree, status }, string.Empty);
                Assert.Equal(native, compiled);
            }
        }
    }

    [PinnedSemanticHostFact]
    public async Task StrictCsvRowsMatchBothPinnedHosts()
    {
        const string script = """
            param([string] $Path)
            try {
                $rows = Import-Csv -LiteralPath $Path -Header Title,Author,Notes
                foreach ($row in $rows) {
                    Write-Output "title=[$($row.Title)] author=[$($row.Author)] notes=[$($row.Notes)]"
                }
                exit 0
            } catch {
                "Error: $($Error[0]) (line $($_.InvocationInfo.ScriptLineNumber))"
                exit 1
            }
            """;
        using var fixture = OracleFixture.Create(script);
        using var wildcardFixture = OracleFixture.Create(script.Replace("-LiteralPath", "-Path", StringComparison.Ordinal));
        var rows = Path.Combine(fixture.RootPath, "data[42].csv");
        var empty = Path.Combine(fixture.RootPath, "empty.csv");
        var missing = Path.Combine(fixture.RootPath, "missing.csv");
        File.WriteAllText(rows, "first,\"Ada, B.\",\"a\"\"b\"\r\nsecond,Doe\r\nthird,Lee,\"line 1\r\nline 2\"\r\n\r\n");
        File.WriteAllText(empty, string.Empty);
        var observer = new PowerShellCompilationSemanticRuntimeFreeArtifactObserver();
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var build = BuildStrictStreamingFixture(fixture, "StrictCsvFields", pin.ProfileId);
            Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
            var host = StrictStreamingHostPath(pin) ??
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            foreach (var path in new[] { rows, empty, missing })
            {
                var native = await ObserveExitedConsole(host, fixture.RootPath,
                    new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath, path }, string.Empty);
                var compiled = await ObserveExitedConsole(build.ArtifactPath!, fixture.RootPath, new[] { path }, string.Empty);
                Assert.Equal(path == missing ? 1 : 0, native.Code);
                Assert.Equal(native.Code, compiled.Code);
                Assert.Equal(native.Output, compiled.Output);
                Assert.Equal(native.Error, compiled.Error);
                var observation = observer.Observe(pin.ProfileId, build, new[] { path });
                Assert.Equal(native.Code, observation.ExitCode);
                Assert.Equal(path == rows ? 3 : path == missing ? 1 : 0, observation.Success.Length);
            }
            var wildcardBuild = BuildStrictStreamingFixture(wildcardFixture, "StrictCsvPattern", pin.ProfileId);
            Assert.True(wildcardBuild.Succeeded, wildcardBuild.Error + Environment.NewLine + wildcardBuild.BuildOutput);
            var ambiguous = Path.Combine(fixture.RootPath, "*.csv");
            var original = await ObserveExitedConsole(host, fixture.RootPath,
                new[] { "-NoProfile", "-NonInteractive", "-File", wildcardFixture.ScriptPath, ambiguous }, string.Empty);
            var generated = await ObserveExitedConsole(wildcardBuild.ArtifactPath!, fixture.RootPath, new[] { ambiguous }, string.Empty);
            Assert.Equal(1, original.Code);
            Assert.Contains("path resolved to more than one file", original.Output, StringComparison.Ordinal);
            Assert.Equal(original, generated);
        }
    }

    [PinnedSemanticHostFact]
    public async Task StrictFileProcessingMatchesBothPinnedHosts()
    {
        using var fixture = OracleFixture.Create("""
            param([string] $Path = "$PWD", [int] $Initial = 0, [double] $Elapsed = 2.5, [switch] $ResolveOnly)
            try {
                $Path = Resolve-Path "$Path"
                if ($ResolveOnly) { return $Path }
                Write-Progress "Reading $Path"
                [int] $count = $Initial
                Get-ChildItem $Path -Attributes !Directory -Recurse |
                    Where-Object { $_.Length -eq 0 } |
                    ForEach-Object { "file=$($_.Name)"; $count++ }
                Write-Progress -Completed "Reading $Path"
                [int] $seconds = $Elapsed
                "count=$count; path=$Path; seconds=$seconds"
                exit 0
            } catch {
                "Error: $($Error[0]) (line $($_.InvocationInfo.ScriptLineNumber))"
                exit 1
            }
            """);
        var tree = Path.Combine(fixture.RootPath, "tree");
        var nested = Directory.CreateDirectory(Path.Combine(tree, "child")).FullName;
        File.WriteAllText(Path.Combine(tree, "a.txt"), string.Empty);
        File.WriteAllText(Path.Combine(tree, "full.txt"), "data");
        File.WriteAllText(Path.Combine(nested, "b.txt"), string.Empty);
        var missing = Path.Combine(tree, "missing");
        var cases = new (string[] Arguments, int Code)[]
        {
            (Array.Empty<string>(), 0),
            (new[] { "." }, 0),
            (new[] { nested, "0", "3.5" }, 0),
            (new[] { nested, "0", "2147483648.123456" }, 1),
            (new[] { "~", "-ResolveOnly" }, 0),
            (new[] { Path.Combine(tree, "ch?ld", "*.txt"), "-ResolveOnly" }, 0),
            (new[] { tree, int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture) }, 1),
            (new[] { "" }, 1),
            (new[] { missing }, 0)
        };
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var build = BuildStrictStreamingFixture(fixture, "StrictFileProcessing", pin.ProfileId);
            Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
            Assert.True(build.Manifest!.DependencyClosureVerified);
            Assert.False(build.Manifest.RequiresPowerShellRuntime);
            var host = StrictStreamingHostPath(pin) ??
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            foreach (var scenario in cases)
            {
                var hostArguments = new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath }
                    .Concat(scenario.Arguments).ToArray();
                var native = await ObserveExitedConsole(host, tree, hostArguments, string.Empty);
                var compiledArguments = scenario.Arguments.Select(static argument => argument == "-ResolveOnly" ? "--ResolveOnly" : argument).ToArray();
                var compiled = await ObserveExitedConsole(build.ArtifactPath!, tree, compiledArguments, string.Empty);
                Assert.Equal(scenario.Code, native.Code);
                Assert.Equal(native.Code, compiled.Code);
                Assert.Equal(native.Output, compiled.Output);
                if (scenario.Arguments.FirstOrDefault() == missing)
                {
                    // Detached hosts render error records using different width and path conventions.
                    Assert.Contains("Resolve-Path", native.Error);
                    Assert.Contains("Resolve-Path", compiled.Error);
                    Assert.Contains(missing, native.Error.Replace(Environment.NewLine, string.Empty));
                    Assert.Contains(missing, compiled.Error.Replace(Environment.NewLine, string.Empty));
                }
                else
                    Assert.Equal(native.Error, compiled.Error);
            }
        }
    }

    [PinnedSemanticHostFact]
    public async Task StrictLocalLifecycleStopsBeforeResumingTheProducerOnBothPinnedHosts()
    {
        const string function = """
            function Get-Record {
                [CmdletBinding()]
                [OutputType([int])]
                param([Parameter(ValueFromPipeline)][int] $Record, [switch] $Mark, [switch] $Fail)
                begin {
                    [IO.File]::AppendAllText('trace.txt', "begin=$Mark;")
                    [int[]] $initial = -1,-2
                    foreach ($item in $initial) { $item }
                    [IO.File]::AppendAllText('trace.txt', 'after begin;')
                }
                process {
                    [IO.File]::AppendAllText('trace.txt', "before ${Record};")
                    if ($Fail -and $Record -eq 2) { throw [InvalidOperationException]::new('failure after output') }
                    $Record
                    [IO.File]::AppendAllText('trace.txt', "after ${Record};")
                }
                end {
                    [IO.File]::AppendAllText('trace.txt', 'end;')
                    99
                }
            """;
        const string invocation = """
            }
            if ($First) { return 1,2 | Get-Record -Mark | Select-Object -First 3 }
            if ($BeginOnly) { return 1,2 | Get-Record -Mark | Select-Object -First 1 }
            if ($Fail) {
                try { return 1,2 | Get-Record -Mark -Fail }
                catch { exit 7 }
            }
            return 1,2 | Get-Record -Mark
            """;
        const string cleanup = """
                clean {
                    [IO.File]::AppendAllText('trace.txt', 'clean;')
                    -88
                }
            """;
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var host = StrictStreamingHostPath(pin) ??
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var supportsCleanClause = pin.ProfileId == PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId;
            var versions = supportsCleanClause ? new[] { false, true } : new[] { false };
            foreach (var withClean in versions)
            {
                using var fixture = OracleFixture.Create("param([switch] $First, [switch] $BeginOnly, [switch] $Fail)\n" + function + "\n" +
                    (withClean ? cleanup + "\n" : string.Empty) + invocation);
                var tracePath = Path.Combine(fixture.RootPath, "trace.txt");
                var build = BuildStrictStreamingFixture(fixture, "StrictLocalLifecycle", pin.ProfileId);
                Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
                Assert.False(build.Manifest!.RequiresPowerShellRuntime);
                Assert.True(build.Manifest.DependencyClosureVerified);
                var cases = new[]
                {
                    (Option: "", Code: 0, Records: new[] { "-1", "-2", "1", "2", "99" },
                        Trace: "begin=True;after begin;before 1;after 1;before 2;after 2;end;"),
                    (Option: "First", Code: 0, Records: new[] { "-1", "-2", "1" }, Trace: "begin=True;after begin;before 1;"),
                    (Option: "BeginOnly", Code: 0, Records: new[] { "-1" }, Trace: "begin=True;"),
                    (Option: "Fail", Code: 7, Records: new[] { "-1", "-2", "1" }, Trace: "begin=True;after begin;before 1;after 1;before 2;")
                };
                foreach (var scenario in cases)
                {
                    var hostArgs = new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath };
                    if (scenario.Option.Length != 0) hostArgs = hostArgs.Append("-" + scenario.Option).ToArray();
                    var expected = await ObserveExitedConsole(host, fixture.RootPath, hostArgs, string.Empty);
                    Assert.Equal(scenario.Code, expected.Code);
                    Assert.Equal(scenario.Records, expected.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries));
                    var expectedTrace = scenario.Trace;
                    if (withClean) expectedTrace += "clean;";
                    Assert.Equal(expectedTrace, File.ReadAllText(tracePath));
                    File.Delete(tracePath);
                    var candidateArgs = scenario.Option.Length == 0 ? Array.Empty<string>() : new[] { "--" + scenario.Option };
                    var actual = await ObserveExitedConsole(build.ArtifactPath!, fixture.RootPath, candidateArgs, string.Empty);
                    Assert.Equal(expected, actual);
                    Assert.Equal(expectedTrace, File.ReadAllText(tracePath));
                    File.Delete(tracePath);
                }
            }
        }
    }

    [PinnedSemanticHostFact]
    public void StrictDataCommandErrorObservationRejectsOtherErrorProducers()
    {
        var scripts = new[]
        {
            """
            function Read-Host { return 'local' }
            try { $answer = Read-Host } catch { "line=$($_.InvocationInfo.ScriptLineNumber)" }
            """,
            """
            param([switch] $Fault)
            try {
                if ($Fault) { throw [InvalidOperationException]::new('earlier failure') }
                $answer = Read-Host 'Input'
            } catch { "line=$($_.InvocationInfo.ScriptLineNumber)" }
            """,
            """
            function Read-Text { $answer = Read-Host 'Input'; return $answer }
            Read-Text
            """
        };
        foreach (var script in scripts)
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            using var fixture = OracleFixture.Create(script);
            var build = BuildStrictStreamingFixture(fixture, "StrictCommandScope", pin.ProfileId);
            Assert.False(build.Succeeded);
            Assert.Contains("every source-closure unit", build.Error, StringComparison.OrdinalIgnoreCase);
        }
    }

    [PinnedSemanticHostFact]
    public void StrictStreamingExpandsKnownPropertiesOnBothPinnedHosts()
    {
        CompareStrictStreamingCases(
            ("ExpansionDropsNullAndKeepsZeroLength", """
                [string[]] $values = 'one', 'placeholder', '', 'one', 'aa'
                [Array]::Clear($values, 1, 1)
                $values | Select-Object -ExpandProperty Length
                """),
            ("AliasedPropertyExpansionStreamsIntoCapture", """
                [string[]] $values = 'pie', '', 'other', 'too-long'
                [int[]] $result = @($values | select -ExpandProperty lEnGtH | where { $_ -gt 0 } | % { $_ } | select -First 2)
                $result
                """),
            ("TimeSpanTicksRetainInt64Type", """
                [TimeSpan] $first = [TimeSpan]::new([long] 40000)
                [TimeSpan] $second = [TimeSpan]::new([long] -20000)
                [TimeSpan[]] $values = $first, $second
                $values | Microsoft.PowerShell.Utility\Select-Object -ExpandProperty Ticks
                """));
    }

    [PinnedSemanticHostFact]
    public void StrictStreamingRunsWithinTypedControlFlowOnBothPinnedHosts()
    {
        CompareStrictStreamingCases(
            ("PipelineInTryThenCatchAndFinally", """
                try {
                    1, 2 | ForEach-Object { $_ }
                    throw [System.InvalidOperationException]::new('go to catch')
                } catch {
                    3, 4 | Select-Object -First 1
                } finally {
                    5, 6 | Where-Object { $_ -eq 6 }
                }
                """),
            ("CaptureInsideAnIf", """
                [int[]] $result = 8, 9
                if ($result.Length -eq 2) {
                    [int[]] $result = @('one', '', 'four' | Select-Object -ExpandProperty Length | Where-Object { $_ -gt 0 })
                }
                $result
                """),
            ("PipelineReturnEmitsBeforeFinally", """
                function Get-Results {
                    try {
                        return 'one', 'two', 'three' | Select-Object -First 2
                    } finally {
                        'finally', 'ignored' | Select-Object -First 1
                    }
                }
                Get-Results
                """),
            ("PipelineRestartsEachLoopIteration", """
                foreach ($number in 7, 8, 9) {
                    1, 2, 3 | ForEach-Object { $_ } | Select-Object -First 1
                }
                """));
    }

    [PinnedSemanticHostFact]
    public void StrictStreamingResumesMapBodyAfterDownstreamOnBothPinnedHosts()
    {
        CompareStrictStreamingCases(
            ("CountersOnEitherSideOfDownstreamProcessing", """
                [int[]] $values = 1, 2, 3, 4
                [int] $before = 0
                [int] $outer = 0
                [int] $inner = 0
                $values | ForEach-Object { $before++; $_ } | ForEach-Object { $_; $outer++ } | ForEach-Object { $_; $inner++ } | Where-Object { $_ -gt 2 } | Select-Object -First 1
                if ($before -ne 3 -or $outer -ne 2 -or $inner -ne 2) {
                    throw [System.InvalidOperationException]::new('Stages resumed at the wrong point.')
                }
                $before
                """),
            ("DownstreamFilterStillResumesProducer", """
                [int[]] $values = 1, 2, 3
                [int] $count = 0
                $values | ForEach-Object { $_; $count++ } | Where-Object { $_ -gt 3 }
                $count
                """));
    }

    [PinnedSemanticHostFact]
    public void StrictStreamingCollectsTypedArraysWithNativeRecordSemantics()
    {
        CompareStrictStreamingCases(
            ("CollectedRecordsStayInOrder", """
                [int[]] $values = 3, 1, 2, 3
                [int[]] $result = @($values | Where-Object { $_ -gt 1 } | ForEach-Object { $_ * 2 })
                $result
                """),
            ("EmptyCaptureProducesAnArray", """
                [int[]] $values = 1, 2
                [int[]] $result = @($values | Where-Object { $_ -gt 2 })
                $result.Length
                """),
            ("CollectedTakeStopsUpstream", """
                [int[]] $values = 2, 1, 4
                [int] $seen = 0
                [int[]] $result = @($values | Where-Object { $_ -gt 1 } | ForEach-Object { $seen++; [int] ($_ * 2) } | Select-Object -First 1)
                if ($seen -ne 1) { throw [System.InvalidOperationException]::new('Upstream did not stop.') }
                $result
                """),
            ("TypedStringArrayNormalizesNull", """
                [string[]] $values = 'drop', 'keep'
                [Array]::Clear($values, 0, 1)
                [string[]] $result = @($values | Where-Object { $true })
                $result
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
            ("DiscardLocalStreamWithVoid", streamingCommand + Environment.NewLine +
                "[void](Get-One)\n10", "writes success records", new[] { "10" }),
            ("DiscardLocalStreamWithNullAssignment", streamingCommand + Environment.NewLine +
                "$null = Get-One\n10", "writes success records", new[] { "10" })
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

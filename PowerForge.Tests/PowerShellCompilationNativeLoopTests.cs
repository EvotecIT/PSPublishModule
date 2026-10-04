using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeLoops_PreserveInvocationCountersOutputAndErrorBoundaries(string framework, string host)
    {
        var loops = new[] {
            ("For", "for ($i=0; $i -lt $Limit; $i++) { \"i=$i\" }"),
            ("Step", "for ($i=0; $i -lt $Limit; $i+=$Step) { \"i=$i\" }"),
            ("Descending", "for ($i=$Limit; $i -gt 0; $i--) { \"i=$i\" }"),
            ("While", "$i=0; while (($i -lt $Limit)) { \"i=$i\"; ++$i }"),
            ("DoWhile", "$i=0; do { \"i=$i\"; $i++ } while ($i -lt $Limit)"),
            ("DoUntil", "$i=0; do { \"i=$i\"; $i++ } until ($i -ge $Limit)"),
            ("If", "$i=0; if ($i -lt $Limit) { $i=1 }"),
            ("ElseIf", "$i=0; if ($Limit -eq -1) { $i=-1 } elseif ($i -lt $Limit) { $i=1 }"),
            ("ConditionMutation", "$i=0; while (($i+=$Step) -lt $Limit) { \"i=$i\" }"),
            ("DoContinue", "$i=0; do { $i++; continue } while ($i -lt $Limit)"),
            ("DoOutput", "$i=0; do { $i++; 'body' } while ($i -lt $Limit)"),
            ("DoArray", "$i=0; do { $i++; $result=@(1; 2) } while ($i -lt $Limit)"),
            ("EmptyDo", "do {} while (1 / $Limit)") };
        var source = string.Join(Environment.NewLine, loops.Select(loop =>
            "function Read-NativeLoop" + loop.Item1 +
            " { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Limit=3,[object]$Step=1); " +
            loop.Item2 + "; return \"end=$i;status=$?\" }"));
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeLoops", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(loops.Length == result.Manifest!.CompiledMethods, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => unit.Name + ": " + cause.Message))));
        const string probe = """
            $cases=@(@{limit=0;step=1},@{limit=1;step=1},@{limit=3;step=1},@{limit=3;step=2},
                @{limit='3';step=1},@{limit=$null;step=1},@{limit=3;step='bad'},@{limit='bad';step=1})
            foreach ($command in (Get-Command -Module MODULE_NAME -Name 'Read-NativeLoop*' | Sort-Object Name).Name) {
                foreach ($action in 'Continue','SilentlyContinue','Ignore','Stop') {
                    for ($index=0;$index -lt $cases.Count;$index++) {
                        if ($command.EndsWith('EmptyDo') -and $index -ne 7) { continue }
                        $case=$cases[$index]; $Error.Clear(); $faults=@(); $records=@(); $caught=$null
                        if ($action -eq 'Stop') {
                            try { $records=@(& $command -Limit $case.limit -Step $case.step -ErrorAction $action -ErrorVariable faults 2>$null) }
                            catch { $caught=$_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName+':'+$_.InvocationInfo.ScriptLineNumber+':'+$_.InvocationInfo.OffsetInLine }
                        } else {
                            $records=@(& $command -Limit $case.limit -Step $case.step -ErrorAction $action -ErrorVariable faults 2>$null)
                        }
                        [pscustomobject]@{command=$command;case=$index;action=$action;records=$records;caught=$caught;
                            faults=@($faults | ForEach-Object { $_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName+':'+$_.InvocationInfo.ScriptLineNumber+':'+$_.InvocationInfo.OffsetInLine });
                            errors=@($Error | ForEach-Object { $_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName+':'+$_.InvocationInfo.ScriptLineNumber+':'+$_.InvocationInfo.OffsetInLine })} | ConvertTo-Json -Compress -Depth 6
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "$m=Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "' -PassThru; " + probe.Replace("MODULE_NAME", "$m.Name", StringComparison.Ordinal), fixture.RootPath, "native-loop-probe");
        var compiled = RunStatementErrorProbe(host, "$m=Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "' -PassThru; " + probe.Replace("MODULE_NAME", "$m.Name", StringComparison.Ordinal), fixture.RootPath, "native-loop-probe");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal((loops.Length - 1) * 8 * 4 + 4, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(8).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
        Assert.True(original.StandardError == compiled.StandardError,
            "Original errors: " + original.StandardError + Environment.NewLine + "Generated errors: " + compiled.StandardError);
    }
}

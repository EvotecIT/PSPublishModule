using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeMutations_PreserveVariableConstraintsTypesAndErrorContinuation(string framework, string host)
    {
        var mutations = new[] {
            ("Assign", "$value=$Right"), ("Add", "$value+=$Right"), ("Subtract", "$value-=$Right"),
            ("Multiply", "$value*=$Right"), ("Divide", "$value/=$Right"), ("Modulo", "$value%=$Right"),
            ("Increment", "++$value"), ("Decrement", "--$value"),
            ("PostIncrement", "$value++"), ("PostDecrement", "$value--") };
        var functions = string.Join(Environment.NewLine, mutations.SelectMany(mutation => new[] { false, true }.Select(constrained =>
            "function Read-NativeMutation" + mutation.Item1 + (constrained ? "Constrained" : "Free") +
            " { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1," +
            (constrained ? "[ValidateRange(1,9)][int]" : "[object]") + "$value,[object]$Right,[object]$Reporter); " +
            mutation.Item2 + "; return \"status=$?;$Reporter\" }")));
        functions += Environment.NewLine + string.Join(Environment.NewLine, mutations.SelectMany(mutation =>
            new[] { false, true }.Select(constrained =>
                "function Read-NativeMutation" + mutation.Item1 + (constrained ? "Result" : "TypedResult") +
                " { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1," +
                (constrained ? "[ValidateRange(1,9)]" : "") + "[int]$value,[object]$Right,[object]$Reporter); $copy='kept'; $copy=(" +
                "\n" + mutation.Item2.Replace("$Right", "\n$Right", StringComparison.Ordinal) + "\n); return \"status=$?;$Reporter\" }")));
        const string support = """
            function Get-NativeMutationValueDescription($value) {
                if ($null -eq $value) { return 'null' }
                if ($value -is [array]) {
                    return $value.GetType().FullName+':'+(($value | ForEach-Object { Get-NativeMutationValueDescription $_ }) -join '|')
                }
                return $value.GetType().FullName+':'+[string]$value
            }
            function New-NativeMutationReporter {
                $reporter=[pscustomobject]@{}
                Add-Member -InputObject $reporter -MemberType ScriptMethod -Name ToString -Force -Value {
                    $description=Get-NativeMutationValueDescription (Get-Variable -Scope 1 -Name value).Value
                    $copy=Get-Variable -Scope 1 -Name copy -ErrorAction Ignore
                    if ($copy) { $description+=';copy='+(Get-NativeMutationValueDescription $copy.Value) }
                    $description
                }
                $reporter
            }
            """;
        using var fixture = ArtifactFixture.Create(functions + Environment.NewLine + support, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeMutations", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(mutations.Length * 4, result.Manifest!.CompiledMethods);
        Assert.Equal(mutations.Length * 4, result.Manifest.UnitDispositionLedger!.Entries.Count(unit => unit.UsesNativeFunctionBinding));
        const string probe = """
            $cases=@(
                @{left=1;right=2},@{left=9;right=1},@{left=[int]::MaxValue;right=1},@{left=[long]::MaxValue;right=1},
                @{left=[uint64]::MaxValue;right=1},@{left=[single]0.1;right=[single]0.2},@{left=[decimal]3;right=2},
                @{left=4;right=2},@{left=5;right=2},@{left=1;right=0},@{left=1;right='bad'},
                @{left='A';right='a'},@{left=$null;right=3},@{left=[psobject]12;right=2},
                @{left=@(1,2);right=2},@{left=@('A','a');right='a'},@{left='prefix';right=@('a','b')})
            $reporter=New-NativeMutationReporter
            foreach ($command in (Get-Command -Module MODULE_NAME -Name 'Read-NativeMutation*' | Sort-Object Name).Name) {
                foreach ($action in 'Continue','SilentlyContinue','Ignore','Stop') {
                    for ($index=0;$index -lt $cases.Count;$index++) {
                        $case=$cases[$index]; $Error.Clear(); $faults=@(); $records=@(); $caught=$null
                        if ($action -eq 'Stop') {
                            try { $records=@(& $command -value $case.left -Right $case.right -Reporter $reporter -ErrorAction $action -ErrorVariable faults 2>$null) }
                            catch { $caught=$_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName+':'+$_.InvocationInfo.ScriptLineNumber+':'+$_.InvocationInfo.OffsetInLine }
                        } else {
                            $records=@(& $command -value $case.left -Right $case.right -Reporter $reporter -ErrorAction $action -ErrorVariable faults 2>$null)
                        }
                        [pscustomobject]@{command=$command;case=$index;action=$action;records=$records;caught=$caught;
                            faults=@($faults | ForEach-Object { $_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName+':'+$_.InvocationInfo.ScriptLineNumber+':'+$_.InvocationInfo.OffsetInLine });
                            errors=@($Error | ForEach-Object { $_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName+':'+$_.InvocationInfo.ScriptLineNumber+':'+$_.InvocationInfo.OffsetInLine })} | ConvertTo-Json -Compress -Depth 6
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "$m=Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "' -PassThru; " + probe.Replace("MODULE_NAME", "$m.Name", StringComparison.Ordinal), fixture.RootPath, "native-mutation-probe");
        var compiled = RunStatementErrorProbe(host, "$m=Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "' -PassThru; " + probe.Replace("MODULE_NAME", "$m.Name", StringComparison.Ordinal), fixture.RootPath, "native-mutation-probe");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(mutations.Length * 4 * 17 * 4, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(8).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
        Assert.True(original.StandardError == compiled.StandardError,
            "Original errors: " + original.StandardError + Environment.NewLine + "Generated errors: " + compiled.StandardError);
    }
}

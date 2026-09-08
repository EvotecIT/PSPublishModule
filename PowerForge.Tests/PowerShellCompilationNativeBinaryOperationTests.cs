using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeBinaryOperations_PreserveRuntimeValuesTypesAndErrorBoundaries(string framework, string host)
    {
        var operations = new[] {
            ("Add", "+"), ("Subtract", "-"), ("Multiply", "*"), ("Divide", "/"), ("Modulo", "%"),
            ("Equal", "-eq"), ("CaseEqual", "-ceq"), ("NotEqual", "-ne"), ("CaseNotEqual", "-cne"),
            ("Less", "-lt"), ("CaseLess", "-clt"), ("LessEqual", "-le"), ("CaseLessEqual", "-cle"),
            ("Greater", "-gt"), ("CaseGreater", "-cgt"), ("GreaterEqual", "-ge"), ("CaseGreaterEqual", "-cge"),
            ("And", "-band"), ("Or", "-bor"), ("Xor", "-bxor"), ("ShiftLeft", "-shl"), ("ShiftRight", "-shr") };
        var functions = string.Join(Environment.NewLine, operations.Select(operation =>
            "function Read-Native" + operation.Item1 + " { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1," +
            "[object]$Left,[object]$Right,[object]$Reporter); $value='kept'; $value=$Left " + operation.Item2 +
            " $Right; return \"status=$?;$Reporter\" }"));
        const string support = """
            function Get-NativeValueDescription($value) {
                if ($null -eq $value) { return 'null' }
                if ($value -is [array]) {
                    return $value.GetType().FullName+':'+(($value | ForEach-Object { Get-NativeValueDescription $_ }) -join '|')
                }
                return $value.GetType().FullName+':'+[string]$value
            }
            function New-NativeValueReporter {
                $reporter=[pscustomobject]@{}
                Add-Member -InputObject $reporter -MemberType ScriptMethod -Name ToString -Force -Value {
                    Get-NativeValueDescription (Get-Variable -Scope 1 -Name value).Value
                }
                $reporter
            }
            """;
        using var fixture = ArtifactFixture.Create(functions + Environment.NewLine + support, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeBinaryOperations", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(operations.Length, result.Manifest!.CompiledMethods);
        Assert.Equal(operations.Length, result.Manifest.UnitDispositionLedger!.Entries.Count(unit => unit.UsesNativeFunctionBinding));
        const string probe = """
            $cases=@(
                @{left=1;right=2},@{left=[int]::MaxValue;right=1},@{left=[long]::MaxValue;right=1},
                @{left=[uint64]::MaxValue;right=1},@{left=[single]0.1;right=[single]0.2},@{left=[decimal]3;right=2},
                @{left=4;right=2},@{left=5;right=2},@{left=1;right=0},@{left=1;right='bad'},
                @{left='A';right='a'},@{left=$null;right=3},@{left=[psobject]12;right=2},
                @{left=@(1,2);right=2},@{left=@('A','a');right='a'},@{left='prefix';right=@('a','b')})
            $reporter=New-NativeValueReporter
            foreach ($command in (Get-Command -Module MODULE_NAME -Name 'Read-Native*' | Sort-Object Name).Name) {
                foreach ($action in 'Continue','SilentlyContinue','Ignore') {
                    for ($index=0;$index -lt $cases.Count;$index++) {
                        $case=$cases[$index]; $Error.Clear(); $faults=@()
                        $records=@(& $command -Left $case.left -Right $case.right -Reporter $reporter -ErrorAction $action -ErrorVariable faults 2>$null)
                        [pscustomobject]@{command=$command;case=$index;action=$action;records=$records;
                            faults=@($faults | ForEach-Object { $_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName });
                            errors=@($Error | ForEach-Object { $_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName })} | ConvertTo-Json -Compress -Depth 6
                    }
                }
                foreach ($action in 'Continue','Stop') {
                    $Error.Clear(); $faults=@(); $records=@(); $caught=$null
                    try { $records=@(& $command -Left 1 -Right 'bad' -Reporter $reporter -ErrorAction $action -ErrorVariable faults 2>$null) }
                    catch { $caught=$_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName }
                    [pscustomobject]@{command=$command;action=$action;caught=$caught;records=$records;
                        faults=@($faults | ForEach-Object { $_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName });
                        errors=@($Error | ForEach-Object { $_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName })} | ConvertTo-Json -Compress -Depth 6
                }
            }
            """;
        // Reuse the caller path so host-formatted errors compare without path normalization.
        // Obtain exported names from each actual module; generated module and source fixture names differ.
        var original = RunStatementErrorProbe(host, "$m=Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "' -PassThru; " + probe.Replace("MODULE_NAME", "$m.Name", StringComparison.Ordinal), fixture.RootPath, "native-binary-probe");
        var compiled = RunStatementErrorProbe(host, "$m=Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "' -PassThru; " + probe.Replace("MODULE_NAME", "$m.Name", StringComparison.Ordinal), fixture.RootPath, "native-binary-probe");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(operations.Length * 50, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(8).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
        Assert.True(original.StandardError == compiled.StandardError,
            "Original errors: " + original.StandardError + Environment.NewLine + "Generated errors: " + compiled.StandardError);
    }
}

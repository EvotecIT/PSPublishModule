using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeCaptureAssignments_PreserveConstraintsStorageAndEvaluationOrder(string framework, string host)
    {
        const string values = "foreach ($item in $Items) { $trace += 'body;'; Write-Output $item }";
        var bodies = new[]
        {
            "[int]$x=" + values,
            "[int[]]$x=" + values,
            "[ValidateRange(1,3)][int]$x=" + values,
            "[ValidateRange(1,3)][int]$x=2; $x=" + values,
            "[ValidateRange(1,3)][int]$x=2; [string]$x=" + values,
            "[int]$x=2; $x += " + values,
            "$x=2; $x += foreach ($item in $Items) { $trace += 'body;'; $x=20; $item }",
            "[int]$x=2; [double]$x += foreach ($item in $Items) { $trace += 'body;'; $x=20; $item }",
            "$script:Captured='prior'; $script:Captured=" + values + "; \"scoped=$script:Captured\"",
            "[int]$script:Captured=" + values + "; \"scoped=$script:Captured\"",
            "$true=" + values,
            "$null /= " + values,
            "$x=foreach ($item in $Items) { [int]$inner=foreach ($part in $item) { $trace += 'inner;'; $part }; $inner }",
            "[int[]]$x=foreach ($x in $Items) { $trace += 'body;'; $x }",
            "[int]$x=for ($i=0; $i -lt $Items.Count; $i++) { $trace += 'body;'; Write-Output $Items[$i] }",
            "$i=0; [ValidateRange(1,3)][int]$x=while ($i -lt $Items.Count) { $trace += 'body;'; $Items[$i]; $i++ }",
            "$i=0; [int[]]$x=do { $trace += 'body;'; $Items[$i]; $i++ } while ($i -lt $Items.Count)",
            "$i=0; [string]$x=do { $trace += 'body;'; $Items[$i]; $i++ } until ($i -ge $Items.Count)",
            "[ValidateScript({ $_ -gt 0 })][int]$x=" + values,
            "$x=foreach ($item in $Items) { Write-Warning \"warning=$item\"; Write-Information \"information=$item\"; Write-Output $item }",
            "$x=foreach ($item in $Items) { Read-CaptureNestedHelper -Value $item; Write-Output 'tail' }"
        };
        const string describe = """
            "stored=$x;trace=$trace"
            Get-Variable x | ForEach-Object {
                if ($null -eq $_.Value) { 'type=null' } else { $_.Value.GetType().FullName }
                $_.Attributes | ForEach-Object { $_.GetType().FullName }
            }
            Write-Output 'outside'
            """;
        var source = string.Join(Environment.NewLine, bodies.Select((body, index) =>
            "function Read-CaptureAssignment" + index +
            " { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=2,[object]$Items); $x='prior'; $trace='';\n" +
            body + ";\n" + describe + "\n}"));
        source += """

            function Read-CaptureNestedHelper {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=2,[object]$Value)
                $result=foreach ($item in $Value) { Write-Output $item }
                $result
            }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeCaptureAssignments", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == bodies.Length + 1, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => unit.Name + ": " + cause.Message))));
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries, unit =>
        {
            Assert.True(unit.EmittedClrMethod);
            Assert.False(unit.RetainedHostedSource);
        });
        const string probe = """
            $cases=@(@{value=@()},@{value=@(2)},@{value=@(2,3)},@{value=@('bad')},@{value=@($null)},@{value=@(0)})
            foreach ($name in (Get-Command -Module MODULE_NAME -Name 'Read-CaptureAssignment*' | Sort-Object Name).Name) {
                for ($index=0;$index -lt $cases.Count;$index++) {
                    foreach ($preference in 'Continue','SilentlyContinue','Stop','Ignore') {
                        $Error.Clear(); $records=@(); $emitted=@(); $faults=@(); $warnings=@(); $information=@(); $caught=$null
                        if ($preference -eq 'Stop') {
                            try { $records=@(& $name -Items $cases[$index].value -ErrorAction $preference -OutVariable emitted -ErrorVariable faults -WarningAction SilentlyContinue -WarningVariable warnings -InformationAction SilentlyContinue -InformationVariable information 2>$null) }
                            catch { $caught=$_.FullyQualifiedErrorId }
                        } else {
                            $records=@(& $name -Items $cases[$index].value -ErrorAction $preference -OutVariable emitted -ErrorVariable faults -WarningAction SilentlyContinue -WarningVariable warnings -InformationAction SilentlyContinue -InformationVariable information 2>$null)
                        }
                        [pscustomobject]@{name=$name;case=$index;preference=$preference;records=$records;emitted=@($emitted);caught=$caught;
                            faults=@($faults | ForEach-Object { $_.FullyQualifiedErrorId });
                            warnings=@($warnings | ForEach-Object { $_.Message });information=@($information | ForEach-Object { $_.MessageData });
                            errors=@($Error | ForEach-Object { [ordered]@{id=$_.FullyQualifiedErrorId;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine;source=$_.InvocationInfo.Line;position=$_.InvocationInfo.PositionMessage} })} | ConvertTo-Json -Depth 12 -Compress
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "$m=Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "' -PassThru; " + probe.Replace("MODULE_NAME", "$m.Name", StringComparison.Ordinal), fixture.RootPath, "capture-assignment-probe");
        var compiled = RunStatementErrorProbe(host, "$m=Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "' -PassThru; " + probe.Replace("MODULE_NAME", "$m.Name", StringComparison.Ordinal), fixture.RootPath, "capture-assignment-probe");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(bodies.Length * 6 * 4, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(8).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}

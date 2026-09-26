using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeCaptureReturn_TransfersThroughForeachAndFinally(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-Direct {
                [CmdletBinding()] param([object]$Value, [switch]$WithFinally)
                $result = foreach ($i in @(1,2,3)) {
                    try {
                        if ($i -eq 2) { return $Value }
                        "item:$i"
                    } catch { 'caught' } finally {
                        if ($WithFinally) { "finally:$i" }
                    }
                }
                "after:$($result.Count)"
            }
            function Read-Array {
                [CmdletBinding()] param([object]$Value, [switch]$WithFinally)
                $result = @(foreach ($i in @(1,2,3)) {
                    try {
                        if ($i -eq 2) { return $Value }
                        "item:$i"
                    } catch { 'caught' } finally {
                        if ($WithFinally) { "finally:$i" }
                    }
                })
                "after:$($result.Count)"
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.CaptureReturn", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.CompiledMethods);
        const string probe = """
            foreach ($name in 'Read-Direct','Read-Array') {
                foreach ($kind in 'null','string','vector') {
                    $value = switch ($kind) { 'null' { $null } 'string' { 'returned' } 'vector' { ,@('a','b') } }
                    foreach ($withFinally in $false,$true) {
                        $records = @(& $name -Value $value -WithFinally:$withFinally)
                        [pscustomobject]@{ Name=$name; Kind=$kind; WithFinally=$withFinally;
                            Records=@($records | ForEach-Object { [pscustomobject]@{
                                Type=if ($null -eq $_) { $null } else { $_.GetType().FullName }; Value=[string]$_
                            } }) } | ConvertTo-Json -Compress -Depth 7
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-capture-return");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-capture-return");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(12, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(expected, actual);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeOutput_PreservesValuesCollectionsAndCapturedContainers(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-NativeOutput { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value); 'before'; $Value; 'after' }
            function Read-NativeReturn { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value); return $Value }
            function Read-NativeCaptureOutput { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value,[object]$Count=2); $result=for ($i=0;$i -lt $Count;$i++) { $Value }; return ,$result }
            function Read-NativeArrayOutput { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value); @($Value; 1) }
            function Read-NativeSingleArrayOutput { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value); return ,@($Value) }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeOutput", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 5, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => unit.Name + ": " + cause.Message))));
        const string probe = """
            $values=@(@{value=$null},@{value=1},@{value='text'},@{value=@(1,2)},@{value=@()},
                @{value=@((1,2),(3,4))},@{value=@{Name='one'}},@{value=[ordered]@{Name='two'}},
                @{value=[pscustomobject]@{Name='three'}},@{value=[object[]]@($null,1,$null)})
            foreach ($name in 'Read-NativeOutput','Read-NativeReturn','Read-NativeCaptureOutput','Read-NativeArrayOutput','Read-NativeSingleArrayOutput') {
                for ($index=0;$index -lt $values.Count;$index++) {
                    $Error.Clear(); $emitted=@()
                    $records=@(& $name -Value $values[$index].value -OutVariable emitted)
                    [pscustomobject]@{name=$name;case=$index;count=$records.Count;records=$records;emitted=@($emitted);
                        types=@($records | ForEach-Object { if ($null -eq $_) { 'null' } else { $_.GetType().FullName } });
                        errors=@($Error | ForEach-Object FullyQualifiedErrorId)} | ConvertTo-Json -Compress -Depth 12
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-native-output");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-native-output");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(50, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(8).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
        Assert.True(original.StandardError == compiled.StandardError,
            "Original errors: " + original.StandardError + Environment.NewLine + "Generated errors: " + compiled.StandardError);
    }
}

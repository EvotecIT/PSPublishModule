using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
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
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeOutput", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 4, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => unit.Name + ": " + cause.Message))));
        const string probe = """
            $values=@(@{value=$null},@{value=1},@{value='text'},@{value=@(1,2)},@{value=@()},
                @{value=@((1,2),(3,4))},@{value=@{Name='one'}},@{value=[ordered]@{Name='two'}},
                @{value=[pscustomobject]@{Name='three'}},@{value=[object[]]@($null,1,$null)})
            foreach ($name in 'Read-NativeOutput','Read-NativeReturn','Read-NativeCaptureOutput','Read-NativeArrayOutput') {
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
        Assert.Equal(40, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(8).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
        Assert.True(original.StandardError == compiled.StandardError,
            "Original errors: " + original.StandardError + Environment.NewLine + "Generated errors: " + compiled.StandardError);
    }
}

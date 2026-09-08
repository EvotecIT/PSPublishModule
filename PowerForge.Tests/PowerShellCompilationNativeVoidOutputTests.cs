using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeOutput_ExecutesVoidCollectionItemsWithoutRecords(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-NativeVoidCall { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value); @([GC]::KeepAlive($Value); 'tail') }
            function Read-NativeVoidMutation { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value); $i=1; @($i++; "$i"; ++$i; "$i"; $i--; "$i"; --$i; "$i") }
            function Read-NativeVoidFailure { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value); @('before'; [Threading.Monitor]::Exit($Value); 'tail') }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeVoidOutput", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 3, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => unit.Name + ": " + cause.Message))));
        const string probe = """
            foreach ($name in 'Read-NativeVoidCall','Read-NativeVoidMutation','Read-NativeVoidFailure') {
                foreach ($action in 'Continue','SilentlyContinue','Ignore','Stop') {
                    $Error.Clear(); $faults=@(); $emitted=@(); $records=@(); $caught=$null
                    try { $records=@(& $name -Value ([object]::new()) -ErrorAction $action -ErrorVariable faults -OutVariable emitted 2>$null) }
                    catch { $caught=$_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName }
                    [pscustomobject]@{name=$name;action=$action;records=$records;emitted=@($emitted);caught=$caught;
                        faults=@($faults | ForEach-Object { $_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName+':'+$_.InvocationInfo.ScriptLineNumber+':'+$_.InvocationInfo.OffsetInLine });
                        errors=@($Error | ForEach-Object { $_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName+':'+$_.InvocationInfo.ScriptLineNumber+':'+$_.InvocationInfo.OffsetInLine })} | ConvertTo-Json -Compress -Depth 8
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-native-void");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-native-void");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Equal(12, original.StandardOutput.Split('\n').Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.True(original.StandardOutput == compiled.StandardOutput,
            "Original: " + original.StandardOutput + Environment.NewLine + "Generated: " + compiled.StandardOutput);
        Assert.True(original.StandardError == compiled.StandardError,
            "Original errors: " + original.StandardError + Environment.NewLine + "Generated errors: " + compiled.StandardError);
    }
}

using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeCommandRegions_PreservePipelineWrappersAndAuthoredErrorSource(string framework, string host)
    {
        const string source = """
            function Read-NativeRegionSource {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[int]$Shape)
                $before=1; if($Shape -eq 0) { Get-Command PowerForgeMissingRegionCommand; 'after' }
                if($Shape -eq 1) { $copy=(Get-Command PowerForgeMissingRegionCommand); 'after' }
                if($Shape -eq 2) { (Get-Command PowerForgeMissingRegionCommand) 2>$null; 'after' }
                if($Shape -eq 3) { $copy=(Get-Command PowerForgeMissingRegionCommand) 2>$null; 'after' }
                if($Shape -eq 4) { return (Get-Command PowerForgeMissingRegionCommand) 2>$null }
                if($Shape -eq 5) { $copy=@('before'; (Get-Command PowerForgeMissingRegionCommand) 2>$null; 'after'); return $copy }
            }
            """;
        const string probe = """
            foreach($shape in 0,1,2,3,4,5) {
                $Error.Clear()
                $records=@(Read-NativeRegionSource -Shape $shape 2>&1 | ForEach-Object {
                    if($_ -is [System.Management.Automation.ErrorRecord]) { 'error:'+$_.FullyQualifiedErrorId } else { $_ }
                })
                [pscustomobject]@{shape=$shape;records=$records;errors=@($Error | ForEach-Object {
                    [pscustomobject]@{id=$_.FullyQualifiedErrorId;line=$_.InvocationInfo.Line;position=$_.InvocationInfo.PositionMessage;
                        number=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}
                })} | ConvertTo-Json -Compress -Depth 10
            }
            """;
        AssertNativeCommandRegionProbe(source, probe, framework, host, "source");
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeCommandRegions_PreserveBackgroundCaptures(string framework, string host)
    {
        if (framework == "net472") return; // Background pipeline syntax was introduced in PowerShell 6.
        const string source = """
            function Read-NativeBackground {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[int]$Shape)
                if($Shape -eq 0) { $job = Write-Output 'background' &; return $job }
                if($Shape -eq 1) { $job = Write-Output 'background' | ForEach-Object { $_ } &; return $job }
                if($Shape -eq 2) { return Write-Output 'background' & }
                if($Shape -eq 3) { $job = (Write-Output 'background' &); return $job }
                if($Shape -eq 4) { $job = @(Write-Output 'background' &); return $job }
                if($Shape -eq 5) { Write-Output 'background' & }
            }
            """;
        const string probe = """
            foreach($shape in 0,1,2,3,4,5) {
                $result=Read-NativeBackground -Shape $shape
                $isJob=$result -is [System.Management.Automation.Job]
                $records=if($isJob) {
                    try { $result | Wait-Job -Timeout 20 | Receive-Job -ErrorAction Stop }
                    finally { $result | Remove-Job -Force }
                } else { $result }
                [pscustomobject]@{shape=$shape;job=$isJob;records=@($records)} | ConvertTo-Json -Compress
            }
            """;
        AssertNativeCommandRegionProbe(source, probe, framework, host, "background");
    }

    private static void AssertNativeCommandRegionProbe(string source, string probe, string framework, string host, string name)
    {
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeRegionReview", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries, unit => Assert.False(unit.RetainedHostedSource));
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, name + "-original");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, name + "-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(original.StandardOutput == compiled.StandardOutput, "Original: " + original.StandardOutput + "\nGenerated: " + compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}

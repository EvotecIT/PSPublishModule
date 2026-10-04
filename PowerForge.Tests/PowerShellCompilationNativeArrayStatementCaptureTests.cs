using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeArrayStatementCapture_PreservesNestedLoopRecordsAndArrayIdentity(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-FilteredRecords {
                [CmdletBinding()] param([object[]]$Items, [bool]$IncludeHeader)
                [Array]$result = @(
                    if ($IncludeHeader) { 'header' }
                    foreach ($item in $Items) {
                        if ($null -ne $item) { "value=$item" }
                    }
                    foreach ($item in $Items) {
                        if ($null -eq $item) { $null }
                    }
                )
                return ,$result
            }
            function Test-EmptyArrayIdentity {
                [CmdletBinding()] param([object[]]$Items)
                $first = @(foreach ($item in $Items) { $item })
                $second = @(foreach ($item in $Items) { $item })
                return "$( [object]::ReferenceEquals($first, $second) )|$($first.GetType().FullName)|$($second.GetType().FullName)"
            }
            function Get-TransferredRecords {
                [CmdletBinding()] param([int[]]$Items)
                $result = @(foreach ($item in $Items) {
                    if ($item -eq 2) { continue }
                    if ($item -eq 4) { break }
                    $item
                })
                return ,$result
            }
            function Get-EscapingReturn {
                [CmdletBinding()] param([int[]]$Items)
                $result = @(foreach ($item in $Items) { if ($item -eq 2) { return 'stopped' }; $item })
                return ,$result
            }
            function Get-AfterArrayFailure {
                [CmdletBinding()] param([int[]]$Items)
                $result = @('prior')
                $caught = $false
                try {
                    $result = @(foreach ($item in $Items) {
                        if ($item -eq 2) { throw 'failed after output' }
                        $item
                    })
                } catch { $caught = $true }
                return "$caught|$($result.Count)|$($result[0])"
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ArrayStatementCapture", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 5, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit =>
                unit.DiagnosticChain.Select(cause => unit.Name + ": " + cause.Message))));
        const string probe = """
            foreach ($case in @(@(), @(1), @(1,2,3))) {
                [pscustomobject]@{returnRecords=@(Get-EscapingReturn -Items $case)}|ConvertTo-Json -Compress -Depth 5
            }
            foreach ($case in @(@(), @('a'), @('a',$null,'b'))) {
                foreach ($header in $false,$true) {
                    $values=Get-FilteredRecords -Items $case -IncludeHeader $header
                    [pscustomobject]@{count=$values.Count;type=$values.GetType().FullName;values=@($values);
                        identity=(Test-EmptyArrayIdentity -Items $case)} | ConvertTo-Json -Compress -Depth 5
                }
            }
            foreach ($case in @(@(), @(1), @(1,2,3,4,5))) {
                $values=Get-TransferredRecords -Items $case
                [pscustomobject]@{case=@($case);count=$values.Count;values=@($values)} | ConvertTo-Json -Compress -Depth 5
            }
            @(Get-EscapingReturn -Items @(1,2,3)) | ConvertTo-Json -Compress -Depth 5
            Get-AfterArrayFailure -Items @(1,2,3)
            Get-AfterArrayFailure -Items @(1,3)
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-array-statement-capture");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-array-statement-capture");
        Assert.Equal(0, original.ExitCode);
        Assert.Empty(original.StandardError);
        Assert.Contains("True|1|prior", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("False|2|1", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.ExitCode, compiled.ExitCode);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}

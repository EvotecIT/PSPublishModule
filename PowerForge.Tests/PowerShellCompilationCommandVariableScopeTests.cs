using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CommandVariableScope_PreservesPipelineBindingAndOutputCapture(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-CapturedValue {
                [CmdletBinding()] param([Parameter(ValueFromPipeline)][int]$Value)
                return $Value
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.VariableScope", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        const string probe = """
            foreach ($action in 'Continue','SilentlyContinue','Ignore','Stop') {
                foreach ($append in $false,$true) {
                    $faults=@('seed'); $captured=@('seed'); $Error.Clear()
                    $records=[Collections.Generic.List[string]]::new()
                    $ev=if ($append) { '+faults' } else { 'faults' }
                    $ov=if ($append) { '+captured' } else { 'captured' }
                    try {
                        1,'bad',2 | Get-CapturedValue -ErrorAction $action -ErrorVariable $ev -OutVariable $ov 2>&1 | ForEach-Object {
                            if ($_ -is [Management.Automation.ErrorRecord]) { [void]$records.Add('error:'+$_.FullyQualifiedErrorId) }
                            else { [void]$records.Add('value:'+$_) }
                        }
                    } catch { [void]$records.Add('caught:'+$_.FullyQualifiedErrorId) }
                    [pscustomobject]@{ action=$action; append=$append; records=$records.ToArray();
                        faults=@($faults | ForEach-Object { $_.GetType().FullName });
                        captured=@($captured); errors=@($Error | ForEach-Object { $_.FullyQualifiedErrorId }) } | ConvertTo-Json -Compress
                    $before=$faults.Count
                    $later=@(Get-CapturedValue 7)
                    [pscustomobject]@{ later=$later; before=$before; after=$faults.Count; captured=@($captured) } | ConvertTo-Json -Compress
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-variable-scope");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-variable-scope");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }
}

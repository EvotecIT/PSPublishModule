using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void ModuleLifecycle_PreservesStateThroughNamedBlocks(string framework, string host)
    {
        var source = """
            function Initialize-OwnerError { $null=$Error.Add('module-owned error') }
            function Invoke-OwnerLifecycle {
                [CmdletBinding(SupportsShouldProcess=$true)]
                param([Parameter(ValueFromPipeline=$true)][int]$Value,[Collections.Generic.List[string]]$Observed,[bool]$Fail)
                begin { $Observed.Add('begin:'+$Error.Count+':'+[string]$WhatIfPreference+':'+[string]$VerbosePreference) }
                process {
                    $Observed.Add('process:'+ $Value+':'+$Error.Count)
                    Microsoft.PowerShell.Utility\Write-Verbose 'module verbose'
                    if($PSCmdlet.ShouldProcess('probe-only','Observe')) { $Observed.Add('acted') }
                    if($Fail) { [void][int]::Parse('bad') }
                }
                end { $Observed.Add('end:'+$Error.Count) }
            """ + (framework == "net472" ? "\n}" : "\nclean { $Observed.Add('clean:'+$Error.Count) }\n}");
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ModuleLifecycleState", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.CompiledMethods);
        Assert.Equal(PowerShellCompilationLifecycleExecution.HostedSteppablePipeline, Assert.Single(result.Manifest.Lifecycles).Execution);
        Assert.True(result.Manifest.RuntimeFallbackUnits > 0);
        const string probe = """
            Initialize-OwnerError
            $global:WhatIfPreference=$false
            $global:VerbosePreference='SilentlyContinue'
            $global:ErrorActionPreference='Continue'
            & {
                $WhatIfPreference=$true
                $VerbosePreference='Continue'
                $ErrorActionPreference='Stop'
                foreach($fail in $false,$true) {
                foreach($explicit in $false,$true) {
                    $global:Error.Clear()
                    $observed=[Collections.Generic.List[string]]::new()
                    $parameters=@{}
                    if($explicit) { $parameters=@{WhatIf=$true;Verbose=$true;ErrorAction='Ignore'} }
                    $caught=$false
                    try { $values=@(1,2 | Invoke-OwnerLifecycle -Observed $observed -Fail $fail @parameters 2>&1 4>&1) }
                    catch { $caught=$true; $values=@($_) }
                    $records=@(foreach($value in $values) {
                        if($value -is [Management.Automation.ErrorRecord]) { 'error:'+$value.Exception.GetType().FullName }
                        elseif($value -is [Management.Automation.VerboseRecord]) { 'verbose:'+$value.Message }
                        else { [string]$value }
                    })
                    [pscustomobject]@{fail=$fail;explicit=$explicit;caught=$caught;observed=$observed.ToArray();records=$records;errors=$global:Error.Count;callerWhatIf=[bool]$WhatIfPreference;callerErrorAction=[string]$ErrorActionPreference} | ConvertTo-Json -Compress
                }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-module-lifecycle-state");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-module-lifecycle-state");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.True(original.StandardOutput == compiled.StandardOutput, "Original:\n" + original.StandardOutput + "Compiled:\n" + compiled.StandardOutput);
    }
}

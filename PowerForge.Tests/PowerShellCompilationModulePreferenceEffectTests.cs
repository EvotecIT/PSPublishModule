using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void ModulePreferences_PreserveShouldProcessStreamsAndContinuation(string framework, string host)
    {
        const string source = """
            function Test-OwnerAction { [CmdletBinding(SupportsShouldProcess=$true)] param() return $PSCmdlet.ShouldProcess('probe-only') }
            function Read-OwnerVerbose { [CmdletBinding()] param() Microsoft.PowerShell.Utility\Write-Verbose 'module verbose'; return 'after' }
            function Read-OwnerFailure { [CmdletBinding()] param() [void][int]::Parse('bad'); return 'after' }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ModulePreferenceEffects", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(3, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        const string probe = """
            $global:WhatIfPreference=$false
            $global:VerbosePreference='SilentlyContinue'
            $global:ErrorActionPreference='Continue'
            & {
                $WhatIfPreference=$true
                $VerbosePreference='Continue'
                $ErrorActionPreference='Stop'
                foreach($name in 'Test-OwnerAction','Read-OwnerVerbose','Read-OwnerFailure') {
                    $global:Error.Clear()
                    $caught=$false
                    try { $values=@(& $name 2>&1 4>&1) } catch { $caught=$true; $values=@($_) }
                    $records=@(foreach($value in $values) {
                        if($value -is [Management.Automation.ErrorRecord]) { 'error:'+$value.FullyQualifiedErrorId }
                        elseif($value -is [Management.Automation.VerboseRecord]) { 'verbose:'+$value.Message }
                        else { [string]$value }
                    })
                    [pscustomobject]@{name=$name;caught=$caught;records=$records;errors=$global:Error.Count;callerWhatIf=[bool]$WhatIfPreference;callerVerbose=[string]$VerbosePreference;callerErrorAction=[string]$ErrorActionPreference} | ConvertTo-Json -Compress
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-module-preference-effects");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-module-preference-effects");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }
}

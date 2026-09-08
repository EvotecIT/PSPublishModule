using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    public static IEnumerable<object[]> ModuleSessionStateHosts()
        => StatementErrorHosts().SelectMany(configuration => new[] { false, true }
            .Select(hybrid => new object[] { configuration[0], configuration[1], hybrid }));

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(ModuleSessionStateHosts))]
    public void ModuleSessionState_PreservesSnapshotsAcrossCallersImportsAndRunspaces(string framework, string host, bool hybrid)
    {
        const string functions = """
            function Read-OwnerState {
                [CmdletBinding(SupportsShouldProcess=$true)] param()
                return @($Error.Count,$VerbosePreference.ToString(),$ErrorActionPreference.ToString(),$ConfirmPreference.ToString(),$WhatIfPreference)
            }
            function Read-IndirectOwnerState { [CmdletBinding()] param() return Read-OwnerState }
            """;
        var source = hybrid
            ? "$null=$Error.Add('initial module error'); $WhatIfPreference=$true\n" + functions + "\nfunction Add-OwnerError { [CmdletBinding()] param() $null=$Error.Add('added module error'); return $Error.Count }"
            : functions;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ModuleSessionState", PowerShellCompilationArtifactKind.BinaryModule,
            hybrid ? PowerShellCompilationMode.Hybrid : PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.CompiledMethods);
        if (hybrid) Assert.True(result.Manifest.RuntimeFallbackUnits > 0);
        else Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);

        const string probe = """
            foreach($cycle in 0,1) {
                $global:VerbosePreference='SilentlyContinue'
                $global:ErrorActionPreference='Continue'
                $global:ConfirmPreference='High'
                $global:WhatIfPreference=$false
                $module=Import-Module $modulePath -Force -PassThru -Verbose:$false
                $global:Error.Clear()
                try { throw 'caller error' } catch {}
                & {
                    $VerbosePreference='Continue'
                    $ErrorActionPreference='Ignore'
                    $ConfirmPreference='None'
                    $WhatIfPreference=$true
                    foreach($name in 'Read-OwnerState','Read-IndirectOwnerState') {
                        $values=@(& $name)
                        [pscustomobject]@{phase='local';cycle=$cycle;name=$name;values=$values;callerErrors=$global:Error.Count} | ConvertTo-Json -Compress
                    }
                }
                if($hybrid) { $null=Add-OwnerError }
                [pscustomobject]@{phase='after-mutation';cycle=$cycle;values=@(Read-OwnerState);callerErrors=$global:Error.Count} | ConvertTo-Json -Compress
                $global:VerbosePreference='Continue'
                [pscustomobject]@{phase='global-change';cycle=$cycle;values=@(Read-OwnerState)} | ConvertTo-Json -Compress
                [pscustomobject]@{phase='explicit';cycle=$cycle;values=@(Read-OwnerState -Verbose:$false -ErrorAction Stop -Confirm:$false -WhatIf:$false)} | ConvertTo-Json -Compress
                [pscustomobject]@{phase='after-explicit';cycle=$cycle;values=@(Read-OwnerState)} | ConvertTo-Json -Compress
                Remove-Module $module -Verbose:$false
            }
            $first=[powershell]::Create()
            $second=[powershell]::Create()
            try {
                foreach($ps in $first,$second) {
                    [void]$ps.AddScript('param($path) Import-Module $path -Force; $global:Error.Clear(); try { throw "runspace caller error" } catch {}').AddArgument($modulePath)
                    [void]$ps.Invoke()
                    if($ps.HadErrors) { throw "Runspace import failed: $($ps.Streams.Error)" }
                    $ps.Commands.Clear()
                }
                if($hybrid) { [void]$first.AddCommand('Add-OwnerError'); [void]$first.Invoke(); $first.Commands.Clear() }
                $index=0
                foreach($ps in $first,$second) {
                    [void]$ps.AddCommand('Read-OwnerState')
                    $values=@($ps.Invoke() | ForEach-Object { $_.PSObject.BaseObject })
                    if($ps.HadErrors) { throw "Runspace snapshot failed: $($ps.Streams.Error)" }
                    [pscustomobject]@{phase='runspace';index=$index;values=$values} | ConvertTo-Json -Compress
                    $index++
                }
            } finally { $first.Dispose(); $second.Dispose() }
            """;
        var setup = "$hybrid=$" + (hybrid ? "true" : "false") + "; $modulePath='";
        var original = RunStatementErrorProbe(host, setup + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-module-session-state");
        var compiled = RunStatementErrorProbe(host, setup + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-module-session-state");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Contains("\"phase\":\"local\"", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }
}

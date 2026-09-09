namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeModuleState_PreservesInitializationFailureAndCleanup(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            param([bool]$Fail = $false)
            $script:State = 'initial'
            $ExecutionContext.SessionState.Module.OnRemove = { $global:StateRemovalCount++ }
            function Read-InitializedState { [CmdletBinding()] param([ValidateNotNull()][string]$Prefix='value') return $Prefix+':'+$script:State }
            function Set-InitializedState { [CmdletBinding()] param([ValidateNotNull()][string]$Value) $script:State = $Value }
            if($Fail) { throw 'initialization failed' }
            Export-ModuleMember -Function Read-InitializedState, Set-InitializedState
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeInitialization", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries.Where(unit => unit.Name.EndsWith("InitializedState", StringComparison.Ordinal)),
            unit => Assert.False(unit.RetainedHostedSource));
        const string probe = """
            $global:StateRemovalCount=0
            foreach($fail in $true,$false,$true,$false) {
                $loaded=$null; $caught=$null; $values=@()
                try {
                    $loaded=Import-Module $modulePath -ArgumentList $fail -PassThru -ErrorAction Stop
                    $values=@(Read-InitializedState; Set-InitializedState -Value 'changed'; Read-InitializedState)
                } catch { $caught=$_.Exception.Message }
                $present=$null -ne (Get-Command Read-InitializedState -ErrorAction SilentlyContinue)
                if($null -ne $loaded) { Remove-Module $loaded -ErrorAction Stop }
                [pscustomobject]@{fail=$fail;caught=$caught;values=$values;present=$present;removed=$global:StateRemovalCount;
                    remains=($null -ne (Get-Command Read-InitializedState -ErrorAction SilentlyContinue))} | ConvertTo-Json -Compress
            }
            """;
        var original = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-native-initialization");
        var compiled = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-native-initialization");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Equal(4, original.StandardOutput.Split('\n').Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeModuleState_PreservesRetainedReentryRunspacesAndReimport(string framework, string host)
    {
        var clean = framework == "net472" ? "" : "clean { $script:State.Add('clean:'+ $Depth) }";
        using var fixture = ArtifactFixture.Create("""
            $script:State = [Collections.Generic.List[string]]::new()
            $script:State.Add('initialized')
            $ExecutionContext.SessionState.Module.OnRemove = { $global:StateRemovalCount++ }
            function Invoke-StateFlow {
                [CmdletBinding()] param([Parameter(ValueFromPipeline)][string]$Value,[int]$Depth=0,[switch]$Fail)
                begin { $script:State.Add('begin:'+ $Depth) }
                process {
                    $script:State.Add('before:'+ $Value)
                    $records = Read-RetainedStage -Value $Value -Depth $Depth -Fail:$Fail
                    $script:State.Add('after:'+ $Value)
                    'result:'+ ($records -join ',')
                }
                end { $script:State.Add('end:'+ $Depth) }
            CLEAN_CLAUSE
            }
            function Read-RetainedStage {
                [CmdletBinding()] param([string]$Value,[int]$Depth=0,[switch]$Fail)
                dynamicparam { }
                process {
                    $script:State.Add('retained:'+ $Value)
                    if ($Depth -gt 0) { 'nested' | Invoke-StateFlow -Depth ($Depth-1) }
                    if ($Fail) { throw 'retained failure' }
                    $Value.ToUpperInvariant()
                }
            }
            function Get-StateTrace { [CmdletBinding()] param() return $script:State -join '|' }
            Export-ModuleMember -Function Invoke-StateFlow, Read-RetainedStage, Get-StateTrace
            """.Replace("CLEAN_CLAUSE", clean), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeModuleState", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var units = result.Manifest!.UnitDispositionLedger!.Entries;
        Assert.False(Assert.Single(units, unit => unit.Name == "Invoke-StateFlow").RetainedHostedSource);
        Assert.False(Assert.Single(units, unit => unit.Name == "Get-StateTrace").RetainedHostedSource);
        Assert.True(Assert.Single(units, unit => unit.Name == "Read-RetainedStage").RetainedHostedSource);
        const string probe = """
            $sessions=@([powershell]::Create(),[powershell]::Create())
            function Observe-State($index,$label,$source) {
                $ps=$sessions[$index]; $ps.Commands.Clear(); $ps.Streams.Error.Clear()
                [void]$ps.AddScript($source).AddArgument($modulePath)
                $records=@($ps.Invoke())
                if($ps.Streams.Error.Count -ne 0) { throw ($label+':'+($ps.Streams.Error -join '|')) }
                [pscustomobject]@{session=$index;label=$label;records=@($records | ForEach-Object {[string]$_})} | ConvertTo-Json -Compress
            }
            try {
                Observe-State 0 'import' 'param($path) $global:StateRemovalCount=0; $loadedModule=Import-Module $path -PassThru; Get-StateTrace'
                Observe-State 1 'import' 'param($path) $global:StateRemovalCount=0; $loadedModule=Import-Module $path -PassThru; Get-StateTrace'
                Observe-State 0 'reentry' "'a','b' | Invoke-StateFlow -Depth 1; Get-StateTrace"
                Observe-State 1 'independent' 'Get-StateTrace'
                Observe-State 0 'failure' "try { 'x' | Invoke-StateFlow -Fail -ErrorAction Stop } catch { 'caught:'+`$_.Exception.Message }; Get-StateTrace"
                Observe-State 1 'retained-mutation' "Read-RetainedStage -Value 'external'; Get-StateTrace"
                Observe-State 0 'early-stop' "'first','second' | Invoke-StateFlow | Select-Object -First 1; Get-StateTrace"
                Observe-State 0 'remove-reimport' 'param($path) Remove-Module $loadedModule; $global:StateRemovalCount; $loadedModule=Import-Module $path -PassThru; Get-StateTrace'
                Observe-State 1 'still-independent' 'Get-StateTrace'
                Observe-State 0 'reuse' "'later' | Invoke-StateFlow; Get-StateTrace"
                Observe-State 0 'remove' 'Remove-Module $loadedModule; $global:StateRemovalCount'
                Observe-State 1 'remove' 'Remove-Module $loadedModule; $global:StateRemovalCount'
            } finally { foreach($ps in $sessions) { $ps.Dispose() } }
            """;
        var original = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-native-module-state");
        var compiled = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-native-module-state");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Equal(12, original.StandardOutput.Split('\n').Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }
}

using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeFinallyPipelines_PreservePreferencesCaptureAndDownstreamStop(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Invoke-FinallyStream {
                [CmdletBinding()]param([ValidateRange(1,9)][int]$Seed=1,$Trace,[string]$Kind,$Callback)
                $Trace.Add('entered')
                if($Kind -eq 'verbose'){Write-Verbose 'cleanup'}
                elseif($Kind -eq 'warning'){Write-Warning 'cleanup'}
                elseif($Kind -eq 'information'){Write-Information 'cleanup'}
                elseif($Kind -eq 'error'){Write-Error 'cleanup'}
                elseif($Kind -eq 'callback'){& $Callback}
                $Trace.Add('cleaned')
            }
            function Get-DirectFinallyStream {
                [CmdletBinding()]param([ValidateRange(1,9)][int]$Seed=1,$Trace,[string]$Kind,[switch]$Fail,$Callback)
                try {'first';if($Fail){throw 'body failure'};'second'}
                finally {
                    $Trace.Add('entered')
                    if($Kind -eq 'verbose'){Write-Verbose 'cleanup'}
                    elseif($Kind -eq 'warning'){Write-Warning 'cleanup'}
                    elseif($Kind -eq 'information'){Write-Information 'cleanup'}
                    elseif($Kind -eq 'error'){Write-Error 'cleanup'}
                    elseif($Kind -eq 'callback'){& $Callback}
                    $Trace.Add('cleaned')
                }
                'after'
            }
            function Get-IndirectFinallyStream {
                [CmdletBinding()]param([ValidateRange(1,9)][int]$Seed=1,$Trace,[string]$Kind,[switch]$Fail,$Callback)
                try {'first';if($Fail){throw 'body failure'};'second'}
                finally {Invoke-FinallyStream -Trace $Trace -Kind $Kind -Callback $Callback}
                'after'
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeFinallyStreams", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.True(built.Manifest!.CompiledMethods == 3, string.Join(Environment.NewLine,
            built.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => cause.Message))));
        const string probe = """
            foreach($command in 'Get-DirectFinallyStream','Get-IndirectFinallyStream') {
                foreach($kind in 'verbose','warning','information','error','callback') {
                    foreach($action in 'Continue','SilentlyContinue','Stop') {
                        foreach($stop in $false,$true) {
                            foreach($fail in $false,$true) {
                                $VerbosePreference=$action
                                $WarningPreference=$action
                                $InformationPreference=$action
                                $ErrorActionPreference=$action
                                $trace=[Collections.Generic.List[string]]::new()
                                $records=[Collections.Generic.List[object]]::new()
                                $warnings=@();$information=@();$faults=@()
                                try {
                                    if($stop) {& $command -Trace $trace -Kind $kind -Fail:$fail -Callback {Write-Warning 'callback cleanup'} -WarningVariable warnings -InformationVariable information -ErrorVariable faults *>&1 | Select-Object -First 1 | ForEach-Object {$records.Add($_)}}
                                    else {& $command -Trace $trace -Kind $kind -Fail:$fail -Callback {Write-Warning 'callback cleanup'} -WarningVariable warnings -InformationVariable information -ErrorVariable faults *>&1 | ForEach-Object {$records.Add($_)}}
                                } catch {$records.Add($_)}
                                $ErrorActionPreference='Stop'
                                [pscustomobject]@{command=$command;kind=$kind;action=$action;stop=$stop;fail=$fail;trace=@($trace);records=@($records|ForEach-Object {
                                    [pscustomobject]@{type=$_.GetType().FullName;text=[string]$_;id=$(if($_ -is [Management.Automation.ErrorRecord]){$_.FullyQualifiedErrorId})}
                                });warnings=@($warnings|ForEach-Object {[string]$_});information=@($information|ForEach-Object {[string]$_});errors=@($faults|ForEach-Object {
                                    [pscustomobject]@{type=$_.GetType().FullName;message=$(if($_ -is [Exception]){$_.Message}else{[string]$_});id=$(if($_ -is [Management.Automation.ErrorRecord]){$_.FullyQualifiedErrorId})}
                                })}|ConvertTo-Json -Depth 7 -Compress
                            }
                        }
                    }
                }
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        var expected = original.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var actual = generated.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(120, expected.Length);
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < expected.Length; index++)
            Assert.True(expected[index] == actual[index], "Original: " + expected[index] + Environment.NewLine + "Generated: " + actual[index]);
    }
}

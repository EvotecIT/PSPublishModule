using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData("$target.Value = Test-FinallyValue")]
    [InlineData("$target[0] = Test-FinallyValue")]
    [InlineData("$script:value = Test-FinallyValue")]
    [InlineData("$value += Test-FinallyValue")]
    [InlineData("$target.Value += Test-FinallyValue")]
    [InlineData("$WarningPreference = Test-FinallyValue")]
    [InlineData("$local:WarningPreference = Test-FinallyValue")]
    public void FinallyCommandValues_KeepUnqualifiedStorageHosted(string assignment)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-UnqualifiedFinally { [CmdletBinding()]param([ValidateRange(1,9)][int]$Seed=1,$target) " +
            "try {'first';'second'} finally {" + assignment + "} }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "Generated.GuardedFinally", "GuardedFinally", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.DoesNotContain(typed.Methods, method => method.SourceName == "Get-UnqualifiedFinally");
        Assert.Contains(typed.Diagnostics, diagnostic => diagnostic.Message.Contains("Non-success streams from finally", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void FinallyCommandValues_PreserveCapturePreferencesAndDownstreamStop(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-FinallyCondition {
                [CmdletBinding()]param([ValidateRange(1,9)][int]$Seed=1,$Trace,[string]$Kind)
                try {'first'; 'second'}
                finally {
                    [void]$Trace.Add('entered')
                    if(Test-FinallyValue -Trace $Trace -Kind $Kind){[void]$Trace.Add('branch')}
                    [void]$Trace.Add('cleaned')
                }
                'after'
            }
            function Get-FinallyAssignment {
                [CmdletBinding()]param([ValidateRange(1,9)][int]$Seed=1,$Trace,[string]$Kind)
                try {'first'; 'second'}
                finally {
                    [void]$Trace.Add('entered')
                    $value=Test-FinallyValue -Trace $Trace -Kind $Kind
                    [void]$Trace.Add("value=$value")
                    [void]$Trace.Add('cleaned')
                }
                'after'
            }
            function Get-FinallyLocalAssignment {
                [CmdletBinding()]param([ValidateRange(1,9)][int]$Seed=1,$Trace,[string]$Kind)
                try {'first'; 'second'}
                finally {
                    [void]$Trace.Add('entered')
                    $local:value=Test-FinallyValue -Trace $Trace -Kind $Kind
                    [void]$Trace.Add("value=$local:value")
                    [void]$Trace.Add('cleaned')
                }
                'after'
            }
            function Get-FinallyTypedAssignment {
                [CmdletBinding()]param([ValidateRange(1,9)][int]$Seed=1,$Trace,[string]$Kind)
                [int]$value=9
                try {'first'; 'second'}
                finally {
                    [void]$Trace.Add('entered')
                    [int]$value=Test-FinallyValue -Trace $Trace -Kind $Kind
                    [void]$Trace.Add("value=$value")
                    [void]$Trace.Add('cleaned')
                }
                'after'
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.FinallyCommandValues", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Equal(4, built.Manifest!.CompiledMethods);
        const string probe = """
            $ErrorActionPreference='Stop'
            $module=(Get-Command Get-FinallyCondition).Module
            & $module {
                function script:Test-FinallyValue {
                    [CmdletBinding()]param($Trace,$Kind)
                    [void]$Trace.Add('provider')
                    if($Kind -eq 'warning'){Write-Warning 'cleanup warning'}
                    elseif($Kind -eq 'error'){Write-Error 'cleanup error' -ErrorId OfflineFinallyFailure}
                    elseif($Kind -eq 'throw'){throw 'cleanup throw'}
                    elseif($Kind -eq 'empty'){return}
                    elseif($Kind -eq 'null'){$null;return}
                    elseif($Kind -eq 'many'){$false;$true;return}
                    $true
                }
            }
            foreach($command in 'Get-FinallyCondition','Get-FinallyAssignment','Get-FinallyLocalAssignment','Get-FinallyTypedAssignment'){
                foreach($kind in 'success','warning','error','throw','empty','null','many'){
                    foreach($action in 'Continue','SilentlyContinue','Stop'){
                        foreach($stop in $false,$true){
                            $trace=[Collections.ArrayList]::new();$warnings=@();$faults=@();$Error.Clear()
                            $records=[Collections.Generic.List[object]]::new();$caught=$null
                            try {
                                if($stop){& $command -Trace $trace -Kind $kind -WarningAction $action -ErrorAction $action -WarningVariable warnings -ErrorVariable faults *>&1 | Select-Object -First 1 | ForEach-Object {$records.Add($_)}}
                                else{& $command -Trace $trace -Kind $kind -WarningAction $action -ErrorAction $action -WarningVariable warnings -ErrorVariable faults *>&1 | ForEach-Object {$records.Add($_)}}
                            }catch{$caught=[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}}
                            [pscustomobject]@{command=$command;kind=$kind;action=$action;stop=$stop;trace=@($trace);records=@($records|ForEach-Object{[pscustomobject]@{type=$_.GetType().FullName;text=[string]$_;id=$(if($_ -is [Management.Automation.ErrorRecord]){$_.FullyQualifiedErrorId})}});warnings=@($warnings|ForEach-Object{[string]$_});faults=@($faults|ForEach-Object{[pscustomobject]@{type=$_.GetType().FullName;message=$(if($_ -is [Exception]){$_.Message}else{[string]$_});id=$(if($_ -is [Management.Automation.ErrorRecord]){$_.FullyQualifiedErrorId})}});caught=$caught;errors=@($Error|ForEach-Object{$_.FullyQualifiedErrorId})}|ConvertTo-Json -Depth 6 -Compress
                        }
                    }
                }
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        var expected = original.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var actual = generated.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(168, expected.Length);
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < expected.Length; index++)
            Assert.True(expected[index] == actual[index], "Original: " + expected[index] + Environment.NewLine + "Generated: " + actual[index]);
    }
}

using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void BasicCommandHost_StrictKeepsUnqualifiedBasicSurfaceClosed()
    {
        using var fixture = ArtifactFixture.Create("function Get-BasicVerbose {Write-Verbose 'message'; 'done'}", ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.StrictBasicCommandHost", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));
        Assert.False(built.Succeeded);
        Assert.Null(built.ArtifactPath);
    }

    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void BasicCommandHost_PreservesOfflineTimeConfigurationAndLooseArguments(string framework, string host)
    {
        var source = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "Time", "Set-TimeSynchronization.ps1");
        Assert.Equal("228477621613D8C6EFD4BFCFE6E4DBA061E104C11290356E288AD387BF3C3BDC",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))));
        using var fixture = ArtifactFixture.Create(File.ReadAllText(source) + """

            function Get-BasicVerbose {param([string]$Value='done') Write-Verbose 'basic message'; $Value}
            function Invoke-ClockHelper {[CmdletBinding()]param() Get-Date -Format o}
            function Get-BasicMixed {[bool]$available=Microsoft.PowerShell.Core\Get-Command Get-Command -EA Ignore; $clock=Get-Date -Format o; "$available|$clock"}
            function Get-BasicTransitive {Invoke-ClockHelper; [bool](Microsoft.PowerShell.Core\Get-Command Get-Command -EA Ignore)}
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.BasicCommandHost", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        foreach (var name in new[] { "Set-TimeSynchronization", "Get-BasicVerbose", "Invoke-ClockHelper", "Get-BasicMixed", "Get-BasicTransitive" })
            Assert.True(Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, entry => entry.Name == name).EmittedClrMethod, name);
        const string probe = """
            $ErrorActionPreference='Stop'
            function global:Set-ItemProperty {throw 'Unexpected registry provider scope'}
            function global:Stop-Service {throw 'Unexpected service provider scope'}
            function global:Start-Service {throw 'Unexpected service provider scope'}
            $module=(Get-Command Set-TimeSynchronization).Module
            & $module {
                function script:Set-ItemProperty {
                    [CmdletBinding()]param($Path,$Name,$Value)
                    $script:Trace.Add([pscustomobject]@{operation='registry';path=$Path;name=$Name;value=$Value;verbose=[string]$VerbosePreference;errorAction=[string]$ErrorActionPreference})
                    if($script:Trace.Count -eq $script:FailAt){Write-Error -Message 'offline registry failure' -ErrorId 'OfflineTimeFailure'}
                }
                function script:Stop-Service {
                    [CmdletBinding()]param($Name)
                    $script:Trace.Add([pscustomobject]@{operation='stop';name=$Name})
                    if($script:Trace.Count -eq $script:FailAt){throw 'offline service failure'}
                }
                function script:Start-Service {
                    [CmdletBinding()]param($Name)
                    $script:Trace.Add([pscustomobject]@{operation='start';name=$Name})
                }
                function script:Get-Date {[CmdletBinding()]param($Format) 'offline-clock'}
            }
            foreach($case in 'default','custom','multiple','empty','registry-failure','service-failure'){
                foreach($loose in $false,$true){
                    & $module {param($Case) $script:Trace=[Collections.Generic.List[object]]::new(); $script:FailAt=switch($Case){'registry-failure'{2};'service-failure'{8};default{0}}} $case
                    $arguments=@{}
                    if($case -eq 'custom'){$arguments=@{TimeSource='offline.example';MaxPosPhaseCorrection=12;MaxnegPhaseCorrection=13;PollInterval=14}}
                    elseif($case -eq 'multiple'){$arguments=@{TimeSource=@('first.example','second.example')}}
                    elseif($case -eq 'empty'){$arguments=@{TimeSource=@()}}
                    if($loose){$arguments.Verbose=$true;$arguments.ErrorAction='SilentlyContinue';$arguments.ErrorVariable='ignored'}
                    $Error.Clear();$ignored=@();$failure=$null;$records=@()
                    try {$records=@(Set-TimeSynchronization @arguments)}catch{$failure=[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message}}
                    $trace=@(& $module {$script:Trace.ToArray()})
                    [pscustomobject]@{case=$case;loose=$loose;records=$records;trace=$trace;failure=$failure;ignoredCount=@($ignored).Count;errors=@($Error|ForEach-Object{$_.FullyQualifiedErrorId})}|ConvertTo-Json -Depth 7 -Compress
                }
            }
            $drivers=@({Get-BasicVerbose},{Get-BasicVerbose -Verbose},{Get-BasicVerbose -Verbose:$false},{Get-BasicVerbose -ErrorAction SilentlyContinue},{Get-BasicVerbose -Unknown value},{Get-BasicVerbose -OutVariable ignored})
            for($i=0;$i -lt $drivers.Count;$i++){
                $result=@(& $drivers[$i] 4>&1)
                [pscustomobject]@{kind='loose';case=$i;records=@($result|ForEach-Object{[pscustomobject]@{type=$_.GetType().FullName;value=$_.ToString()}})}|ConvertTo-Json -Depth 5 -Compress
            }
            foreach($name in 'Set-TimeSynchronization','Get-BasicVerbose'){
                $command=Get-Command $name
                [pscustomobject]@{kind='metadata';name=$name;commandType=[string]$command.CommandType;advanced=$command.CmdletBinding;parameters=@($command.Parameters.Keys|Sort-Object)}|ConvertTo-Json -Depth 4 -Compress
            }
            foreach($name in 'Get-BasicMixed','Get-BasicTransitive'){
                foreach($loose in $false,$true){
                    $arguments=@{};if($loose){$arguments.Verbose=$true;$arguments.ErrorAction='SilentlyContinue'}
                    [pscustomobject]@{kind='composition';name=$name;loose=$loose;records=@(& $name @arguments)}|ConvertTo-Json -Depth 4 -Compress
                }
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.Equal(original, generated);
        Assert.Equal(24, original.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("OfflineTimeFailure", original);
        Assert.Contains("offline service failure", original);
        Assert.DoesNotContain("VerboseRecord", original);
    }
}

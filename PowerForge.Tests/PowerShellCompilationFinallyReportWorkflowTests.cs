using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void FinallyCommandValues_PreserveOfflineReportStagingAndCleanup(string framework, string host)
    {
        var source = FindCompleteConversionWorkflow("CleanupMonster", "FullModule", "Private", "New-HTMLProcessedComputers.ps1");
        Assert.Equal("69CF988CA55E718F6D7D67FC9ABDEFD239770CD8E6496A525CBEC8BF1EEFFEF7",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))));
        using var fixture = ArtifactFixture.Create(File.ReadAllText(source), ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.FinallyReport", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.True(Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries,
            entry => entry.Name == "New-HTMLProcessedComputers").EmittedClrMethod);
        const string probe = """
            $ErrorActionPreference='Stop'
            function global:Remove-Item {throw 'Unexpected removal provider scope'}
            function global:Move-Item {throw 'Unexpected move provider scope'}
            function global:Invoke-Item {throw 'Unexpected launch provider scope'}
            function global:Test-Path {throw 'Unexpected path provider scope'}
            $module=(Get-Command New-HTMLProcessedComputers).Module
            & $module {
                function script:New-HTML {
                    [CmdletBinding()]param([scriptblock]$ScriptBlock,[string]$FilePath,[switch]$Online)
                    $script:Staging=$FilePath;$script:StageExists=$true
                    [void]$script:Trace.Add("html:online=$Online")
                    if($script:Failure -eq 'html'){throw 'offline HTML failure'}
                    'report-first';'report-second'
                }
                function script:Test-Path {
                    [CmdletBinding()]param([string]$LiteralPath)
                    if($LiteralPath -ne $script:Staging){throw 'Unexpected path lookup'}
                    [void]$script:Trace.Add('exists')
                    if($script:Failure -eq 'exists'){Write-Error 'offline lookup failure' -ErrorId OfflineLookupFailure}
                    $script:StageExists
                }
                function script:Remove-Item {
                    [CmdletBinding(SupportsShouldProcess)]param([string]$LiteralPath,[switch]$Force)
                    if($LiteralPath -ne $script:Staging){throw 'Unexpected removal target'}
                    [void]$script:Trace.Add('remove')
                    if($script:Failure -eq 'cleanup'){Write-Error 'offline cleanup failure' -ErrorId OfflineCleanupFailure}
                    else{$script:StageExists=$false}
                }
                function script:Move-Item {
                    [CmdletBinding(SupportsShouldProcess)]param([string]$LiteralPath,[string]$Destination,[switch]$Force)
                    if($LiteralPath -ne $script:Staging -or $Destination -ne $script:Output){throw 'Unexpected move target'}
                    [void]$script:Trace.Add('move')
                    if($script:Failure -eq 'move'){throw 'offline move failure'}
                    $script:StageExists=$false;$script:OutputExists=$true
                }
                function script:Merge-ADComputerHTMLReportData {
                    [CmdletBinding()]param($StagingHtmlPath,$DataFilePath,$OutputPath,$DataStoreID)
                    if($StagingHtmlPath -ne $script:Staging -or $OutputPath -ne $script:Output){throw 'Unexpected merge target'}
                    [void]$script:Trace.Add("merge:$DataStoreID")
                    if($script:Failure -eq 'merge'){throw 'offline merge failure'}
                    $script:OutputExists=$true
                }
                function script:Invoke-Item {
                    [CmdletBinding(SupportsShouldProcess)]param([string]$LiteralPath)
                    if($LiteralPath -ne $script:Output){throw 'Unexpected launch target'}
                    [void]$script:Trace.Add('show')
                }
            }
            $output=Join-Path ([IO.Path]::GetTempPath()) 'PFC/offline-finally-report.html'
            foreach($failure in 'none','html','move','merge','cleanup','exists'){
                foreach($count in 0,2){
                    foreach($stop in $false,$true){
                        & $module {param($Failure,$Output) $script:Trace=[Collections.ArrayList]::new();$script:Failure=$Failure;$script:Output=$Output;$script:StageExists=$false;$script:OutputExists=$false} $failure $output
                        $Error.Clear();$faults=@();$records=[Collections.Generic.List[object]]::new();$caught=$null
                        try {
                            $arguments=@{Export=@{};DisableOnlyIf=@{};DeleteOnlyIf=@{};MoveOnlyIf=@{};ComputerReportData=[pscustomobject]@{Count=$count;FilePath='offline-data.json';DataStoreID='inventory'};FilePath=$output;Online=$true;ShowHTML=$true;ReportOnly=$true;ErrorVariable='faults'}
                            if($stop){New-HTMLProcessedComputers @arguments | Select-Object -First 1 | ForEach-Object {$records.Add($_)}}
                            else{New-HTMLProcessedComputers @arguments | ForEach-Object {$records.Add($_)}}
                        }catch{$caught=[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}}
                        $state=& $module {[pscustomobject]@{trace=@($script:Trace);stageExists=$script:StageExists;outputExists=$script:OutputExists}}
                        [pscustomobject]@{failure=$failure;count=$count;stop=$stop;records=@($records);state=$state;caught=$caught;faults=@($faults|ForEach-Object{[pscustomobject]@{type=$_.GetType().FullName;message=$(if($_ -is [Exception]){$_.Message}else{[string]$_});id=$(if($_ -is [Management.Automation.ErrorRecord]){$_.FullyQualifiedErrorId})}});errors=@($Error|ForEach-Object{$_.FullyQualifiedErrorId})}|ConvertTo-Json -Depth 6 -Compress
                    }
                }
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        var expected = original.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var actual = generated.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(24, expected.Length);
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < expected.Length; index++)
            Assert.True(expected[index] == actual[index], "Original: " + expected[index] + Environment.NewLine + "Generated: " + actual[index]);
    }
}

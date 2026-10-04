using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_CloudInventoryLiteralCallbacksPreserveOfflineProjection(string framework, string host)
    {
        var names = new[] { "Get-InitialCloudDevices", "Get-CloudDevicePropertyValue", "Test-CloudDeviceRegistrationScope",
            "Test-CloudDeviceInventoryScope", "Get-CloudDeviceJoinTypeFromRegistrationState", "Set-CloudDeviceDuplicateNameMetadata" };
        var source = string.Join(Environment.NewLine, names.Select(name => File.ReadAllText(
            FindCompleteConversionWorkflow("CleanupMonster", "FullModule", "Private", name + ".ps1"))));
        using var fixture = ArtifactFixture.Create(source + Environment.NewLine + """
            function Set-OfflineCloudInventory {
                [CmdletBinding()]param($Entra,$Intune,[string]$Failure)
                $script:OfflineEntra=$Entra;$script:OfflineIntune=$Intune;$script:OfflineFailure=$Failure
                $script:OfflineCalls=[Collections.Generic.List[string]]::new()
            }
            function Read-OfflineCloudCalls { [CmdletBinding()]param();$script:OfflineCalls }
            function Get-MyDevice {
                [CmdletBinding()]param($Type,[switch]$IncludeAutopilotInventory)
                $script:OfflineCalls.Add('entra:'+($Type -join ',')+':'+$IncludeAutopilotInventory)
                if($script:OfflineFailure -eq 'entra') {Write-Warning 'offline Entra failure';return}
                $script:OfflineEntra
            }
            function Get-MyDeviceIntune {
                [CmdletBinding()]param($Type,$PropertySet,[switch]$IncludeAutopilotInventory)
                $script:OfflineCalls.Add('intune:'+($Type -join ',')+':'+$IncludeAutopilotInventory)
                if($script:OfflineFailure -eq 'intune') {Write-Warning 'offline Intune failure';return}
                $script:OfflineIntune
            }
            function Get-Date { [CmdletBinding()]param();[datetime]'2024-01-20T12:00:00' }
            function Write-Color { [CmdletBinding()]param($Text,$Color) }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.OfflineCloudInventory", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Contains(built.Manifest!.UnitDispositionLedger!.Entries,
            unit => unit.Name == "Get-InitialCloudDevices" && unit.EmittedClrMethod && unit.UsesNativeFunctionBinding && !unit.RetainedHostedSource);
        const string probe = """
            foreach($case in 'matched','unmatched','intune-only','excluded','duplicate','safety-entra','safety-intune','warning-entra','warning-intune') {
                $entra=[pscustomobject]@{Name='owned';TrustType='AzureAD registered';DeviceId='device-one';EntraDeviceObjectId='entra-one';OperatingSystem='Windows';OperatingSystemVersion='10';FirstSeen=[datetime]'2024-01-01T12:00:00';Enabled=$true;IsManaged=$true;IsCompliant=$false;MdmAppId='0000000a-0000-0000-c000-000000000000';AutopilotDeviceId='entra-autopilot';AutopilotInventoryLoaded=$true;AutopilotOnboarded=$true;AutopilotGroupTag='entra-group';AutopilotLastContactedDays=8}
                $intune=[pscustomobject]@{Name='owned';AzureAdDeviceId='device-one';ManagedDeviceId='intune-one';EntraDeviceObjectId='entra-one';OperatingSystem='Windows';OperatingSystemVersion='11';FirstSeen=[datetime]'2024-01-10T12:00:00';DeviceRegistrationState='registered';AutopilotDeviceId='';AutopilotInventoryLoaded=$false;AutopilotOnboarded=$false;AutopilotGroupTag='intune-group';AutopilotLastContacted=[datetime]'2024-01-18T12:00:00'}
                $entraValues=@($entra);$intuneValues=@($intune)
                $settings=@{IncludeJoinType=@('AzureAD registered','Not available');IncludeOperatingSystem=@();ExcludeOperatingSystem=@();Exclusions=@();IncludeAutopilotInventory=$true}
                $failure=''
                if($case -eq 'unmatched') {$intuneValues=@()}
                elseif($case -eq 'intune-only') {$entraValues=@();$intune.AzureAdDeviceId='orphan';$intune.EntraDeviceObjectId=$null}
                elseif($case -eq 'excluded') {$settings.Exclusions=@('owned')}
                elseif($case -eq 'duplicate') {
                    $duplicate=[pscustomobject]@{Name='owned';TrustType='Hybrid AzureAD';DeviceId='hybrid-reference';OperatingSystem='Windows';AutopilotOnboarded=$false}
                    $entraValues=@($entra,$duplicate);$settings.IncludeDuplicateNameProtectionInventory=$true
                }
                elseif($case -eq 'safety-entra') {$settings.SafetyEntraLimit=2}
                elseif($case -eq 'safety-intune') {$settings.SafetyIntuneLimit=2}
                elseif($case -eq 'warning-entra') {$failure='entra'}
                elseif($case -eq 'warning-intune') {$failure='intune'}
                Set-OfflineCloudInventory -Entra $entraValues -Intune $intuneValues -Failure $failure
                $result=@(Get-InitialCloudDevices @settings)
                $rows=@($result|Where-Object {$_ -is [pscustomobject]}|Select-Object Name,RecordState,IntuneLinkState,OperatingSystem,RegisteredDays,EntraRegisteredDays,IntuneRegisteredDays,AutopilotInventoryLoaded,AutopilotOnboarded,AutopilotDeviceId,AutopilotGroupTag,AutopilotLastContactedDays,DuplicateNameCount,PreserveDuplicateNameGroup,DuplicateNameProtectionReason)
                [pscustomobject]@{case=$case;count=$result.Count;failed=($result.Count -eq 1 -and $result[0] -is [bool] -and -not $result[0]);rows=$rows;calls=@(Read-OfflineCloudCalls);inputEntra=@($entraValues|ForEach-Object {$_.Name+':'+$_.TrustType});inputIntune=$intune.AutopilotDeviceId}|ConvertTo-Json -Depth 7 -Compress
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.True(original == generated, "Original: " + original + Environment.NewLine + "Generated: " + generated);
        Assert.Contains("\"AutopilotDeviceId\":\"entra-autopilot\"", generated);
        Assert.Contains("\"AutopilotInventoryLoaded\":false", generated);
        Assert.Contains("\"AutopilotGroupTag\":\"intune-group\"", generated);
        Assert.Contains("\"PreserveDuplicateNameGroup\":true", generated);
        Assert.Contains("\"failed\":true", generated);
    }
}

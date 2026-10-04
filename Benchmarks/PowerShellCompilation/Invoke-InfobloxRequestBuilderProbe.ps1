param([Parameter(Mandatory)][string]$ModulePath)
$ErrorActionPreference='Stop'
$PSModuleAutoloadingPreference='None'
Import-Module Microsoft.PowerShell.Utility -ErrorAction Stop
Import-Module Microsoft.PowerShell.Management -ErrorAction Stop
Import-Module -Name $ModulePath -Force
function global:Invoke-InfobloxQuery {
    [CmdletBinding()]param([string]$RelativeUri,[string]$Method,[System.Collections.IDictionary]$Body)
    $global:OfflineRequests.Add([pscustomobject]@{uri=$RelativeUri;method=$Method;body=$Body;keys=@($Body.Keys);type=$Body.GetType().FullName;arrayTypes=@(foreach($key in 'members','options','ms_options','exclude'){if($Body.Contains($key)){[pscustomobject]@{key=$key;type=$Body[$key].GetType().FullName;count=$Body[$key].Count}}})})
    if($global:OfflineProviderMode -eq 'failure') {'partial-response';throw 'offline request boundary failure'}
    if($global:OfflineProviderMode -eq 'empty'){return}
    'offline-reference/1';'offline-reference/2'
}
function global:Get-InfobloxExtensibleAttributeDefinition {
    [CmdletBinding()]param([string]$Name)
    if($Name -eq 'Site'){return [pscustomobject]@{list_values=@([pscustomobject]@{value='Lab';id='site-1'})}}
    [pscustomobject]@{list_values=@()}
}
$option1=[pscustomobject]@{name='routers';num=3;use_option=$true;value='192.0.2.1';vendor_class='DHCP';extra='discard-only-on-update'}
$option2=[pscustomobject]@{name='dns';num=6;use_option=$false;value='192.0.2.2';vendor_class='DHCP';extra='second'}
$cases=@(
    @{id='network-minimal';command='Add-InfobloxNetwork';args=@{Network='192.0.2.0/24';ReturnOutput=$true}}
    @{id='network-empty';command='Add-InfobloxNetwork';args=@{Subnet='192.0.2.0/24';Members=@();Options=@();MSOptions=@();Comment=''}}
    @{id='network-one';command='Add-InfobloxNetwork';args=@{Network='192.0.2.0/24';Members=@('192.0.2.1');Options=@($option1);MSOptions=@($option1);ReturnOutput=$true}}
    @{id='network-many';command='Add-InfobloxSubnet';args=@{Subnet='192.0.2.0/24';Members=@('192.0.2.1','192.0.2.2');Options=@($option1,$option2);ms_options=@($option1,$option2);DHCPGateway='192.0.2.1';DHCPLeaseTime='3600';DHCPDomainNameServers='192.0.2.2';AutoCreateReverseZone=$true;ExtensibleAttribute=@{Site='Lab';VLAN='10'};ReturnOutput=$true}}
    @{id='range-minimal';command='Add-InfobloxDHCPRange';args=@{StartAddress='192.0.2.10';EndAddress='192.0.2.20';ReturnOutput=$true}}
    @{id='range-one';command='Add-InfobloxDHCPRange';args=@{StartAddress='192.0.2.10';EndAddress='192.0.2.20';Options=@($option1);MSOptions=@($option1);Exclude=@('192.0.2.11');Comment=$false;ReturnOutput=$true}}
    @{id='range-many';command='Add-InfobloxDHCPRange';args=@{StartAddress='192.0.2.10';EndAddress='192.0.2.20';Options=@($option1,$option2);MSOptions=@($option1,$option2);Exclude=@('192.0.2.11-192.0.2.12',@{StartAddress='192.0.2.15';EndAddress='192.0.2.16'});MSServer='192.0.2.1';DDNSUpdateMode='Override';DDNSEnabled=$false;AlwaysUpdateDns=$false;Disable=$false;Comment='note';ExtensibleAttribute=@{Site='Lab'};ReturnOutput=$true}}
    @{id='range-invalid-ddns';command='Add-InfobloxDHCPRange';args=@{StartAddress='192.0.2.10';EndAddress='192.0.2.20';DDNSUpdateMode='Inherit';DDNSEnabled=$true}}
    @{id='update-no-change';command='Set-InfobloxDHCPRange';args=@{ReferenceID='range/offline'}}
    @{id='update-empty';command='Set-InfobloxDHCPRange';args=@{ReferenceID='range/offline';Options=@();MSOptions=@();Exclude=@();Comment=$null}}
    @{id='update-one';command='Set-InfobloxDHCPRange';args=@{ReferenceID='range/offline';Options=@($option1);MSOptions=@($option1);Exclude=@('192.0.2.11');Comment=$false}}
    @{id='update-many';command='Set-InfobloxDHCPRange';args=@{ReferenceID='range/offline';Options=@($option1,$option2);ms_options=@($option1,$option2);Exclude=@('192.0.2.11-192.0.2.12',@{StartAddress='192.0.2.15';EndAddress='192.0.2.16'});MSServer='192.0.2.1';DDNSUpdateMode='Override';DDNSEnabled=$false;AlwaysUpdateDns=$false;Disable=$false;ExtensibleAttribute=@{Site='Lab'}}}
    @{id='update-invalid-ddns';command='Set-InfobloxDHCPRange';args=@{ReferenceID='range/offline';DDNSUpdateMode='Inherit';DDNSEnabled=$true}}
)
foreach($connected in $true,$false){
    Set-OfflineInfobloxConfiguration -Connected $connected
    foreach($mode in 'normal','empty','failure'){
        foreach($case in $cases){
            $global:OfflineProviderMode=$mode
            $global:OfflineRequests=[Collections.Generic.List[object]]::new()
            $warnings=@();$verbose=[Collections.Generic.List[string]]::new();$failure=$null;$captured=[Collections.Generic.List[object]]::new()
            $arguments=$case.args
            try {& $case.command @arguments -ErrorAction Stop -WarningAction SilentlyContinue -WarningVariable warnings -Verbose 4>&1 | ForEach-Object {if($_ -is [Management.Automation.VerboseRecord]){$verbose.Add($_.Message)}else{$captured.Add($_)}}}
            catch {$failure=[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message}}
            [pscustomobject]@{case=$case.id;connected=$connected;mode=$mode;requests=@($global:OfflineRequests);result=@($captured);warnings=@($warnings|ForEach-Object {$_.Message});verbose=@($verbose);failure=$failure}|ConvertTo-Json -Depth 20 -Compress
        }
    }
}

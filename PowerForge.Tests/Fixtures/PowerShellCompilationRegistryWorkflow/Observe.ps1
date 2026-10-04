function Describe-RegistryError($fault) {
    $record=if($fault -is [Management.Automation.ErrorRecord]) {$fault} else {$fault.ErrorRecord}
    [pscustomobject]@{id=$record.FullyQualifiedErrorId; type=$record.Exception.GetType().FullName; message=$record.Exception.Message}
}
$cases=@(
    @{name='local-key'; arguments=@{RegistryPath='HKLM:\Software\Fixture'; Key='Value'; ComputerName='localhost'}}
    @{name='remote-complete'; arguments=@{RegistryPath='HKEY_LOCAL_MACHINE\Software\Fixture'; ComputerName='remote.invalid'; Advanced=$true; ExpandEnvironmentNames=$true}}
    @{name='default-key'; arguments=@{RegistryPath='HKCU:\Fixture'; DefaultKey=$true; ComputerName=@('localhost','remote.invalid')}}
    @{name='all-default'; arguments=@{RegistryPath='HKUAD:\Fixture'; ComputerName='localhost'}}
    @{name='all-offline-default'; arguments=@{RegistryPath='HKUDUDO:\Fixture'; ComputerName='localhost'; DoNotUnmount=$true}}
    @{name='all-offline-cleanup'; arguments=@{RegistryPath='HKUDUDO:\Fixture'; ComputerName='localhost'}}
)
foreach($case in $cases) {
    foreach($fault in 'none','error','throw') {
        foreach($action in 'Continue','SilentlyContinue','Stop') {
            foreach($stopEarly in $false,$true) {
                foreach($initialized in $false,$true) {
                  foreach($premounted in $false,$true) {
                    & $module {
                        param($mode,$initialized,$premounted)
                        $script:RegistryTrace.Clear(); $script:RegistryCalls=0; $script:RegistryFault=$mode
                        Remove-Variable CurrentGetCount,Dictionary,HiveDictionary,ReverseTypesDictionary -Scope Script -ErrorAction Ignore
                        $script:DefaultRegistryMounted=if($premounted) {[pscustomobject]@{Status=$true}} else {$null}
                        $script:OfflineRegistryMounted=if($premounted) {[ordered]@{Offline_Fixture=[pscustomobject]@{Status=$true}}} else {$null}
                        if($initialized) { Get-PSRegistryDictionaries }
                    } $fault $initialized $premounted
                    $Error.Clear(); $faults=@(); $records=@(); $caught=$null
                    $arguments=$case.arguments
                    $selected=$module.ExportedCommands['Get-PSRegistry']
                    try {
                        if($stopEarly) {
                            $records=@(& $selected @arguments -ErrorAction $action -ErrorVariable +faults -Verbose -InformationAction Continue 2>$null | Select-Object -First 1)
                        } else {
                            $records=@(& $selected @arguments -ErrorAction $action -ErrorVariable +faults -Verbose -InformationAction Continue 2>$null)
                        }
                    } catch { $caught=Describe-RegistryError $_ }
                    $state=& $module {
                        [pscustomobject]@{counter=$script:CurrentGetCount; dictionary=$script:Dictionary.Count; hive=$script:HiveDictionary.Count;
                            defaultMounted=($null -ne $script:DefaultRegistryMounted); offlineMounted=($null -ne $script:OfflineRegistryMounted);
                            trace=$script:RegistryTrace.ToArray()}
                    }
                    [pscustomobject]@{case=$case.name; fault=$fault; action=$action; stop=$stopEarly; initialized=$initialized; premounted=$premounted;
                        records=$records; state=$state; caught=$caught; faults=@($faults | ForEach-Object {Describe-RegistryError $_});
                        errors=@($Error | ForEach-Object {Describe-RegistryError $_})} | ConvertTo-Json -Compress -Depth 10
                  }
                }
            }
        }
    }
}
& $module { $script:RegistryFault='none'; $script:RegistryTrace.Clear() }
$later=@(& $module.ExportedCommands['Get-PSRegistry'] -RegistryPath 'HKLM:\Later' -Key Value -ComputerName localhost)
[pscustomobject]@{later=$later; state=(& $module {$script:CurrentGetCount})} | ConvertTo-Json -Compress -Depth 6

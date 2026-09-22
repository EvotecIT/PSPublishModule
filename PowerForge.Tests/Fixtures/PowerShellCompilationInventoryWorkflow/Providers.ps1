# Replace the two retained provider boundaries before any inventory command runs.
# Add-Type only declares the unchanged module's interop types; no native methods are called.
$env:COMPUTERNAME='fixture-local'
& $module {
    $script:InventoryTrace=[Collections.Generic.List[string]]::new()
    $script:InventoryStopState=$null
    function script:Start-InventoryFixtureCall {
        if($null -ne $script:InventoryStopState) {
            $script:InventoryStopState.Calls++
            if($script:InventoryStopState.Pause) {
                $script:InventoryStopState.Pause=$false
                [void]$script:InventoryStopState.Started.Set()
                if(!$script:InventoryStopState.Release.WaitOne(5000)) { throw 'Inventory provider was not released.' }
            }
        }
    }
    function script:Get-CimData {
        [CmdletBinding()] param($ComputerName,$Protocol,$Credential,$Class,$Properties)
        try {
            Start-InventoryFixtureCall
            $script:InventoryTrace.Add('cim:'+$Class+':'+$Properties.GetType().FullName+':'+($Properties -join ','))
            Write-Verbose 'fixture-cim'
            Write-Information 'fixture-cim'
            foreach($ordinal in $script:InventoryItems) {
                if($Class -eq 'Win32_OptionalFeature') {
                    [pscustomobject]@{PSComputerName='';Name='feature'+$ordinal;Caption='Feature';InstallState=$ordinal}
                } else {
                    [pscustomobject]@{PSComputerName='';Index=$ordinal;Model='model';Caption='disk';SerialNumber=' serial ';Description='drive';
                        MediaType='fixed';FirmwareRevision='v1';Partitions=2;Size=($ordinal*1.5GB);PNPDeviceID='fixture-device'}
                }
            }
            if($script:InventoryFault -eq 'error') { Write-Error 'fixture provider failure' -ErrorId InventoryProviderFailure }
            if($script:InventoryFault -eq 'throw') { throw 'fixture provider termination' }
        } finally {
            $script:InventoryTrace.Add('cim:finally')
            if($null -ne $script:InventoryStopState) { $script:InventoryStopState.Cleanups++ }
        }
    }
    function script:Get-ComputerSMBInfo {
        [CmdletBinding()] param($ComputerName,$Name,[switch]$SkipDiskSpace,$InputObject)
        try {
            Start-InventoryFixtureCall
            $script:InventoryTrace.Add('smb:'+$ComputerName+':'+($Name -join ',')+':'+$SkipDiskSpace)
            Write-Verbose 'fixture-smb'
            Write-Information 'fixture-smb'
            foreach($ordinal in $script:InventoryItems) { [pscustomobject]@{ComputerName=$ComputerName;Name='share'+$ordinal} }
            if($script:InventoryFault -eq 'error') { Write-Error 'fixture provider failure' -ErrorId InventoryProviderFailure }
            if($script:InventoryFault -eq 'throw') { throw 'fixture provider termination' }
        } finally {
            $script:InventoryTrace.Add('smb:finally')
            if($null -ne $script:InventoryStopState) { $script:InventoryStopState.Cleanups++ }
        }
    }
}

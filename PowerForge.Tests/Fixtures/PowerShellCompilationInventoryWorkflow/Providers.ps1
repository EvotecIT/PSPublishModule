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
                } elseif($Class -eq 'win32_processor') {
                    [pscustomobject]@{PSComputerName='';Name='cpu'+$ordinal;DeviceID='CPU'+$ordinal;Caption='Processor';
                        CurrentClockSpeed=2400;MaxClockSpeed=3200;ProcessorID='processor';ThreadCount=8;Architecture=9;
                        Status='OK';LoadPercentage=25;Manufacturer='fixture';NumberOfCores=4;NumberOfEnabledCore=4;
                        NumberOfLogicalProcessors=8}
                } elseif($Class -eq 'win32_pnpentity') {
                    [pscustomobject]@{PSComputerName='';PNPClass='fixture';Name='device'+$ordinal;Status='OK';
                        ConfigManagerErrorCode=0;DeviceID='DEV'+$ordinal;ErrorCleared=$false;ErrorDescription='';
                        LastErrorCode=0;StatusInfo=3;ClassGuid='class';CompatibleID=@('compatible');
                        HardwareID=@('hardware');Manufacturer='Fixture (Vendor)'}
                } elseif($Class.Trim() -eq 'Win32_physicalmemory') {
                    [pscustomobject]@{PSComputerName='';Manufacturer='fixture';FormFactor=8;SMBIOSMemoryType=26;
                        Capacity=2GB;Speed=3200;InterleavePosition=0;MemoryType=2;TypeDetail=128;
                        PartNumber='part';DeviceLocator='DIMM'+$ordinal;SerialNumber='serial';BankLabel='BANK'+$ordinal;
                        ConfiguredClockSpeed=3200;ConfiguredVoltage=1200}
                } elseif($Class -eq 'win32_startupCommand') {
                    [pscustomobject]@{PSComputerName='';Caption='startup'+$ordinal;Description='fixture startup';
                        Command='fixture.exe';Location='Startup';Name='startup'+$ordinal;User='fixture';UserSID='S-1-0-0'}
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

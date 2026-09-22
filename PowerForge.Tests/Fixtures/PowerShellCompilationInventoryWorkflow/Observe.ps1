foreach($command in 'disk','features','shares') {
    foreach($items in @(@{values=@()},@{values=@(1)},@{values=@(1,2,3,4,9,$null)})) {
        foreach($fault in 'none','error','throw') {
            foreach($action in 'Continue','Stop') {
                foreach($variant in 0,1) {
                    foreach($repeat in 0,1) {
                        & $module { param($items,$fault) $script:InventoryItems=$items;$script:InventoryFault=$fault;$script:InventoryTrace.Clear() } $items.values $fault
                        $records=@();$errors=@();$verbose=@();$information=@();$caught=$null
                        $common=@{ErrorAction=$action;ErrorVariable='errors';Verbose=$true;InformationVariable='information'}
                        try {
                            $records=@(switch($command) {
                                disk { Get-ComputerDisk -ComputerName fixture -All:($variant -eq 1) @common 2>$null 4>&1 }
                                features { Get-ComputerWindowsFeatures -ComputerName fixture -EnabledOnly:($variant -eq 1) @common 2>$null 4>&1 }
                                shares { Get-ComputerSMBShareList -ComputerName first,second -ShareName 's*' -SkipDiskSpace:($variant -eq 1) @common 2>$null 4>&1 }
                            })
                        } catch { $caught=$_.FullyQualifiedErrorId }
                        $order=@($records | ForEach-Object { if($_ -is [Management.Automation.VerboseRecord]) { 'verbose:'+ $_.Message } else { 'success' } })
                        $verbose=@($records | Where-Object { $_ -is [Management.Automation.VerboseRecord] })
                        $records=@($records | Where-Object { $_ -isnot [Management.Automation.VerboseRecord] })
                        [pscustomobject]@{command=$command;count=$items.values.Count;fault=$fault;action=$action;variant=$variant;repeat=$repeat;
                            records=$records;order=$order;errors=@($errors | ForEach-Object {$_.FullyQualifiedErrorId});caught=$caught;
                            verbose=@($verbose | ForEach-Object {$_.Message});information=@($information | ForEach-Object { [string]$_.MessageData });
                            trace=@(& $module { $script:InventoryTrace.ToArray() });interopLoaded=($null -ne ('Win32Share.NativeMethods' -as [type]))} |
                            ConvertTo-Json -Depth 8 -Compress
                    }
                }
            }
        }
    }
}
foreach($command in 'Get-ComputerDisk','Get-ComputerWindowsFeatures','Get-ComputerSMBShareList') {
    & $module { $script:InventoryItems=@(1,2,3);$script:InventoryFault='none';$script:InventoryTrace.Clear() }
    $qualified=$module.Name+'\'+$command
    $stopped=@(& $qualified -ComputerName fixture | Select-Object -First 1)
    $reused=@(& $qualified -ComputerName fixture)
    [pscustomobject]@{command=$command;phase='downstream-stop-reuse';stopped=$stopped;reused=$reused;
        trace=@(& $module { $script:InventoryTrace.ToArray() })} | ConvertTo-Json -Depth 8 -Compress
}

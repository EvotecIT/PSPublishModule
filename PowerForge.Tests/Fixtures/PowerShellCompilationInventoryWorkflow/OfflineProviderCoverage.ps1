$ErrorActionPreference = 'Stop'
$module = Import-Module -Name $modulePath -Force -PassThru -ErrorAction Stop
try {
    & $module.NewBoundScriptBlock({
        $script:OfflineCalls = [Collections.Generic.List[string]]::new()
        $script:NoFirewallRules = $false
        $script:MissingNode = $false
        function Get-NetFirewallRule {
            # OFFLINE_PROVIDER
            [CmdletBinding()] param([string]$CimSession)
            $script:OfflineCalls.Add('rule')
            if ($script:NoFirewallRules) { return }
            [pscustomobject]@{Name='rule-a';ID='rule-a';DisplayName='Offline rule';Enabled=$true;Direction='Inbound';Action='Allow'}
        }
        function Get-NetFirewallPortFilter {
            # OFFLINE_PROVIDER
            [CmdletBinding()] param([string]$CimSession)
            $script:OfflineCalls.Add('port')
            [pscustomobject]@{InstanceID='rule-a';Protocol='TCP';LocalPort='443';RemotePort='Any'}
        }
        function Get-NetFirewallApplicationFilter {
            # OFFLINE_PROVIDER
            [CmdletBinding()] param([string]$CimSession)
            $script:OfflineCalls.Add('filter')
            [pscustomobject]@{InstanceID='rule-a';Program='offline.exe';AppPath='C:\offline.exe'}
        }
        function Get-CimData {
            # OFFLINE_PROVIDER
            [CmdletBinding()] param([string[]]$ComputerName,[string]$Class,[pscredential]$Credential)
            $script:OfflineCalls.Add("cim:$Class")
            if ($Class -eq 'win32_operatingsystem') {
                foreach ($computer in $ComputerName) {
                    if ($script:MissingNode -and $computer -eq 'node-b') { continue }
                    [pscustomobject]@{PSComputerName=$computer;LocalDateTime=[datetime]'2024-01-02T12:30:00';
                        LastBootUpTime=[datetime]'2024-01-01T12:30:00';InstallDate=[datetime]'2023-01-01T00:00:00'}
                }
            } elseif ($Class -eq 'Win32_LocalTime') {
                foreach ($computer in $ComputerName) {
                    [pscustomobject]@{PSComputerName=$computer;Year=2024;Month=1;Day=2;Hour=12;Minute=30;Second=0}
                }
            } else { throw "Unexpected CIM class: $Class" }
        }
        foreach ($name in 'Get-NetFirewallRule','Get-NetFirewallPortFilter',
            'Get-NetFirewallApplicationFilter','Get-CimData') {
            if ((Get-Command -Name $name -ErrorAction Stop).Definition -notmatch 'OFFLINE_PROVIDER') {
                throw "Offline provider did not replace $name"
            }
        }
        $firewall = @(Get-ComputerFirewall -ComputerName 'offline-node' -ErrorAction Stop)
        [pscustomobject]@{step='firewall';count=$firewall.Count;types=@($firewall | ForEach-Object {$_.GetType().FullName});
            values=@($firewall | ForEach-Object {[pscustomobject]@{name=$_.Name;program=$_.Program;
                protocol=$_.Protocol;localPort=$_.LocalPort;remotePort=$_.RemotePort}});calls=@($script:OfflineCalls)} |
            ConvertTo-Json -Compress -Depth 7
        $firewallRepeat = @(Get-ComputerFirewall -ComputerName 'offline-node' -ErrorAction Stop)
        [pscustomobject]@{step='firewall-repeat';count=$firewallRepeat.Count;
            names=@($firewallRepeat | ForEach-Object Name);calls=@($script:OfflineCalls)} | ConvertTo-Json -Compress -Depth 7
        $script:NoFirewallRules = $true
        $firewallEmpty = @(Get-ComputerFirewall -ComputerName 'offline-node' -ErrorAction Stop)
        [pscustomobject]@{step='firewall-empty';count=$firewallEmpty.Count;calls=@($script:OfflineCalls)} |
            ConvertTo-Json -Compress -Depth 7
        $script:NoFirewallRules = $false
        $times = @(Get-ComputerTime -TimeSource 'offline-clock' -TimeTarget @('node-a','node-b') -ForceCIM -ErrorAction Stop)
        [pscustomobject]@{step='time';count=$times.Count;types=@($times | ForEach-Object {$_.GetType().FullName});
            values=@($times | ForEach-Object {[pscustomobject]@{name=$_.Name;source=$_.TimeSourceName;
                status=$_.Status;minutes=$_.TimeDifferenceMinutes;bootDays=$_.LastBootUpTimeInDays}});
            calls=@($script:OfflineCalls)} | ConvertTo-Json -Compress -Depth 7
        $timesRepeat = @(Get-ComputerTime -TimeSource 'offline-clock' -TimeTarget @('node-a','node-b') -ForceCIM -ErrorAction Stop)
        [pscustomobject]@{step='time-repeat';count=$timesRepeat.Count;
            names=@($timesRepeat | ForEach-Object Name);calls=@($script:OfflineCalls)} | ConvertTo-Json -Compress -Depth 7
        $script:MissingNode = $true
        $timesMissing = @(Get-ComputerTime -TimeSource 'offline-clock' -TimeTarget @('node-a','node-b') -ForceCIM -ErrorAction Stop)
        [pscustomobject]@{step='time-missing';count=$timesMissing.Count;
            values=@($timesMissing | ForEach-Object {[pscustomobject]@{name=$_.Name;status=$_.Status;
                minutes=$_.TimeDifferenceMinutes;bootDays=$_.LastBootUpTimeInDays}});calls=@($script:OfflineCalls)} |
            ConvertTo-Json -Compress -Depth 7
    })
} finally {
    Remove-Module -Name $module.Name -Force -ErrorAction Stop
}
[pscustomobject]@{step='cleanup';remaining=@(Get-Module -Name PSSharedGoods).Count} | ConvertTo-Json -Compress

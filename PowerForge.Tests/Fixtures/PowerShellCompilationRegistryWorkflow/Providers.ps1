# The caller supplies the selected $module. All registry access stays inside these providers.
if (!$module.ExportedCommands.ContainsKey('Get-PSRegistry')) { throw 'The selected module does not export Get-PSRegistry.' }
$env:COMPUTERNAME = 'localhost'
& $module {
    $script:DebugPreference = 'Continue'
    $script:RegistryTrace = [Collections.Generic.List[string]]::new()
    $script:RegistryCalls = 0
    $script:RegistryFault = 'none'
    $script:RegistryStopState = $null
    function script:Invoke-RegistryFixtureRead {
        [CmdletBinding()]
        param($Registry, [string]$ComputerName, [switch]$Remote, [switch]$Advanced, [switch]$ExpandEnvironmentNames, [string]$Kind)
        try {
            $script:RegistryCalls++
            if ($null -ne $script:RegistryStopState) {
                $script:RegistryStopState.Calls++
                if ($script:RegistryStopState.Pause) {
                    $script:RegistryStopState.Pause=$false
                    [void]$script:RegistryStopState.Started.Set()
                    if (!$script:RegistryStopState.Release.WaitOne(5000)) { throw 'Registry provider was not released.' }
                }
            }
            $script:RegistryTrace.Add('read:'+$Kind+':'+$Registry.Registry+':'+$Registry.Key+':'+$ComputerName+':remote='+$Remote+':advanced='+$Advanced+':expand='+$ExpandEnvironmentNames+':depth='+$script:CurrentGetCount)
            Write-Verbose ('provider:'+$Kind)
            Write-Debug ('provider:'+$Kind)
            Write-Information ('provider:'+$Kind)
            if ($script:RegistryCalls -eq 1) {
                switch ($script:RegistryFault) {
                    error { Write-Error 'injected registry provider error' -ErrorId RegistryProviderFailure }
                    throw { throw 'injected registry provider termination' }
                }
            }
            foreach ($ordinal in 1,2) {
                [pscustomobject][ordered]@{
                    ComputerName=$ComputerName; Registry=$Registry.Registry; Key=$Registry.Key
                    HiveKey=$Registry.HiveKey; SubKeyName=$Registry.SubKeyName; Ordinal=$ordinal; Depth=$script:CurrentGetCount
                    PSSubKeys=@('.DEFAULT','S-1-5-21-1-2-3-1001','S-1-5-21-1-2-3-1001_Classes','S-1-5-18')
                }
            }
        } finally {
            if ($null -ne $script:RegistryStopState) { $script:RegistryStopState.Cleanups++ }
            $script:RegistryTrace.Add('provider-finally:depth='+$script:CurrentGetCount)
        }
    }
    function script:Get-PSSubRegistry {
        [CmdletBinding()]
        param($Registry, [string]$ComputerName, [switch]$Remote, [switch]$ExpandEnvironmentNames)
        Invoke-RegistryFixtureRead @PSBoundParameters -Kind key
    }
    function script:Get-PSSubRegistryComplete {
        [CmdletBinding()]
        param($Registry, [string]$ComputerName, [switch]$Remote, [switch]$Advanced, [switch]$ExpandEnvironmentNames)
        Invoke-RegistryFixtureRead @PSBoundParameters -Kind complete
    }
    function script:Mount-DefaultRegistryPath {
        [CmdletBinding()] param()
        $script:RegistryTrace.Add('mount:default:depth='+$script:CurrentGetCount)
        [pscustomobject]@{Status=$true}
    }
    function script:Mount-AllRegistryPath {
        [CmdletBinding()] param([string[]]$MountUsers)
        $script:RegistryTrace.Add('mount:offline:depth='+$script:CurrentGetCount)
        [ordered]@{Offline_Fixture=[pscustomobject]@{Status=$true}; Offline_Skipped=[pscustomobject]@{Status=$false}}
    }
    function script:Dismount-PSRegistryPath {
        [CmdletBinding()] param([string]$MountPoint)
        $script:RegistryTrace.Add('dismount:'+$MountPoint+':depth='+$script:CurrentGetCount)
        [pscustomobject]@{Status=$true}
    }
}

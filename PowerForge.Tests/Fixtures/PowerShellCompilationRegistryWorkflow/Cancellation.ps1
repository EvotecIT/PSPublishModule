# The caller supplies $modulePath and the same isolated $providerSetup used by Observe.ps1.
Add-Type -TypeDefinition @'
using System.Threading;
public sealed class RegistryWorkflowStopState {
    public readonly ManualResetEvent Started = new ManualResetEvent(false);
    public readonly ManualResetEvent Release = new ManualResetEvent(false);
    public bool Pause = true;
    public int Calls;
    public int Cleanups;
}
'@
$configure = {
    param($path, $state, $setup)
    $module=Import-Module $path -PassThru
    & ([scriptblock]::Create($setup))
    & $module {
        param($state)
        $script:RegistryStopState=$state
        $script:DefaultRegistryMounted=[pscustomobject]@{Status=$true}
    } $state
    $qualified=$module.Name+'\Get-PSRegistry'
    $selected=Get-Command $qualified -ErrorAction Stop
    if($selected.Module.Path -ne $module.Path) { throw 'Command lookup selected a different module.' }
    [pscustomobject]@{command=$qualified; module=$module.Name}
}
$inspect = {
    param($name)
    & (Get-Module $name) {
        [pscustomobject]@{counter=$script:CurrentGetCount; mounted=($null -ne $script:DefaultRegistryMounted);
            trace=$script:RegistryTrace.ToArray()}
    }
}
foreach($path in 'HKLM:\Fixture','HKUAD:\Fixture') {
    $state=[RegistryWorkflowStopState]::new()
    $ps=[powershell]::Create()
    try {
        [void]$ps.AddScript($configure.ToString()).AddArgument($modulePath).AddArgument($state).AddArgument($providerSetup)
        $selection=@($ps.Invoke())
        if($ps.HadErrors -or $selection.Count -ne 1) { throw ('Registry provider setup failed: '+$ps.Streams.Error) }
        $selected=$selection[0]
        $ps.Commands.Clear()
        [void]$ps.AddCommand($selected.command).AddParameter('RegistryPath',$path).AddParameter('ComputerName','localhost')
        $running=$ps.BeginInvoke()
        if(!$state.Started.WaitOne(5000)) { throw ('The registry workflow did not reach the provider: '+$ps.Streams.Error) }
        $stop=$ps.BeginStop($null,$null)
        $deadline=[DateTime]::UtcNow.AddSeconds(5)
        while($ps.InvocationStateInfo.State -ne 'Stopping' -and [DateTime]::UtcNow -lt $deadline) { [Threading.Thread]::Sleep(1) }
        if($ps.InvocationStateInfo.State -ne 'Stopping') { throw 'The registry workflow did not acknowledge cancellation.' }
        [void]$state.Release.Set()
        $ps.EndStop($stop)
        try { [void]$ps.EndInvoke($running) } catch {}
        $status=[string]$ps.InvocationStateInfo.State
        $errors=@($ps.Streams.Error | ForEach-Object FullyQualifiedErrorId)
        $ps.Commands.Clear(); $ps.Streams.Error.Clear()
        [void]$ps.AddScript($inspect.ToString()).AddArgument($selected.module)
        $snapshot=@($ps.Invoke())
        if($ps.HadErrors -or $snapshot.Count -ne 1) { throw 'Registry state could not be inspected after cancellation.' }
        [pscustomobject]@{path=$path; phase='stopped'; state=$status; calls=$state.Calls; cleanups=$state.Cleanups;
            snapshot=$snapshot[0]; errors=$errors} | ConvertTo-Json -Compress -Depth 8

        $ps.Commands.Clear(); $state.Calls=0; $state.Cleanups=0
        [void]$ps.AddCommand($selected.command).AddParameter('RegistryPath',$path).AddParameter('ComputerName','localhost')
        $records=@($ps.Invoke())
        $errors=@($ps.Streams.Error | ForEach-Object FullyQualifiedErrorId)
        $ps.Commands.Clear(); $ps.Streams.Error.Clear()
        [void]$ps.AddScript($inspect.ToString()).AddArgument($selected.module)
        $snapshot=@($ps.Invoke())
        [pscustomobject]@{path=$path; phase='reuse'; calls=$state.Calls; cleanups=$state.Cleanups;
            records=$records; snapshot=$snapshot[0]; errors=$errors} | ConvertTo-Json -Compress -Depth 8

        $ps.Commands.Clear()
        [void]$ps.AddScript({
            param($name,$modulePath,$setup)
            Remove-Module $name -Force
            $module=Import-Module $modulePath -PassThru
            & ([scriptblock]::Create($setup))
            & $module { [pscustomobject]@{counter=$script:CurrentGetCount; dictionary=($null -ne $script:Dictionary); mounted=($null -ne $script:DefaultRegistryMounted)} }
        }.ToString()).AddArgument($selected.module).AddArgument($modulePath).AddArgument($providerSetup)
        $fresh=@($ps.Invoke())
        if($ps.HadErrors -or $fresh.Count -ne 1) { throw ('Registry module reimport failed: '+$ps.Streams.Error) }
        [pscustomobject]@{path=$path; phase='reimport'; snapshot=$fresh[0]} | ConvertTo-Json -Compress
    } finally {
        [void]$state.Release.Set()
        $ps.Dispose(); $state.Started.Dispose(); $state.Release.Dispose()
    }
}

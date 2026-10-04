Add-Type -TypeDefinition @'
using System.Threading;
public sealed class InventoryWorkflowStopState {
    public readonly ManualResetEvent Started = new ManualResetEvent(false);
    public readonly ManualResetEvent Release = new ManualResetEvent(false);
    public bool Pause = true;
    public int Calls;
    public int Cleanups;
}
'@
foreach($command in 'Get-ComputerDisk','Get-ComputerWindowsFeatures','Get-ComputerSMBShareList') {
    $state=[InventoryWorkflowStopState]::new()
    $ps=[powershell]::Create()
    try {
        [void]$ps.AddScript({
            param($path,$setup,$state,$command)
            $module=Import-Module $path -PassThru
            & ([scriptblock]::Create($setup))
            & $module {
                param($state)
                $script:InventoryStopState=$state;$script:InventoryItems=@(1,2);$script:InventoryFault='none'
            } $state
            $qualified=$module.Name+'\'+$command
            if((Get-Command $qualified -ErrorAction Stop).Module.Path -ne $module.Path) { throw 'Wrong module selected.' }
            $qualified
        }.ToString()).AddArgument($modulePath).AddArgument($providerSetup).AddArgument($state).AddArgument($command)
        $selection=@($ps.Invoke())
        if($ps.HadErrors -or $selection.Count -ne 1) { throw ('Inventory setup failed: '+$ps.Streams.Error) }
        $ps.Commands.Clear()
        [void]$ps.AddCommand($selection[0]).AddParameter('ComputerName','fixture')
        $running=$ps.BeginInvoke()
        if(!$state.Started.WaitOne(15000)) { throw ('Inventory provider was not reached: '+$ps.Streams.Error) }
        $stop=$ps.BeginStop($null,$null)
        $deadline=[DateTime]::UtcNow.AddSeconds(5)
        while($ps.InvocationStateInfo.State -ne 'Stopping' -and [DateTime]::UtcNow -lt $deadline) { [Threading.Thread]::Sleep(1) }
        if($ps.InvocationStateInfo.State -ne 'Stopping') { throw 'Inventory cancellation was not acknowledged.' }
        [void]$state.Release.Set()
        $ps.EndStop($stop)
        try { [void]$ps.EndInvoke($running) } catch {}
        [pscustomobject]@{command=$command;phase='stopped';state=[string]$ps.InvocationStateInfo.State;calls=$state.Calls;cleanups=$state.Cleanups;
            errors=@($ps.Streams.Error | ForEach-Object FullyQualifiedErrorId)} | ConvertTo-Json -Depth 8 -Compress
        $ps.Commands.Clear();$ps.Streams.Error.Clear();$state.Calls=0;$state.Cleanups=0
        [void]$ps.AddCommand($selection[0]).AddParameter('ComputerName','fixture')
        $records=@($ps.Invoke())
        [pscustomobject]@{command=$command;phase='reuse';calls=$state.Calls;cleanups=$state.Cleanups;records=$records;
            errors=@($ps.Streams.Error | ForEach-Object FullyQualifiedErrorId)} | ConvertTo-Json -Depth 8 -Compress
    } finally {
        [void]$state.Release.Set();$ps.Dispose();$state.Started.Dispose();$state.Release.Dispose()
    }
}

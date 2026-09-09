# The caller supplies $modulePath. Every runspace and synchronization handle is local to this probe.
Add-Type -TypeDefinition @'
using System.Threading;
public sealed class RandomWorkflowStopState {
    public readonly ManualResetEvent Started = new ManualResetEvent(false);
    public readonly ManualResetEvent Release = new ManualResetEvent(false);
    public bool Pause = true;
    public int Calls;
    public int Cleanups;
}
'@
$configure = {
    param($path, $state)
    $target = Import-Module $path -PassThru
    foreach ($name in 'Get-RandomCharacters', 'Get-RandomPassword') {
        if (!$target.ExportedCommands.ContainsKey($name)) { throw ('The selected module does not export ' + $name) }
    }
    & $target {
        param($state)
        $script:RandomStopState = $state
        function script:Get-Random {
            [CmdletBinding()]
            param([int]$Maximum, [int]$Count = 1, [Parameter(ValueFromPipeline = $true)][object]$InputObject)
            process {
                try {
                    $script:RandomStopState.Calls++
                    if ($script:RandomStopState.Pause) {
                        $script:RandomStopState.Pause = $false
                        [void]$script:RandomStopState.Started.Set()
                        if (!$script:RandomStopState.Release.WaitOne(5000)) { throw 'Provider was not released.' }
                    }
                    0
                } finally { $script:RandomStopState.Cleanups++ }
            }
        }
    } $state
}
foreach ($name in 'Get-RandomCharacters', 'Get-RandomPassword') {
    $state = [RandomWorkflowStopState]::new()
    $ps = [powershell]::Create()
    try {
        [void]$ps.AddScript($configure.ToString()).AddArgument($modulePath).AddArgument($state)
        [void]$ps.Invoke()
        if ($ps.HadErrors) { throw ('Provider setup failed: ' + $ps.Streams.Error) }
        $ps.Commands.Clear()
        [void]$ps.AddCommand($name)
        if ($name -eq 'Get-RandomCharacters') { [void]$ps.AddParameter('length', 3).AddParameter('characters', 'abc') }
        $running = $ps.BeginInvoke()
        if (!$state.Started.WaitOne(5000)) { throw ('The workflow did not reach the provider: ' + $ps.Streams.Error) }
        $stop = $ps.BeginStop($null, $null)
        $deadline = [DateTime]::UtcNow.AddSeconds(5)
        while ($ps.InvocationStateInfo.State -ne 'Stopping' -and [DateTime]::UtcNow -lt $deadline) { [Threading.Thread]::Sleep(1) }
        if ($ps.InvocationStateInfo.State -ne 'Stopping') { throw 'The runspace did not acknowledge cancellation.' }
        [void]$state.Release.Set()
        $ps.EndStop($stop)
        try { [void]$ps.EndInvoke($running) } catch { }
        [pscustomobject]@{
            name = $name; state = [string]$ps.InvocationStateInfo.State
            calls = $state.Calls; cleanups = $state.Cleanups
            errors = @($ps.Streams.Error | ForEach-Object FullyQualifiedErrorId)
        } | ConvertTo-Json -Compress
        $ps.Commands.Clear(); $ps.Streams.Error.Clear(); $state.Calls = 0; $state.Cleanups = 0
        [void]$ps.AddCommand($name)
        if ($name -eq 'Get-RandomCharacters') { [void]$ps.AddParameter('length', 3).AddParameter('characters', 'abc') }
        $records = @($ps.Invoke())
        [pscustomobject]@{
            name = $name; state = [string]$ps.InvocationStateInfo.State
            calls = $state.Calls; cleanups = $state.Cleanups; records = $records
            errors = @($ps.Streams.Error | ForEach-Object FullyQualifiedErrorId)
        } | ConvertTo-Json -Compress
    } finally {
        [void]$state.Release.Set()
        $ps.Dispose()
        $state.Started.Dispose()
        $state.Release.Dispose()
    }
}

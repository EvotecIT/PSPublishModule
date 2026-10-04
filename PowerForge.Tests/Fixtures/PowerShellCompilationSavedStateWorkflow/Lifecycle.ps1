# Only the pinned saved-state workflow runs. Providers below keep all data in memory.
Add-Type -TypeDefinition @'
using System.Threading;
using System.Management.Automation;
public sealed class SavedStateWorkflowStopState {
    public readonly ManualResetEvent Started = new ManualResetEvent(false);
    public readonly ManualResetEvent Release = new ManualResetEvent(false);
    public string Stage;
    public int StopAt;
    public bool Pause = true;
    public int Calls;
    public int Cleanups;
    public static System.IAsyncResult Begin(PowerShell pipeline, PSDataCollection<PSObject> output) {
        return pipeline.BeginInvoke<PSObject,PSObject>(null, output);
    }
}
'@
$configure = {
    param($path, $state)
    $module=Import-Module $path -PassThru -Force
    & $module {
        param($state)
        $script:StopState=$state
        $script:Trace=[Collections.Generic.List[string]]::new()
        $script:Export=@{}
        $pending=[ordered]@{}
        foreach($name in 'first','second') {
            $dn='CN='+$name+',DC=example,DC=test'
            $pending[$dn]=[pscustomobject]@{SamAccountName=$name+'$';DistinguishedName=$dn;ActionDate=[datetime]'2026-01-17'}
        }
        $script:Stored=@{PendingDeletion=$pending;History=[Collections.ArrayList]@('previous')}
        function script:Invoke-IsolatedSavedStateBoundary {
            param($stage)
            if($script:StopState.Stage -ne $stage) { return }
            $script:StopState.Calls++
            try {
                if($script:StopState.Pause -and $script:StopState.Calls -eq $script:StopState.StopAt) {
                    $script:StopState.Pause=$false
                    [void]$script:StopState.Started.Set()
                    if(!$script:StopState.Release.WaitOne(10000)) { throw 'Saved-state cancellation release timed out.' }
                }
            } finally {
                $script:StopState.Cleanups++
            }
        }
        function script:Get-Date { [datetime]'2026-01-20' }
        function script:Test-Path { [CmdletBinding()]param($LiteralPath) $script:Trace.Add('test'); $true }
        function script:Import-Clixml {
            [CmdletBinding()]param($LiteralPath)
            $script:Trace.Add('read')
            Invoke-IsolatedSavedStateBoundary 'read'
            $script:Stored
        }
        function script:Write-Color { param($Text,$Color) $script:Trace.Add(($Text -join '')) }
        function script:ConvertFrom-DistinguishedName {
            param($DistinguishedName,[switch]$ToDomainCN)
            $script:Trace.Add('convert:'+ $DistinguishedName)
            Invoke-IsolatedSavedStateBoundary 'convert'
            'example.test'
        }
    } $state
    $qualified=$module.Name+'\Import-ComputersData'
    $selected=Get-Command $qualified -ErrorAction Stop
    if($selected.Module.Path -ne $module.Path) { throw 'Saved-state command lookup selected a different module.' }
    $export=& $module { $script:Export }
    [pscustomobject]@{command=$qualified; module=$module.Name; export=$export}
}
$invoke = {
    param($name)
    & (Get-Module $name) {
        $result=Import-ComputersData -DataStorePath 'isolated-state' -Export $script:Export
        [pscustomobject]@{result=$result;keys=@($result.Keys);samePending=[object]::ReferenceEquals($result,$script:Stored.PendingDeletion);
            sameHistory=[object]::ReferenceEquals($script:Export.History,$script:Stored.History)}
    }
}
$inspect = {
    param($name)
    & (Get-Module $name) {
        [pscustomobject]@{keys=@($script:Stored.PendingDeletion.Keys);stored=$script:Stored;export=$script:Export;trace=@($script:Trace)}
    }
}
foreach($stage in 'read','convert') {
    $state=[SavedStateWorkflowStopState]::new()
    $state.Stage=$stage
    $state.StopAt=if($stage -eq 'convert'){2}else{1}
    $freshState=[SavedStateWorkflowStopState]::new()
    $freshState.Stage=$stage
    $freshState.Pause=$false
    $ps=[powershell]::Create()
    $stoppedOutput=[Management.Automation.PSDataCollection[Management.Automation.PSObject]]::new()
    try {
        [void]$ps.AddScript($configure.ToString()).AddArgument($modulePath).AddArgument($state)
        $selection=@($ps.Invoke())
        if($ps.HadErrors -or $selection.Count -ne 1) { throw ('Saved-state setup failed: '+$ps.Streams.Error) }
        $selected=$selection[0]
        $ps.Commands.Clear()
        [void]$ps.AddCommand($selected.command).AddParameter('DataStorePath','isolated-state').AddParameter('Export',$selected.export)
        $running=[SavedStateWorkflowStopState]::Begin($ps,$stoppedOutput)
        if(!$state.Started.WaitOne(5000)) { throw ('The saved-state workflow did not reach the provider: '+$ps.Streams.Error) }
        $stop=$ps.BeginStop($null,$null)
        $deadline=[DateTime]::UtcNow.AddSeconds(5)
        while($ps.InvocationStateInfo.State -ne 'Stopping' -and [DateTime]::UtcNow -lt $deadline) { [Threading.Thread]::Sleep(1) }
        if($ps.InvocationStateInfo.State -ne 'Stopping') { throw 'The saved-state workflow did not acknowledge cancellation.' }
        [void]$state.Release.Set()
        $ps.EndStop($stop)
        $terminalError=$null
        try { [void]$ps.EndInvoke($running) } catch {
            $terminalError=[pscustomobject]@{type=$_.Exception.GetType().FullName;inner=$_.Exception.InnerException.GetType().FullName;id=$_.FullyQualifiedErrorId}
        }
        $status=[string]$ps.InvocationStateInfo.State
        $errors=@($ps.Streams.Error | ForEach-Object FullyQualifiedErrorId)
        $ps.Commands.Clear(); $ps.Streams.Error.Clear()
        [void]$ps.AddScript($inspect.ToString()).AddArgument($selected.module)
        $snapshot=@($ps.Invoke())
        if($ps.HadErrors -or $snapshot.Count -ne 1) { throw 'Saved state could not be inspected after cancellation.' }
        [pscustomobject]@{stage=$stage;phase='stopped';status=$status;calls=$state.Calls;cleanups=$state.Cleanups;
            snapshot=$snapshot[0];errors=$errors;records=@($stoppedOutput);terminalError=$terminalError} | ConvertTo-Json -Compress -Depth 12

        $ps.Commands.Clear(); $state.Calls=0; $state.Cleanups=0
        [void]$ps.AddScript($invoke.ToString()).AddArgument($selected.module)
        $records=@($ps.Invoke())
        $errors=@($ps.Streams.Error | ForEach-Object FullyQualifiedErrorId)
        $ps.Commands.Clear(); $ps.Streams.Error.Clear()
        [void]$ps.AddScript($inspect.ToString()).AddArgument($selected.module)
        $snapshot=@($ps.Invoke())
        if($ps.HadErrors -or $snapshot.Count -ne 1) { throw 'Reused saved state could not be inspected.' }
        [pscustomobject]@{stage=$stage;phase='reuse';calls=$state.Calls;cleanups=$state.Cleanups;
            records=$records;snapshot=$snapshot[0];errors=$errors} | ConvertTo-Json -Compress -Depth 12

        $ps.Commands.Clear()
        [void]$ps.AddScript({param($name) Remove-Module $name -Force}.ToString()).AddArgument($selected.module)
        [void]$ps.Invoke()
        if($ps.HadErrors) { throw ('Saved-state module removal failed: '+$ps.Streams.Error) }
        $ps.Commands.Clear()
        [void]$ps.AddScript($configure.ToString()).AddArgument($modulePath).AddArgument($freshState)
        $freshSelection=@($ps.Invoke())
        if($ps.HadErrors -or $freshSelection.Count -ne 1) { throw ('Saved-state reimport failed: '+$ps.Streams.Error) }
        $ps.Commands.Clear()
        [void]$ps.AddScript($inspect.ToString()).AddArgument($freshSelection[0].module)
        $snapshot=@($ps.Invoke())
        if($ps.HadErrors -or $snapshot.Count -ne 1) { throw 'Reimported saved state could not be inspected.' }
        [pscustomobject]@{stage=$stage;phase='reimport';snapshot=$snapshot[0]} | ConvertTo-Json -Compress -Depth 12
        $ps.Commands.Clear()
        [void]$ps.AddScript($invoke.ToString()).AddArgument($freshSelection[0].module)
        $records=@($ps.Invoke())
        $errors=@($ps.Streams.Error | ForEach-Object FullyQualifiedErrorId)
        [pscustomobject]@{stage=$stage;phase='fresh-call';calls=$freshState.Calls;cleanups=$freshState.Cleanups;
            records=$records;errors=$errors} | ConvertTo-Json -Compress -Depth 12
    } finally {
        [void]$state.Release.Set()
        $ps.Dispose()
        $stoppedOutput.Dispose()
        $state.Started.Dispose(); $state.Release.Dispose()
        $freshState.Started.Dispose(); $freshState.Release.Dispose()
    }
}

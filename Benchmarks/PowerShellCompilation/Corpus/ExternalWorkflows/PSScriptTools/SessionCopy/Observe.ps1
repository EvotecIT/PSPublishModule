param([string]$ModulePath)
$ErrorActionPreference='Stop'
Import-Module -Name $ModulePath -Force
function global:Invoke-Command {throw 'Unexpected global remote provider'}
$module=(Get-Command Copy-PSFunction).Module
& $module {
    function script:Get-Date {[datetime]'2024-01-02T03:04:05'}
    function script:Test-Path {
        [CmdletBinding()]param([string]$Path)
        [void]$script:Trace.Add('test:'+ $Path)
        $Path -notmatch 'missing'
    }
    function script:Get-Item {
        [CmdletBinding()]param([string]$Path)
        [void]$script:Trace.Add('item:'+ $Path)
        [pscustomobject]@{ScriptBlock={param($Seed) $Seed+1}}
    }
    function script:Invoke-Command {
        [CmdletBinding()]param([scriptblock]$ScriptBlock,[System.Management.Automation.Runspaces.PSSession]$Session)
        if(-not [object]::ReferenceEquals($Session,$script:Expected)){throw 'Session identity changed'}
        if($Session.Runspace.RunspaceStateInfo.State -ne 'BeforeOpen'){throw 'Unexpected connected session'}
        [void]$script:Trace.Add('invoke:'+ $Session.Name)
        if($ScriptBlock.ToString() -notmatch '\$using:item' -or $ScriptBlock.ToString() -notmatch '\$using:f'){throw 'Copy block changed'}
        # Never execute the remote body or mutate a function drive.
        if($script:Failure){Write-Error 'offline copy failure' -ErrorId OfflineCopyFailure}
        'offline-first';'offline-second'
    }
}
$runspace=$null
try {
    $connection=[System.Management.Automation.Runspaces.WSManConnectionInfo]::new()
    $runspace=[System.Management.Automation.Runspaces.RunspaceFactory]::CreateRunspace($connection)
    if($runspace.RunspaceStateInfo.State -ne 'BeforeOpen'){throw 'Unexpected opened runspace'}
    $constructor=[System.Management.Automation.Runspaces.PSSession].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic') |
        Where-Object {$_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.IsInstanceOfType($runspace)}
    $session=$constructor.Invoke([object[]]@($runspace));$session.Name='offline-session'
    & $module {param($expected) $script:Expected=$expected} $session
    foreach($failure in $false,$true){foreach($action in 'Continue','SilentlyContinue','Stop'){foreach($mode in 'direct','pipeline','downstream-stop'){
        $trace=[Collections.Generic.List[string]]::new();$records=@();$caught=$null;$ev=@();$Error.Clear()
        & $module {param($trace,$failure) $script:Trace=$trace;$script:Failure=$failure} $trace $failure
        try {
            $records=switch($mode){
                direct {@(Copy-PSFunction -Name @('owned-first','missing','owned-last') -Session $session -Force -ErrorAction $action -ErrorVariable +ev *>&1)}
                pipeline {@('owned-first','missing','owned-last' | Copy-PSFunction -Session $session -ErrorAction $action -ErrorVariable +ev *>&1)}
                downstream-stop {@(Copy-PSFunction -Name @('owned-first','missing','owned-last') -Session $session -ErrorAction $action -ErrorVariable +ev *>&1 | Select-Object -First 1)}
            }
        }catch {$caught=[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}}
        [pscustomobject]@{failure=$failure;action=$action;mode=$mode;records=@($records|ForEach-Object {if($_ -is [Management.Automation.ErrorRecord]){[pscustomobject]@{type=$_.GetType().FullName;id=$_.FullyQualifiedErrorId;message=$_.Exception.Message}}else{[pscustomobject]@{type=$_.GetType().FullName;text=$_.ToString()}}});trace=@($trace);caught=$caught;errors=@($Error|ForEach-Object {$_.FullyQualifiedErrorId});captured=@($ev|ForEach-Object {if($_ -is [Management.Automation.ErrorRecord]){$_.FullyQualifiedErrorId}else{$_.GetType().FullName+':'+$_.Message}});state=$runspace.RunspaceStateInfo.State.ToString()}|ConvertTo-Json -Depth 8 -Compress
    }}}
    if($runspace.RunspaceStateInfo.State -ne 'BeforeOpen'){throw 'Copy workflow opened runspace'}
}finally {if($null -ne $runspace){$runspace.Dispose()}}

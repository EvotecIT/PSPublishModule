param([string]$ModulePath)
$ErrorActionPreference='Stop'
Import-Module -Name $ModulePath -Force
function global:MyGetCommand {throw 'Unexpected global metadata provider'}
$module=(Get-Command GetCommands).Module
$seed=New-Module -Name OfflineCommands {
    function Get-OfflineFirst {[CmdletBinding()]param([string]$Value='first');$Value}
    function Get-OfflineSecond {[CmdletBinding()]param([int]$Value=2);$Value}
    Set-Alias -Name offlineAlias -Value Get-OfflineFirst
    Export-ModuleMember -Function Get-OfflineFirst,Get-OfflineSecond -Alias offlineAlias
}
Import-Module $seed
& $module {
    function script:MyGetCommand {
        param([string]$Cmdlet,[System.Management.Automation.Runspaces.PSSession]$Session)
        if(-not [object]::ReferenceEquals($Session,$script:Expected)){throw 'Session forwarding lost identity'}
        if($Session.Runspace.RunspaceStateInfo.State -ne 'BeforeOpen'){throw 'Unexpected opened session'}
        [void]$script:Trace.Add($Cmdlet)
        if($script:Failure){Write-Error 'offline metadata failure' -ErrorId OfflineMetadataFailure}
        [pscustomobject]@{Name=$Cmdlet;Session=$Session.Name;Kind='offline-metadata'}
    }
}
$runspace=$null
try {
    $runspace=[Management.Automation.Runspaces.RunspaceFactory]::CreateRunspace([Management.Automation.Runspaces.WSManConnectionInfo]::new())
    $constructor=[Management.Automation.Runspaces.PSSession].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic') |
        Where-Object {$_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.IsInstanceOfType($runspace)}
    $session=$constructor.Invoke([object[]]@($runspace));$session.Name='offline-session'
    & $module {param($expected) $script:Expected=$expected} $session
    foreach($remote in $false,$true){foreach($names in $false,$true){foreach($failure in $false,$true){foreach($action in 'Continue','SilentlyContinue','Stop'){
        $trace=[Collections.Generic.List[string]]::new();$records=@();$caught=$null;$ev=@();$Error.Clear()
        & $module {param($trace,$failure) $script:Trace=$trace;$script:Failure=$failure} $trace $failure
        $selected=if($remote){$session}else{$null}
        try {$records=@(GetCommands -Module OfflineCommands -Session $selected -AsNames:$names -ErrorAction $action -ErrorVariable +ev *>&1)}
        catch {$caught=[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}}
        [pscustomobject]@{remote=$remote;names=$names;failure=$failure;action=$action;records=@($records|ForEach-Object {if($_ -is [Management.Automation.ErrorRecord]){[pscustomobject]@{type=$_.GetType().FullName;id=$_.FullyQualifiedErrorId;message=$_.Exception.Message}}elseif($_ -is [string]){[pscustomobject]@{type='string';name=$_}}else{[pscustomobject]@{type=$_.GetType().FullName;name=$_.Name;kind=$_.Kind;session=$_.Session;commandType=if($_.CommandType){$_.CommandType.ToString()}else{$null}}}});trace=@($trace);caught=$caught;errors=@($Error|ForEach-Object {$_.FullyQualifiedErrorId});captured=@($ev|ForEach-Object {if($_ -is [Management.Automation.ErrorRecord]){$_.FullyQualifiedErrorId}else{$_.GetType().FullName+':'+$_.Message}});state=$runspace.RunspaceStateInfo.State.ToString()}|ConvertTo-Json -Depth 8 -Compress
    }}}}
    if($runspace.RunspaceStateInfo.State -ne 'BeforeOpen'){throw 'Helper opened a session'}
}finally {if($null -ne $runspace){$runspace.Dispose()};Remove-Module OfflineCommands -ErrorAction SilentlyContinue}

param([string]$ModulePath)
$ErrorActionPreference='Stop'
$env:WT_SESSION=$null
Import-Module -Name $ModulePath -Force
$module=(Get-Command Write-ANSIProgress).Module
& $module {function script:Get-Date {[datetime]'2024-01-02T03:04:05'}}
$ready=$false;$consoleFailure=$null
try {[Console]::SetCursorPosition(0,0);$ready=$true}
catch {$consoleFailure=$_.Exception.GetType().FullName}
[pscustomobject]@{case='console';ready=$ready;failure=$consoleFailure;redirected=[Console]::IsOutputRedirected}|ConvertTo-Json -Compress
$cases=@(
    @{Name='box';X=0;Y=0;Percent=.25;Symbol='Box'},
    @{Name='block-pipeline';X=0;Y=0;Percent=.5;Symbol='Block';Pipeline=$true},
    @{Name='circle-host';X=0;Y=0;Percent=.5;Symbol='Circle';ToHost=$true},
    @{Name='negative-x-continue';X=-1;Y=0;Percent=.25;Symbol='Box'},
    @{Name='negative-x-stop';X=-1;Y=0;Percent=.25;Symbol='Box';Action='Stop'},
    @{Name='negative-y-pipeline';X=0;Y=-1;Percent=.25;Symbol='Circle';Pipeline=$true},
    @{Name='host-stop';X=0;Y=0;Percent=.25;Symbol='Block';ToHost=$true;Action='Stop'},
    @{Name='downstream-stop';X=0;Y=0;Percent=.25;Symbol='Box';Pipeline=$true;Stop=$true},
    @{Name='invalid-color';X=0;Y=0;Percent=.25;Symbol='Box';Color='invalid'},
    @{Name='invalid-percent';X=0;Y=0;Percent=0;Symbol='Box'}
)
foreach($case in $cases){
    $records=@();$ev=@();$caught=$null;$Error.Clear()
    $point=[Management.Automation.Host.Coordinates]::new($case.X,$case.Y)
    $action=if($case.Action){$case.Action}else{'Continue'}
    $options=@{Position=$point;BarSymbol=$case.Symbol;ToHost=[bool]$case.ToHost;ErrorAction=$action;ErrorVariable='+ev'}
    if($case.Color){$options.ProgressColor=$case.Color}
    if($action -eq 'Stop' -or $case.Name -in 'invalid-color','invalid-percent') {try {
        if($case.Stop){$records=@(.25,.5|Write-ANSIProgress @options *>&1|Select-Object -First 1)}
        elseif($case.Pipeline){$records=@(.25,.5|Write-ANSIProgress @options *>&1)}
        else {$records=@(Write-ANSIProgress -PercentComplete $case.Percent @options *>&1)}
    }catch {$caught=[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}}}
    else {
        if($case.Stop){$records=@(.25,.5|Write-ANSIProgress @options *>&1|Select-Object -First 1)}
        elseif($case.Pipeline){$records=@(.25,.5|Write-ANSIProgress @options *>&1)}
        else {$records=@(Write-ANSIProgress -PercentComplete $case.Percent @options *>&1)}
    }
    [pscustomobject]@{case=$case.Name;records=@($records|ForEach-Object {
        if($_ -is [Management.Automation.ErrorRecord]){[pscustomobject]@{type=$_.GetType().FullName;id=$_.FullyQualifiedErrorId;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}}
        else {[pscustomobject]@{type=$_.GetType().FullName;characters=@($_.ToString().ToCharArray()|ForEach-Object {[int]$_})}}
    });caught=$caught;captured=@($ev|ForEach-Object {if($_ -is [Management.Automation.ErrorRecord]){$_.FullyQualifiedErrorId}else{$_.GetType().FullName+':'+$_.Message}});errors=@($Error|ForEach-Object {$_.FullyQualifiedErrorId});position=@($point.X,$point.Y)}|ConvertTo-Json -Depth 8 -Compress
}

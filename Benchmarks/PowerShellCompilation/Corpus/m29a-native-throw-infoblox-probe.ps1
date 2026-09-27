param([string]$ModulePath,[string]$OutputPath)
$ErrorActionPreference='Stop'
$module=Import-Module $ModulePath -Force -PassThru
# Replace the service boundary inside the module; no network command is invoked.
& $module {
    function script:Invoke-InfobloxQuery {
        [CmdletBinding(SupportsShouldProcess=$true)]
        param([string]$RelativeUri,[string]$Method,[object]$QueryParameter)
        $script:QueryCalls++
        if($script:QueryShape -eq 'empty'){return}
        [pscustomobject]@{uri=$RelativeUri;method=$Method;query=$QueryParameter;whatIf=$WhatIfPreference}
        if($script:QueryShape -eq 'partial-failure'){throw 'offline-provider-failure'}
        if($script:QueryShape -eq 'many'){[pscustomobject]@{uri='second';method=$Method;query=$QueryParameter;whatIf=$WhatIfPreference}}
    }
}
function Normalize($value){
    if($value -is [Management.Automation.ErrorRecord]){return [ordered]@{kind='error';id=$value.FullyQualifiedErrorId;type=$value.Exception.GetType().FullName;message=$value.Exception.Message;line=$value.InvocationInfo.ScriptLineNumber;column=$value.InvocationInfo.OffsetInLine}}
    if($value -is [Management.Automation.WarningRecord]){return [ordered]@{kind='warning';message=$value.Message}}
    if($value -is [Management.Automation.VerboseRecord]){return [ordered]@{kind='verbose';message=$value.Message}}
    return [ordered]@{kind='value';uri=$value.uri;method=$value.method;query=$value.query;whatIf=$value.whatIf}
}
$observations=@(foreach($name in 'Get-InfobloxDNSView','Get-InfoBloxSearch'){
    foreach($connected in $false,$true){
        foreach($action in 'Continue','Stop'){
            foreach($shape in 'one','empty','many','partial-failure'){
                & $module {param($connected,$shape) $script:InfobloxConfiguration=if($connected){@{offline=$true}}else{$null};$script:QueryShape=$shape;$script:QueryCalls=0} $connected $shape
                $ErrorActionPreference=$action;$caught=$null;$records=[Collections.Generic.List[object]]::new()
                $arguments=@{Verbose=$true;WarningAction='Continue';ErrorAction=$action}
                if($name -eq 'Get-InfoBloxSearch'){$arguments.IPv4Address='192.0.2.17'}
                try{& $name @arguments 4>&1 3>&1 2>&1 | ForEach-Object {$records.Add((Normalize $_))}}
                catch{$caught=Normalize $_}
                [ordered]@{name=$name;connected=$connected;action=$action;shape=$shape;records=@($records.ToArray());caught=$caught;calls=(& $module {$script:QueryCalls})}
            }
        }
    }
})
$ErrorActionPreference='Stop'
& $module {$script:InfobloxConfiguration=@{offline=$true};$script:QueryShape='one';$script:QueryCalls=0}
$observations+= [ordered]@{name='search-without-address';records=@(Get-InfoBloxSearch | ForEach-Object {Normalize $_});calls=(& $module {$script:QueryCalls})}
[ordered]@{host=[string]$PSVersionTable.PSVersion;observations=$observations}|ConvertTo-Json -Depth 20|Set-Content -LiteralPath $OutputPath -Encoding utf8

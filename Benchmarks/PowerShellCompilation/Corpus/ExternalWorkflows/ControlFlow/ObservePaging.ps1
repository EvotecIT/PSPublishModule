param(
    [Parameter(Mandatory)][string]$ModulePath,
    [ValidateSet('Format','Paging')][string]$Workflow,
    [ValidateSet('plain','loop')][string]$Mode='plain',
    [ValidateSet('empty','data','quit','quit-last','more','all','next','invalid-response')][string]$Case
)
$ErrorActionPreference='Stop'
Import-Module $ModulePath -Force
$command=if($Workflow -eq 'Format'){Get-Command Format-Stream}else{Get-Command Out-More}
$events=[Collections.Generic.List[string]]::new()
$outputs=[Collections.Generic.List[object]]::new()
$providerEvents=[Collections.Generic.List[string]]::new()
& $command.Module {
    param($providerEvents,$caseName)
    $script:ProviderEvents=$providerEvents
    $script:Responses=[Collections.Generic.Queue[string]]::new()
    switch($caseName){
        'quit' {$script:Responses.Enqueue('q')}
        'quit-last' {$script:Responses.Enqueue('q')}
        'more' {$script:Responses.Enqueue('m');$script:Responses.Enqueue('a')}
        'next' {$script:Responses.Enqueue('n');$script:Responses.Enqueue('a')}
        'invalid-response' {$script:Responses.Enqueue('invalid');$script:Responses.Enqueue('a')}
        default {$script:Responses.Enqueue('a')}
    }
    # These command providers are module-owned. Never read a key or alter a screen.
    function script:Read-Host {
        $script:ProviderEvents.Add('read')
        if($script:Responses.Count -eq 0){throw 'Unexpected additional paging prompt.'}
        $script:Responses.Dequeue()
    }
    function script:Write-Host {param($Object,$ForegroundColor,[switch]$NoNewline);$script:ProviderEvents.Add('host:'+"$Object")}
    function script:Clear-Host {throw 'Screen mutation is forbidden in this proof.'}
    function script:Format-TransposeTable {throw 'Transpose is outside this proof.'}
    function script:Format-PSTable {
        param($Object,$Property,$ExcludeProperty,$NoAliasOrScriptProperties,$DisplayPropertySet,$PreScanHeaders,$OverwriteHeaders,[switch]$SkipTitle)
        $script:ProviderEvents.Add('format:'+@($Object).Count)
        $rows=[Collections.ArrayList]::new()
        if(-not $SkipTitle){[void]$rows.Add(@('Value'))}
        foreach($item in $Object){[void]$rows.Add(@("$($item.Value)"))}
        ,$rows
    }
} $providerEvents $Case
function Invoke-ObservedWorkflow {
    if($Workflow -eq 'Format'){
        $items=if($Case -eq 'empty'){@()}else{@([pscustomobject]@{Value='alpha'},[pscustomobject]@{Value='beta'})}
        & $command -InputObject $items -Stream Output | ForEach-Object {[void]$outputs.Add($_)}
    }else{
        $items=if($Case -eq 'quit-last'){1..3}elseif($Case -eq 'data'){1..2}else{1..5}
        $items | & $command -Count 2 | ForEach-Object {[void]$outputs.Add($_)}
    }
}
try {
    if($Mode -eq 'loop'){
        foreach($iteration in 1,2){
            $events.Add('before-call:'+ $iteration)
            Invoke-ObservedWorkflow
            $events.Add('returned:'+ $iteration)
        }
    }else{
        $events.Add('before-call')
        Invoke-ObservedWorkflow
        $events.Add('returned')
    }
    $events.Add('after-call')
}catch{
    $events.Add('caught:'+ $_.FullyQualifiedErrorId)
}finally{
    $events.Add('finally')
    if(@($events | Where-Object {$_ -like 'caught:*'}).Count){throw 'The observed workflow failed before its control-flow proof.'}
    $transfers=($Workflow -eq 'Format' -and $Case -eq 'empty') -or ($Workflow -eq 'Paging' -and $Case -eq 'quit')
    if($transfers){
        $expected=if($Mode -eq 'plain'){'before-call,finally'}else{'before-call:1,after-call,finally'}
        if(($events -join ',') -ne $expected){throw 'Caller transfer or finally differed from the authored boundary.'}
    }elseif(-not $events.Contains('after-call')){throw 'Normal workflow did not resume its caller.'}
    if($Workflow -eq 'Format' -and $Case -eq 'data'){
        $expectedCount=if($Mode -eq 'loop'){12}else{6}
        if($outputs.Count -ne $expectedCount){throw 'Formatted data was not observed.'}
    }
    if($Workflow -eq 'Paging' -and $Case -in 'quit','quit-last' -and ($outputs.Count -ne 2 -or $outputs[0] -ne 1 -or $outputs[1] -ne 2)){
        throw 'The first page was not retained before quitting.'
    }
    [pscustomobject]@{phase='finally';events=@($events);outputs=@($outputs);providerEvents=@($providerEvents)} | ConvertTo-Json -Depth 5 -Compress
}
$events.Add('after-finally')
[pscustomobject]@{phase='complete';events=@($events);outputs=@($outputs);providerEvents=@($providerEvents)} | ConvertTo-Json -Depth 5 -Compress

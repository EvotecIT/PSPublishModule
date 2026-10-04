param([Parameter(Mandatory)][string]$OriginalModule,[Parameter(Mandatory)][string]$GeneratedModule)
$ErrorActionPreference='Stop'
Import-Module $OriginalModule -Force
$names=@('Read-BorrowedEvent','Read-BorrowedEvents','Read-BorrowedEventList','Read-BorrowedEventMap')
$original=@($names|ForEach-Object {Get-Command $_})
Import-Module $GeneratedModule -Force
$generated=@($names|ForEach-Object {Get-Command $_})
$records=@(Microsoft.PowerShell.Diagnostics\Get-WinEvent -LogName System -MaxEvents 2 -ErrorAction Stop)
if($records.Count -ne 2){throw 'Two readable local System records are required for this proof.'}
$list=[Collections.Generic.List[System.Diagnostics.Eventing.Reader.EventLogRecord]]::new()
foreach($record in $records){$list.Add($record)}
$map=[Collections.Generic.Dictionary[string,System.Diagnostics.Eventing.Reader.EventLogRecord]]::new()
$map.Add('first',$records[0]);$map.Add('second',$records[1])
function Observe-Borrow($Commands,$Case){
    $values=@();$caught=$null
    try{
        switch($Case){
            'scalar' {$values=@(& $Commands[0] -Record $records[0])}
            'array' {$values=@(& $Commands[1] -Records $records)}
            'pipeline' {$values=@($records|& $Commands[1])}
            'list' {$values=@(& $Commands[2] -Records $list)}
            'map' {$values=@(& $Commands[3] -Records $map)}
            'null' {$values=@(& $Commands[0] -Record $null)}
            'invalid' {$values=@(& $Commands[0] -Record 'not-an-event-record')}
            'stop' {$values=@($records|& $Commands[1]|Select-Object -First 1)}
        }
    }catch{$caught=[ordered]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message}}
    [pscustomobject]@{count=$values.Count;identities=@($values|ForEach-Object {$null -eq $_ -or [object]::ReferenceEquals($_,$records[0]) -or [object]::ReferenceEquals($_,$records[1])});
        caught=$caught;callerReadable=(-not [string]::IsNullOrEmpty($records[0].ToXml()));
        metadata=$Commands[0].Parameters['Record'].ParameterType.FullName}
}
try{
    foreach($caseName in 'scalar','array','pipeline','list','map','null','invalid','stop'){
        $expected=Observe-Borrow $original $caseName;$actual=Observe-Borrow $generated $caseName
        $expectedJson=$expected|ConvertTo-Json -Depth 5 -Compress;$actualJson=$actual|ConvertTo-Json -Depth 5 -Compress
        if($expectedJson -ne $actualJson){throw "Borrowed event identity mismatch for $caseName. Expected $expectedJson; actual $actualJson"}
        if(@($expected.identities|Where-Object {$_ -ne $true}).Count){throw 'Caller-owned record identity was not retained.'}
        [pscustomobject]@{case=$caseName;matching=$true;observation=$expected}|ConvertTo-Json -Depth 5 -Compress
    }
}finally{foreach($record in $records){$record.Dispose()}}

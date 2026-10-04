param(
    [Parameter(Mandatory)][string]$OriginalModule,
    [Parameter(Mandatory)][string]$GeneratedModule
)
$ErrorActionPreference='Stop'
Import-Module $OriginalModule -Force
$original=(Get-Command Convert-EventLogRecord)
Import-Module $GeneratedModule -Force
$generated=(Get-Command Convert-EventLogRecord)
$records=@(Microsoft.PowerShell.Diagnostics\Get-WinEvent -LogName System -MaxEvents 2 -ErrorAction Stop)
if($records.Count -ne 2){throw 'Two readable local System records are required for this proof.'}
function Get-Hash([string]$Value){
    $algorithm=[Security.Cryptography.SHA256]::Create()
    try{[BitConverter]::ToString($algorithm.ComputeHash([Text.Encoding]::UTF8.GetBytes($Value))).Replace('-','').ToLowerInvariant()}
    finally{$algorithm.Dispose()}
}
function Observe-Conversion($Command,$Case){
    $values=@();$caught=$null;$verbose=[Collections.Generic.List[string]]::new()
    $Error.Clear()
    try{
        if($Case -in 'mixed-continue','mixed-silent','mixed-stop'){
            $preference=if($Case -eq 'mixed-continue'){'Continue'}elseif($Case -eq 'mixed-silent'){'SilentlyContinue'}else{'Stop'}
            $values=[Collections.Generic.List[object]]::new()
            & $Command -LogRecord @($records[1],$records[0]) -ErrorAction $preference 2>$null|ForEach-Object {[void]$values.Add($_)}
        }else{
        switch($Case){
            'array' {$values=@(& $Command -LogRecord $records -ErrorAction Stop)}
            'single' {$values=@(& $Command -LogRecord $records[0] -ErrorAction Stop)}
            'pipeline' {$values=@($records|& $Command -ErrorAction Stop)}
            'duplicate' {$values=@(& $Command -LogRecord @($records[0],$records[0]) -ErrorAction Stop)}
            'verbose' {
                $values=@(& $Command -LogRecord $records -Verbose -ErrorAction Stop 4>&1|ForEach-Object {
                    if($_ -is [Management.Automation.VerboseRecord]){$verbose.Add($_.Message)}else{$_}
                })
            }
            'null' {$values=@(& $Command -LogRecord $null -ErrorAction Stop)}
            'invalid' {$values=@(& $Command -LogRecord 'not-an-event-record' -ErrorAction Stop)}
            'downstream-stop' {$values=@($records|& $Command -ErrorAction Stop|Select-Object -First 1)}
            'disposed' {$values=@(& $Command -LogRecord $records[0] -ErrorAction Stop)}
        }
        }
    }catch{$caught=[ordered]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message;
        line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine;source=$_.InvocationInfo.Line}}
    # Do not persist event payloads, messages, computer names or event identifiers.
    [pscustomobject]@{count=$values.Count;valueHash=(Get-Hash ($values|ConvertTo-Json -Depth 12 -Compress));
        verboseCount=$verbose.Count;verboseHash=(Get-Hash ($verbose|ConvertTo-Json -Compress));
        caught=$caught;errors=@($Error|ForEach-Object {[ordered]@{id=$_.FullyQualifiedErrorId;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine;source=$_.InvocationInfo.Line}});
        callerReadable=if($Case -in 'disposed','mixed-continue','mixed-silent','mixed-stop'){$false}else{-not [string]::IsNullOrEmpty($records[0].ToXml())};
        otherCallerReadable=(-not [string]::IsNullOrEmpty($records[1].ToXml()))}
}
try{
    foreach($caseName in 'array','single','pipeline','duplicate','verbose','null','invalid','downstream-stop','disposed','mixed-continue','mixed-silent','mixed-stop'){
        if($caseName -eq 'disposed'){$records[0].Dispose()}
        $expected=Observe-Conversion $original $caseName
        $actual=Observe-Conversion $generated $caseName
        $expectedJson=$expected|ConvertTo-Json -Depth 6 -Compress
        $actualJson=$actual|ConvertTo-Json -Depth 6 -Compress
        if($expectedJson -ne $actualJson){throw "Original/generated event conversion differs for $caseName. Expected $expectedJson; actual $actualJson"}
        if($caseName -in 'array','pipeline','duplicate' -and ($expected.count -ne 2 -or $expected.caught)){throw "Successful event conversion missing for $caseName"}
        if($caseName -in 'single','downstream-stop' -and ($expected.count -ne 1 -or $expected.caught)){throw "Single event conversion missing for $caseName"}
        if($caseName -eq 'verbose' -and ($expected.count -ne 2 -or $expected.verboseCount -eq 0 -or $expected.caught)){throw 'Verbose conversion was not executed.'}
        if($caseName -in 'mixed-continue','mixed-silent','mixed-stop' -and ($expected.count -ne 1 -or -not $expected.caught)){throw 'Mixed record failure did not retain partial output and terminating error.'}
        [pscustomobject]@{case=$caseName;matching=$true;observation=$expected}|ConvertTo-Json -Depth 6 -Compress
    }
}finally{foreach($record in $records){$record.Dispose()}}

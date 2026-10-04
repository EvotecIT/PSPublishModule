# The input function is unchanged. Only the Shell.Application provider is replaced;
# every file and directory below belongs to the invoking test fixture.
$module=(Get-Command Get-FileMetaData).Module
& $module {
    function script:New-Object {
        param([string]$ComObject)
        if($ComObject -ne 'Shell.Application') { throw 'Unexpected provider request' }
        $folder=[pscustomobject]@{}
        $folder | Add-Member ScriptMethod ParseName {param($name);[pscustomobject]@{Name=$name}}
        $folder | Add-Member ScriptMethod GetDetailsOf {
            param($file,$index)
            if($null -eq $file) { switch($index) {0 {'title'};1 {'attributes'};2 {'empty'};default {''}} }
            else { switch($index) {0 {'owned title'};1 {'excluded'};default {''}} }
        }
        $application=[pscustomobject]@{Folder=$folder}
        $application | Add-Member ScriptMethod Namespace {param($path);$this.Folder}
        $application
    }
}
$path=Join-Path $owned 'owned.txt'
[IO.File]::WriteAllText($path,'owned')
$file=Get-Item -LiteralPath $path
# This type-data override is confined to this disposable host process. The
# FileInfo remains real; only its version metadata is supplied deterministically.
Update-TypeData -TypeName System.IO.FileInfo -MemberType ScriptProperty -MemberName VersionInfo -Value {$this.PSObject.Properties['OwnedVersion'].Value} -Force
$file | Add-Member NoteProperty OwnedVersion "Name: first`rFlag: False`rName: last`rTrueFlag: True`rEmpty: "
$directory=Get-Item -LiteralPath $owned
$cases=@(
    @{id='version-object';value=$file},
    @{id='path';value=$path},
    @{id='array';value=@($file,$file)},
    @{id='directory';value=$directory},
    @{id='missing';value=(Join-Path $owned 'missing.txt')},
    @{id='invalid';value=7},
    @{id='null';value=$null})
foreach($case in $cases) {
    foreach($asMap in $false,$true) {
        $warnings=@()
        $values=@(Get-FileMetaData -File $case.value -AsHashTable:$asMap -WarningAction SilentlyContinue -WarningVariable warnings)
        $described=@(foreach($value in $values) {
            $names=if($asMap) {@($value.Keys)} else {@($value.PSObject.Properties.Name)}
            [pscustomobject]@{type=$value.GetType().FullName;fields=@(foreach($name in $names) {
                $field=if($asMap) {$value[$name]} else {$value.$name}
                [pscustomobject]@{name=$name;type=$field.GetType().FullName;value=$field}
            })}
        })
        [pscustomobject]@{id=$case.id;map=$asMap;values=$described;warnings=@($warnings | ForEach-Object {$_.Message.Replace($owned,'<owned>')})} | ConvertTo-Json -Depth 9 -Compress
    }
}
$pipeline=@($file,$directory,$file | Get-FileMetaData -AsHashTable)
[pscustomobject]@{id='pipeline';count=$pipeline.Count;names=@($pipeline | ForEach-Object {$_.Name});flags=@($pipeline | ForEach-Object {$_.Flag})} | ConvertTo-Json -Compress
foreach($mode in 'assign','compound','increment') {
    foreach($failure in 'none','key','rhs','null') {
        $trace=[Collections.Generic.List[string]]::new()
        $key=[pscustomobject]@{Trace=$trace;Failure=$failure;Data=@{0=@{1='target'}}}
        $key | Add-Member ScriptProperty Names { $this.Trace.Add('key:'+ $marker); if($this.Failure -eq 'key') {throw 'key failure'}; if($this.Failure -ne 'null') {$this.Data} }
        $value=[pscustomobject]@{Trace=$trace;Failure=$failure}
        $value | Add-Member ScriptMethod GetValue { $this.Trace.Add('rhs:'+ $marker);if($this.Failure -eq 'rhs') {throw 'rhs failure'};7 }
        $map=@{target=10;''=3}
        $faults=@()
        $records=@(Write-IndexedInterpolation -Map $map -Key $key -Value $value -Mode $mode -ErrorVariable faults 2>$null)
        [pscustomobject]@{id=$mode+':'+$failure;records=$records;trace=@($trace);target=$map.target;empty=$map[''];faults=@($faults | ForEach-Object {$_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName+':'+$_.InvocationInfo.ScriptLineNumber+':'+$_.InvocationInfo.OffsetInLine})} | ConvertTo-Json -Compress -Depth 5
    }
}
$map=@{before=1;after=2}
$key=[pscustomobject]@{Names=@{0=@{1='before'}}}
$value=[pscustomobject]@{Key=$key}
$value | Add-Member ScriptMethod GetValue { $this.Key.Names[0][1]='after';9 }
$records=@(Write-IndexedInterpolation -Map $map -Key $key -Value $value -Mode assign)
[pscustomobject]@{id='rhs-replacement';records=$records;before=$map.before;after=$map.after} | ConvertTo-Json -Compress
$file.OwnedVersion=''
$empty=@(Get-FileMetaData -File $file -AsHashTable)
[pscustomobject]@{id='empty-version';count=$empty.Count;keys=@($empty[0].Keys)} | ConvertTo-Json -Compress

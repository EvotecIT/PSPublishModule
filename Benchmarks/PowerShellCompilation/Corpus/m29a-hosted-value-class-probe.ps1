param([string]$SourcePath,[string]$ModulePath,[string]$DataRoot,[string]$OutputPath)
$ErrorActionPreference='Stop'
if($ModulePath){Import-Module $ModulePath -Force}else{. $SourcePath}
function Normalize-Value($value) {
 if($null -eq $value){return $null}
 if($value -is [System.Management.Automation.ErrorRecord]){return [ordered]@{error=$value.FullyQualifiedErrorId;exception=$value.Exception.GetType().FullName;category=[string]$value.CategoryInfo.Category}}
 if($value -is [array]){return ,@($value|ForEach-Object {Normalize-Value $_})}
 return [ordered]@{name=$value.Name;path=([string]$value.Path).Replace($DataRoot,'<root>');count=$value.FileCount;size=$value.FileSize;parent=([string]$value.Parent).Replace($DataRoot,'<root>');computerMatches=($value.Computername -eq [Environment]::MachineName);type=$value.GetType().FullName;nameCount=$value.NameCount;nameSize=$value.NameSize}
}
$cases=@(
 @{id='default-depth';params=@{Path=$DataRoot}},
 @{id='depth-zero';params=@{Path=$DataRoot;Depth=0}},
 @{id='depth-one';params=@{Path=$DataRoot;Depth=1}},
 @{id='depth-two';params=@{Path=$DataRoot;Depth=2}},
 @{id='empty';params=@{Path=(Join-Path $DataRoot 'Empty')}},
 @{id='leaf';params=@{Path=(Join-Path $DataRoot 'Alpha')}},
 @{id='missing';params=@{Path=(Join-Path $DataRoot 'Missing')}},
 @{id='file';params=@{Path=(Join-Path $DataRoot 'root.txt')}}
)
$rows=@(foreach($case in $cases){
 $warnings=@();$errors=@();$caught=$null
 try {$arguments=$case.params; $records=@(Get-DirectoryInfo @arguments -WarningVariable warnings -ErrorAction Continue 2>&1|ForEach-Object {Normalize-Value $_})}
 catch {$records=@();$caught=Normalize-Value $_}
 [ordered]@{id=$case.id;records=$records;warnings=@($warnings|ForEach-Object {[string]$_});caught=$caught}
})
[ordered]@{host=[string]$PSVersionTable.PSVersion;observations=$rows}|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $OutputPath -Encoding utf8

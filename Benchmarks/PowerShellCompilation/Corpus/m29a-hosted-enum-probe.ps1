param([string]$SourcePath,[string]$ModulePath)
$ErrorActionPreference='Stop'
if($ModulePath){Import-Module $ModulePath -Force} else {. $SourcePath}
$cases=@(
 @{id='thin-defaults';args=@{Width=20;Title='ABC'}},
 @{id='thick-center';args=@{Width=24;Caption='ABC';Weight='Thick';Alignment='Center'}},
 @{id='right-title';args=@{Width=22;Title='ABC';Alignment='Right'}},
 @{id='style-array';args=@{Width=24;Title='ABC';TitleStyle=@('bold','italic')}},
 @{id='numeric-enums';args=@{Width=20;Title='ABC';LineWeight=1;Alignment=2;TitleStyle=@(1,7)}},
 @{id='case-fold';args=@{Width=20;Title='ABC';Weight='tHiCk';Alignment='cEnTeR'}},
 @{id='no-title';args=@{Width=16}},
 @{id='long-title';args=@{Width=6;Title='Long title'}},
 @{id='invalid-weight';args=@{Width=20;LineWeight='missing'}},
 @{id='undefined-number';args=@{Width=20;LineWeight=9}},
 @{id='null-weight';args=@{Width=20;LineWeight=$null}},
 @{id='invalid-style-array';args=@{Width=20;TitleStyle=@('bold','missing')}}
)
$observations=@(foreach($case in $cases){
 $warnings=@();$info=@();$caught=$null;$records=@()
 try {$args=$case.args;$records=@(Write-PSHorizontalRule @args -WarningVariable warnings -WarningAction SilentlyContinue -InformationVariable info -InformationAction SilentlyContinue)}
 catch {$caught=[ordered]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;category=[string]$_.CategoryInfo.Category}}
 $information=@(foreach($record in $info){
  $data=if($record.MessageData -is [Collections.IDictionary]){
   $pairs=[ordered]@{};foreach($key in @($record.MessageData.Keys|Sort-Object)){$pairs[$key]=$record.MessageData[$key]};$pairs
  }else{[string]$record.MessageData}
  [ordered]@{data=$data;tags=@($record.Tags)}
 })
 [ordered]@{id=$case.id;records=$records;warnings=@($warnings|ForEach-Object Message);information=$information;error=$caught}
})
$command=Get-Command Write-PSHorizontalRule
$metadata=@(foreach($name in 'LineWeight','Alignment','TitleStyle'){
 $parameter=$command.Parameters[$name];$type=$parameter.ParameterType;$enum=if($type.IsArray){$type.GetElementType()}else{$type}
 [ordered]@{name=$name;type=$type.FullName;names=@([Enum]::GetNames($enum));values=@([Enum]::GetValues($enum)|ForEach-Object {[int]$_});aliases=@($parameter.Aliases)}
})
[ordered]@{host=$PSVersionTable.PSVersion.ToString();observations=$observations;metadata=$metadata}|ConvertTo-Json -Depth 18 -Compress
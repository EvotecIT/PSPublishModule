param([string]$ModulePath,[string]$OutputPath)
$ErrorActionPreference='Stop'
Import-Module $ModulePath -Force
function Normalize($v){
 if($v -is [Management.Automation.ErrorRecord]){return [ordered]@{kind='error';id=$v.FullyQualifiedErrorId;type=$v.Exception.GetType().FullName;category=[string]$v.CategoryInfo.Category;message=$v.Exception.Message}}
 if($v -is [Management.Automation.InformationRecord]){
  $data=$v.MessageData
  if($data -is [Management.Automation.Language.Ast]){$value=[ordered]@{type=$data.GetType().FullName;text=$data.Extent.Text}}
  elseif($data -is [array]){$value=@($data|ForEach-Object {[ordered]@{type=$_.GetType().FullName;kind=[string]$_.Kind;text=$_.Text}})}
  else{$value=[string]$data}
  return [ordered]@{kind='information';tags=@($v.Tags);value=$value}
 }
 return [ordered]@{kind='value';type=$v.GetType().FullName;value=[string]$v}
}
$cases=@(
 @{id='named';text='Get-Date -Format yyyy-MM-dd'},
 @{id='switch';text='Get-ChildItem -Path C:\owned -Recurse -File'},
 @{id='alias';text='gci -Path C:\owned -Filter *.ps1'},
 @{id='quoted';text='Write-Output -InputObject "hello world"'},
 @{id='array';text='Write-Output -InputObject one,two,3'},
 @{id='variable';text='Write-Output -InputObject $ownedValue'},
 @{id='hashtable';text="Write-Output -InputObject @{Name='owned';Count=2}"},
 @{id='numeric';text='Get-Date -Year 2020 -Month 1 -Day 2'},
 @{id='leading-trailing';text='  Get-Date -Format o  '},
 @{id='unknown-command';text='Missing-Owned-Command -Value test'},
 @{id='empty-validation';text=''}
)
$observations=@(foreach($case in $cases){$caught=$null;try{$records=@(Convert-CommandToHashtable -Text $case.text -InformationAction Continue 6>&1 2>&1|ForEach-Object {Normalize $_})}catch{$records=@();$caught=Normalize $_};[ordered]@{id=$case.id;records=$records;caught=$caught}})
[ordered]@{host=[string]$PSVersionTable.PSVersion;observations=$observations}|ConvertTo-Json -Depth 30|Set-Content -LiteralPath $OutputPath -Encoding utf8
param([string]$ModulePath,[string]$OutputPath)
$ErrorActionPreference='Stop'
$module=Import-Module $ModulePath -Force -PassThru
$rows=[Collections.Generic.List[object]]::new()
$model=New-Object Markdown.MAML.Model.MAML.MamlCommand
$model.Name='Get-OfflineCast';$model.Synopsis='Caller-owned model'
foreach($value in @($null,$model,@{Name='Get-Converted';Synopsis='Converted model'},'invalid-model')) {
 foreach($name in @('Convert-ManagedModel','Invoke-ConstrainedModel','Read-ConstrainedModel')) {
  $caught=$null;$records=@()
  try {$records=@(& $name -Value $value -ErrorAction Stop)}
  catch {$caught=[ordered]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message}}
  $rows.Add([ordered]@{command=$name;input=if($null -eq $value){'null'}else{$value.GetType().FullName};records=@($records|ForEach-Object {if($null -eq $_){[ordered]@{type='null'}}elseif($_ -is [string]){[ordered]@{type='string';value=$_}}else{[ordered]@{type=$_.GetType().FullName;name=$_.Name;synopsis=$_.Synopsis;callerIdentity=[object]::ReferenceEquals($_,$value)}}});caught=$caught;callerName=$model.Name})
 }
}
$rows|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $OutputPath -Encoding UTF8

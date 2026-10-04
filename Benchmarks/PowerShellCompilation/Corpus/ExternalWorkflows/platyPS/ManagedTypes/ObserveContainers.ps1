param([string]$ModulePath,[string]$OutputPath)
$ErrorActionPreference='Stop'
Import-Module $ModulePath -Force
$rows=[Collections.Generic.List[object]]::new()
foreach($name in @('Touch-DeclaredList','Touch-DeclaredMap','Read-DeclaredSet','Touch-DeclaredArray')) {
 $model=New-Object Markdown.MAML.Model.MAML.MamlCommand;$model.Name='caller'
 $list=New-Object 'Collections.Generic.List[Markdown.MAML.Model.MAML.MamlCommand]';$list.Add($model)
 $map=New-Object 'Collections.Generic.Dictionary[string,Collections.Generic.List[Markdown.MAML.Model.MAML.MamlCommand]]';$map.Add('key',$list)
 $set=New-Object 'Collections.Generic.HashSet[Markdown.MAML.Model.MAML.MamlCommand]';[void]$set.Add($model);[void]$set.Add($model)
 $array=[Markdown.MAML.Model.MAML.MamlCommand[]]@($model)
 $selected=switch($name){'Touch-DeclaredList' {,$list};'Touch-DeclaredMap' {,$map};'Read-DeclaredSet' {,$set};'Touch-DeclaredArray' {,$array}}
 $records=@(& $name -Models $selected)
 $rows.Add([ordered]@{case='borrowed';command=$name;records=@($records|ForEach-Object {[ordered]@{type=$_.GetType().FullName;name=$_.Name;identity=[object]::ReferenceEquals($_,$model)}});callerName=$model.Name;listCount=$list.Count;mapIdentity=[object]::ReferenceEquals($map['key'],$list);setCount=$set.Count;arrayIdentity=[object]::ReferenceEquals($array[0],$model);parameterType=(Get-Command $name).Parameters['Models'].ParameterType.FullName})
 $caught=$null
 try {& $name -Models 'invalid'|Out-Null} catch {$caught=[ordered]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message}}
 $rows.Add([ordered]@{case='invalid';command=$name;caught=$caught;callerName=$model.Name})
}
$model=New-Object Markdown.MAML.Model.MAML.MamlCommand;$model.Name='pipeline'
$records=@($model|Touch-DeclaredArray)
$rows.Add([ordered]@{case='pipeline-array';count=$records.Count;identity=[object]::ReferenceEquals($records[0],$model);callerName=$model.Name})
$records=@(Read-AdvisoryModelOutput)
$metadata=@((Get-Command Read-AdvisoryModelOutput).OutputType|ForEach-Object {[ordered]@{name=$_.Name;type=if($_.Type){$_.Type.FullName}else{$null}}})
$attributes=@((Get-Command Read-AdvisoryModelOutput).ScriptBlock.Attributes|Where-Object {$_ -is [System.Management.Automation.OutputTypeAttribute]}|ForEach-Object {[ordered]@{types=@($_.Type|ForEach-Object {$_.Name});parameterSets=@($_.ParameterSetName)}})
$rows.Add([ordered]@{case='advisory-output';metadata=$metadata;attributes=$attributes;records=@($records|ForEach-Object {[ordered]@{type=$_.GetType().FullName;value=$_}})})
$rows|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $OutputPath -Encoding UTF8

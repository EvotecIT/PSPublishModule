param([string]$ModulePath,[string]$OutputPath)
$ErrorActionPreference='Stop'
$module=Import-Module $ModulePath -Force -PassThru
$rows=[Collections.Generic.List[object]]::new()
foreach($preserve in $false,$true) {
 $mode=& $module {param($preserve) GetParserMode -PreserveFormatting:$preserve} $preserve
 $rows.Add([ordered]@{case='parser-mode';preserve=$preserve;type=$mode.GetType().FullName;value=$mode.ToString()})
}
foreach($text in @("---`ntitle: Offline help`nversion: 1.2.3`n---`n# Get-Offline",'# Get-Offline',"---`ntitle: 'With: colon'`n---`n# Help")) {
 $metadata=Get-MarkdownMetadata -Markdown $text
 $rows.Add([ordered]@{case='metadata';input=$text;type=if($null -eq $metadata){$null}else{$metadata.GetType().FullName};entries=@($metadata.Keys|Sort-Object|ForEach-Object {[ordered]@{key=$_;value=$metadata[$_]}})})
}
foreach($preserve in $false,$true) {foreach($noMetadata in $false,$true) {
 $model=New-Object Markdown.MAML.Model.MAML.MamlCommand
 $model.Name='Get-Offline'
 $model.Synopsis='Offline parser and model qualification'
 $text=& $module {param($model,$preserve,$noMetadata) ConvertMamlModelToMarkdown -mamlCommand $model -metadata @{title='Offline'} -PreserveFormatting:$preserve -NoMetadata:$noMetadata} $model $preserve $noMetadata
 $rows.Add([ordered]@{case='render';preserve=$preserve;noMetadata=$noMetadata;text=@($text)})
}}
foreach($url in @('https://example.invalid/offline','https://example.invalid/offline',$null)) {
 if($null -eq $model){throw 'Missing offline model'}
 & $module {param($model,$url) SetOnlineVersionUrlLink -MamlCommandObject $model -OnlineVersionUrl $url} $model $url
 $rows.Add([ordered]@{case='link';url=$url;links=@($model.Links|ForEach-Object {[ordered]@{name=$_.LinkName;uri=$_.LinkUri}})})
}
$workRoot=Join-Path (Split-Path -Parent $OutputPath) ([IO.Path]::GetFileNameWithoutExtension($OutputPath)+'-files')
$markdownFolder=Join-Path $workRoot 'markdown'
$yamlFolder=Join-Path $workRoot 'yaml'
function global:Get-OfflineDocumentation {
 <#
 .SYNOPSIS
 Returns the supplied offline text.
 .DESCRIPTION
 This command uses no external service.
 .PARAMETER Text
 The text to return.
 #>
 [CmdletBinding()]param([string]$Text='offline') $Text
}
$markdownResults=@(New-MarkdownHelp -Command Get-OfflineDocumentation -OutputFolder $markdownFolder -Metadata @{title='Offline documentation'} -OnlineVersionUrl 'https://example.invalid/offline')
$yamlResults=@(New-YamlHelp -Path (Join-Path $markdownFolder 'Get-OfflineDocumentation.md') -OutputFolder $yamlFolder)
foreach($path in @((Join-Path $markdownFolder 'Get-OfflineDocumentation.md'),(Join-Path $yamlFolder 'Get-OfflineDocumentation.yml'))) {
 $rows.Add([ordered]@{case='documentation-file';name=[IO.Path]::GetFileName($path);content=[IO.File]::ReadAllText($path)})
}
$rows.Add([ordered]@{case='documentation-results';markdown=@($markdownResults|ForEach-Object {$_.GetType().FullName});yaml=@($yamlResults|ForEach-Object {$_.GetType().FullName})})
foreach($invalid in @($null,'not-a-model')) {
 $caught=$null
 try { & $module {param($value) SetOnlineVersionUrlLink -MamlCommandObject $value -OnlineVersionUrl ''} $invalid }
 catch {$caught=[ordered]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message}}
 $rows.Add([ordered]@{case='invalid-model';value=$invalid;caught=$caught})
}
foreach($definition in @(@('GetParserMode','PreserveFormatting'),@('ConvertMamlModelToMarkdown','mamlCommand'),@('SetOnlineVersionUrlLink','MamlCommandObject'),@('Get-MarkdownMetadata','Markdown'))) {
 $parameterType=& $module {param($name,$parameter) (Get-Command $name).Parameters[$parameter].ParameterType.FullName} $definition[0] $definition[1]
 $rows.Add([ordered]@{case='parameter-type';name=$definition[0];parameter=$definition[1];type=$parameterType})
}
$rows | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputPath -Encoding UTF8

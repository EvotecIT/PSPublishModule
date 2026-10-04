param([string]$ModulePath,[string]$OutputPath)
$ErrorActionPreference='Stop'
$module=Import-Module $ModulePath -Force -PassThru
$rows=[Collections.Generic.List[object]]::new()
$workRoot=Join-Path (Split-Path -Parent $OutputPath) ([IO.Path]::GetFileNameWithoutExtension($OutputPath)+'-files')
[void](New-Item -ItemType Directory -Path $workRoot)
function global:Get-OfflineModelCommand {
 <#
 .SYNOPSIS
 Returns offline text.
 .DESCRIPTION
 Converts no external data and opens no connection.
 .PARAMETER Text
 Text selected for offline documentation.
 .PARAMETER Count
 An optional repeat count.
 .EXAMPLE
 Get-OfflineModelCommand -Text first
 Returns the first sample text.
 .NOTES
 Owned offline documentation fixture.
 .LINK
 https://example.invalid/offline
 #>
 [CmdletBinding(DefaultParameterSetName='Text')]
 param([Parameter(Mandatory,ValueFromPipeline,ParameterSetName='Text')][Alias('t')][ValidateSet('first','second')][string]$Text,
 [Parameter(ParameterSetName='Count')][int]$Count=2,[switch]$Enabled)
 $Text
}
$command=Get-Command Get-OfflineModelCommand
$help=Get-Help Get-OfflineModelCommand -Full
foreach($helpParameters in $false,$true) {foreach($placeholder in $false,$true) {foreach($fullName in $false,$true) {
 $model=& $module {param($command,$help,$helpParameters,$placeholder,$fullName) ConvertPsObjectsToMamlModel -Command $command -Help $help -UseHelpForParametersMetadata:$helpParameters -UsePlaceholderForSynopsis:$placeholder -UseFullTypeName:$fullName -ExcludeDontShow} $command $help $helpParameters $placeholder $fullName
 $rows.Add([ordered]@{case='model';helpParameters=$helpParameters;placeholder=$placeholder;fullName=$fullName;type=$model.GetType().FullName;model=($model|ConvertTo-Json -Depth 25 -Compress)})
}}}
$metadata=& $module { @((Get-Command ConvertPsObjectsToMamlModel).OutputType|ForEach-Object {[ordered]@{name=$_.Name;resolved=if($_.Type){$_.Type.FullName}else{$null}}}) }
$rows.Add([ordered]@{case='output-metadata';metadata=@($metadata)})
$list=New-Object 'Collections.Generic.List[Markdown.MAML.Model.MAML.MamlCommand]'
$first=New-Object Markdown.MAML.Model.MAML.MamlCommand;$first.Name='Get-First';$first.Synopsis='First command synopsis'
$second=New-Object Markdown.MAML.Model.MAML.MamlCommand;$second.Name='Get-Second'
$list.Add($first);$list.Add($second)
$path=Join-Path $workRoot 'OfflineModule.md'
& $module {param($folder) NewModuleLandingPage -Path $folder -ModuleName OfflineModule -ModuleGuid '00000000-0000-0000-0000-000000000123' -CmdletNames Get-First,Get-Second -Locale en-US -Version 1.2.3 -FwLink 'https://example.invalid/download' -Encoding ([Text.UTF8Encoding]::new($false))} $workRoot | Out-Null
$rows.Add([ordered]@{case='create-page';text=[IO.File]::ReadAllText($path)})
foreach($existing in $true,$false) {
 if(-not $existing){Remove-Item -LiteralPath $path}
 & $module {param($folder,$models) NewModuleLandingPage -Path $folder -ModuleName OfflineModule -RefreshModulePage -Module $models -Encoding ([Text.UTF8Encoding]::new($false)) -Force} $workRoot $list | Out-Null
 $rows.Add([ordered]@{case='refresh-page';existing=$existing;text=[IO.File]::ReadAllText($path);count=$list.Count;firstIdentity=[object]::ReferenceEquals($list[0],$first);secondIdentity=[object]::ReferenceEquals($list[1],$second);firstName=$first.Name})
}
$empty=New-Object 'Collections.Generic.List[Markdown.MAML.Model.MAML.MamlCommand]'
$caught=$null
try {& $module {param($folder,$models) NewModuleLandingPage -Path $folder -ModuleName OfflineModule -RefreshModulePage -Module $models -Encoding ([Text.UTF8Encoding]::new($false)) -Force} $workRoot $empty | Out-Null}
catch {$caught=[ordered]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message}}
$rows.Add([ordered]@{case='empty-model-list';caught=$caught;count=$empty.Count})
$type=& $module {(Get-Command NewModuleLandingPage).Parameters['Module'].ParameterType.FullName}
$rows.Add([ordered]@{case='list-parameter';type=$type})
$rows|ConvertTo-Json -Depth 30|Set-Content -LiteralPath $OutputPath -Encoding UTF8

param([Parameter(Mandatory)][string]$ModulePath)
$ErrorActionPreference='Stop'
Import-Module $ModulePath -Force
$module=(Get-Command Copy-HelpExample).Module
& $module {
 $script:Trace=[Collections.Generic.List[string]]::new()
 function script:Get-Help {
  param($Name,$Path,[switch]$Examples)
  $script:Trace.Add('help:'+ $Name+':'+$Examples.IsPresent)
  if($script:CaseName -eq 'provider-failure'){throw 'offline help failure'}
  $helpEntries=@(if($script:CaseName -ne 'empty'){[pscustomobject]@{Title='Example 1';code="'owned sample'";introduction=$null}})
  [pscustomobject]@{examples=[pscustomobject]@{example=$helpEntries}}
 }
 function script:Out-GridView {
  [CmdletBinding()]param([Parameter(ValueFromPipeline)]$InputObject,$Title,[switch]$PassThru)
  process {$script:Trace.Add('grid:'+ $Title+':'+$PassThru.IsPresent);$InputObject}
 }
 function script:Set-Clipboard {
  [CmdletBinding()]param([Parameter(ValueFromPipeline)]$Value)
  process {$script:Trace.Add('clipboard:'+ [string]$Value)}
 }
 function script:Read-Host {throw 'Forbidden interactive prompt'}
 function script:Write-Host {throw 'Forbidden interactive rendering'}
}
foreach($caseName in 'one','false-switch','empty','provider-failure','bad-name','bad-path'){
 & $module {param($caseName) $script:CaseName=$caseName;$script:Trace.Clear()} $caseName
 $warnings=@();$caught=$null;$records=@()
 try {
  $arguments=@{Name='Get-Date';ogv=$true;WarningAction='SilentlyContinue';WarningVariable='warnings';ErrorAction='Stop'}
  if($caseName -eq 'false-switch'){$arguments.ogv=$false}
  if($caseName -eq 'bad-name'){$arguments.Name='OfflineMissingCommand_ForDynamicProbe'}
  if($caseName -eq 'bad-path'){$arguments.Path=Join-Path $PSScriptRoot 'does-not-exist'}
  $records=@(Copy-HelpExample @arguments)
 }catch{$caught=[pscustomobject]@{id=$_.FullyQualifiedErrorId;message=$_.Exception.Message}}
 [pscustomobject]@{case=$caseName;records=$records;caught=$caught;warnings=@($warnings|ForEach-Object {$_.Message});trace=@(& $module {$script:Trace.ToArray()})}|ConvertTo-Json -Depth 6 -Compress
}

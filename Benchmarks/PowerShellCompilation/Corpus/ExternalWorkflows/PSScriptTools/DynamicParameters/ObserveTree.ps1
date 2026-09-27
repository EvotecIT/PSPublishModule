param([Parameter(Mandatory)][string]$ModulePath,[Parameter(Mandatory)][string]$TreeRoot)
$ErrorActionPreference='Stop'
$global:PSAnsiFileMap=@([pscustomobject]@{description='TopContainer';Ansi=''},[pscustomobject]@{description='ChildContainer';Ansi=''},[pscustomobject]@{description='file';Ansi=''})
Import-Module $ModulePath -Force
$module=(Get-Command Show-Tree).Module
& $module {
 function script:Get-Item {
  param($Path,$LiteralPath)
  if($script:ProviderMode -eq 'failure'){throw 'offline provider discovery failure'}
  if($script:ProviderMode -eq 'other'){[pscustomobject]@{PSProvider=[pscustomobject]@{Name='Offline'}};return}
  if($LiteralPath){Microsoft.PowerShell.Management\Get-Item -LiteralPath $LiteralPath}else{Microsoft.PowerShell.Management\Get-Item -Path $Path}
 }
}
foreach($caseName in 'path','literal','color','false-color','depth-zero','files','other-provider','discovery-failure'){
 & $module {param($mode) $script:ProviderMode=$mode} $(if($caseName -eq 'other-provider'){'other'}elseif($caseName -eq 'discovery-failure'){'failure'}else{'filesystem'})
 $arguments=@{Path=$TreeRoot;Depth=1;ErrorAction='Stop'}
 if($caseName -eq 'literal'){$arguments.Remove('Path');$arguments.LiteralPath=$TreeRoot}
 if($caseName -in 'color','other-provider','discovery-failure'){$arguments.ansi=$true}
 if($caseName -eq 'false-color'){$arguments.ansi=$false}
 if($caseName -eq 'depth-zero'){$arguments.Depth=0}
 if($caseName -eq 'files'){$arguments.ShowItem=$true}
 $caught=$null;$records=@()
 try{$records=@(Show-Tree @arguments)}catch{$caught=[pscustomobject]@{id=$_.FullyQualifiedErrorId;message=$_.Exception.Message}}
 [pscustomobject]@{case=$caseName;records=$records;caught=$caught}|ConvertTo-Json -Depth 6 -Compress
}

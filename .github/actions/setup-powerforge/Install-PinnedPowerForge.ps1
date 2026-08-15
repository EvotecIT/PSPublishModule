[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ManifestPath
)

$installer = Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))) 'Build/Install-PowerForgeTool.ps1'
& $installer -ManifestPath $ManifestPath -AddToPath

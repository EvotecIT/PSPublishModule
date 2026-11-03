param(
    [Parameter(Mandatory)][string] $ModuleDll,
    [Parameter(Mandatory)][string] $Root,
    [ValidateSet(0, 1)][int] $PreferSelectedRoot = 0
)
$ErrorActionPreference = 'Stop'
Import-Module $ModuleDll -Force -WarningAction SilentlyContinue -ErrorAction Stop
$name = 'Selected.Metadata.' + [guid]::NewGuid().ToString('N')
$selectedRoot = Join-Path $Root 'selected'
$otherRoot = Join-Path $Root 'other'
foreach ($entry in @(@{ Root = $selectedRoot; Version = '1.0.0' }, @{ Root = $otherRoot; Version = '2.0.0' })) {
    $moduleRoot = Join-Path $entry.Root $name
    $null = New-Item -ItemType Directory -Path $moduleRoot -Force
    Set-Content -LiteralPath (Join-Path $moduleRoot ($name + '.psd1')) -Value "@{ ModuleVersion='$($entry.Version)'; GUID='$([guid]::NewGuid())'; FunctionsToExport=@(); CmdletsToExport=@() }"
}
$env:PSModulePath = $selectedRoot + [IO.Path]::PathSeparator + $otherRoot + [IO.Path]::PathSeparator + $env:PSModulePath
$type = [PowerForge.MissingFunctionsOptions].Assembly.GetType('PowerForge.EmbeddedScripts')
$loader = $type.GetMethod('Load', [Reflection.BindingFlags] 'Static,NonPublic')
$scriptPath = Join-Path $Root 'Get-InstalledModuleInfo.ps1'
Set-Content -LiteralPath $scriptPath -Value ($loader.Invoke($null, @('Scripts/ModulePipeline/Get-InstalledModuleInfo.ps1')))
$names = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($name))
$roots = if ($PreferSelectedRoot) { [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($selectedRoot)) } else { '' }
$line = & $scriptPath -NamesB64 $names -SearchRootsB64 $roots
if ($line -notlike 'PFMODINFO::ITEM::*') { throw 'No module metadata returned.' }
$fields = $line -split '::'
[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($fields[3]))

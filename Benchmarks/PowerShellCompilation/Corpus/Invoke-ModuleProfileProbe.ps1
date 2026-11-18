#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ManifestPath,
    [Parameter(Mandatory)] [string] $ModuleId,
    [ValidateRange(1, 10000)] [int] $Calls = 20,
    [switch] $Reimport
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$env:PSModulePath = Join-Path $PSHOME 'Modules'
. (Join-Path $PSScriptRoot 'Corpus.TimingRecords.ps1')
$ManifestPath = Resolve-CorpusFileSystemPath -Path $ManifestPath
$packet = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'public-corpus.net10.json') -Raw | ConvertFrom-Json
$entries = @($packet.modules | Where-Object id -eq $ModuleId)
if ($entries.Count -ne 1) { throw "Unknown packet module: $ModuleId" }
$probe = [scriptblock]::Create($entries[0].probeScript)
for ($call = 0; $call -lt $Calls; $call++) {
    if ($Reimport) { Remove-Module -Name $ModuleId -ErrorAction SilentlyContinue }
    if ($call -eq 0 -or $Reimport) {
        $imported = @(Import-Module -Name $ManifestPath -Force -PassThru -ErrorAction Stop)
        if ($imported.Count -ne 1 -or $imported[0].Name -ine $entries[0].id) {
            throw 'Profile manifest does not match the selected packet module.'
        }
    }
    $output = @(& $probe)
    if ('ok' -notin $output) { throw 'Profile probe failed validation.' }
}
Write-Output 'Profile probe completed successfully'

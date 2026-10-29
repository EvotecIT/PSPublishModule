#Requires -Version 7.0
<#
.SYNOPSIS
Stages restored NuGet graphs for scanning on another runner.
.DESCRIPTION
Export records repository-relative asset and project paths. Import rebases only
restore path metadata in a copy; package identities, targets and edges are kept.
The output must be new so a failed or partial run cannot reuse old graphs.
.PARAMETER Mode
Export from a restored checkout, or Import from a downloaded graph artifact.
.PARAMETER Source
Checkout root for Export; exported artifact root for Import.
.PARAMETER OutputPath
New directory to receive the staged graphs.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Export', 'Import')][string] $Mode,
    [Parameter(Mandatory)][string] $Source,
    [Parameter(Mandatory)][string] $OutputPath
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($Source)
$output = [IO.Path]::GetFullPath($OutputPath)
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }

function Get-ContainedPath([string] $Base, [string] $Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or
        $Relative.Contains('\') -or $Relative.Contains(':') -or @($Relative.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count) {
        throw "Invalid graph-relative path: $Relative"
    }
    $path = [IO.Path]::GetFullPath((Join-Path $Base $Relative))
    $prefix = $Base.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (!$path.StartsWith($prefix, $comparison)) { throw "Graph path escapes its root: $Relative" }
    return $path
}

if (!(Test-Path -LiteralPath $root -PathType Container)) { throw 'Graph source directory is missing.' }
if (Test-Path -LiteralPath $output) { throw 'Graph output directory must not already exist.' }
$sourcePrefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($output.StartsWith($sourcePrefix, $comparison) -or $output.Equals($root, $comparison)) {
    throw 'Graph output must be outside the source directory.'
}

if ($Mode -eq 'Export') {
    $entries = @(foreach ($file in Get-ChildItem -LiteralPath $root -Filter project.assets.json -File -Recurse) {
        if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked asset files are unsupported.' }
        $graph = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json -AsHashtable
        $project = [string] $graph.project.restore.projectPath
        if (!$project -or !$project.StartsWith($sourcePrefix, $comparison)) {
            throw "Asset graph has no project inside the checkout: $($file.FullName)"
        }
        $relativeProject = [IO.Path]::GetRelativePath($root, $project).Replace('\', '/')
        $relativeAsset = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
        $null = Get-ContainedPath $root $relativeProject
        @{ file = $relativeAsset; project = $relativeProject }
    })
} else {
    $index = Get-Content -LiteralPath (Join-Path $root 'dependency-graphs.json') -Raw | ConvertFrom-Json -AsHashtable
    if ($index.version -ne 1 -or $index.assets -isnot [array]) { throw 'Unsupported graph artifact index.' }
    $entries = @($index.assets)
}
if ($entries.Count -eq 0) { throw 'No restored NuGet asset graphs were found.' }
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
# Validate the whole index before creating any output.
foreach ($entry in $entries) {
    $asset = Get-ContainedPath $root $entry.file
    $null = Get-ContainedPath $output $entry.project
    if ([IO.Path]::GetFileName($asset) -cne 'project.assets.json' -or !$seen.Add($entry.file) -or
        !(Test-Path -LiteralPath $asset -PathType Leaf)) { throw 'Invalid, duplicate or missing asset graph.' }
}
New-Item -ItemType Directory -Path $output | Out-Null
foreach ($entry in $entries) {
    $asset = Get-ContainedPath $root $entry.file
    $destination = Get-ContainedPath $output $entry.file
    New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destination)) | Out-Null
    if ($Mode -eq 'Export') {
        Copy-Item -LiteralPath $asset -Destination $destination
    } else {
        $graph = Get-Content -LiteralPath $asset -Raw | ConvertFrom-Json -AsHashtable
        $project = Get-ContainedPath $output $entry.project
        $graph.project.restore.projectPath = $project
        $graph.project.restore.projectUniqueName = $project
        $graph.project.restore.outputPath = [IO.Path]::GetDirectoryName($destination) + [IO.Path]::DirectorySeparatorChar
        $graph | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $destination -Encoding utf8NoBOM
    }
}
if ($Mode -eq 'Export') {
    @{ version = 1; assets = $entries } | ConvertTo-Json -Depth 10 |
        Set-Content -LiteralPath (Join-Path $output 'dependency-graphs.json') -Encoding utf8NoBOM
}
Write-Host "Staged $($entries.Count) NuGet asset graphs ($Mode)."

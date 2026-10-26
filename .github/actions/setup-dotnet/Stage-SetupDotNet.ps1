<#
.SYNOPSIS
Stages the SDK action from the current composite-action payload.
.DESCRIPTION
GitHub resolves local action paths from the caller workspace. This script copies
the sibling SDK action there so GitHub can execute it with its bundled Node
runtime, without requiring node on PATH or downloading another action revision.
.PARAMETER Remove
Removes only the staged directory carrying this job's ownership marker.
.EXAMPLE
./Stage-SetupDotNet.ps1
.EXAMPLE
./Stage-SetupDotNet.ps1 -Remove
#>
[CmdletBinding()]
param([switch] $Remove)

$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath($env:GITHUB_WORKSPACE)
$target = [IO.Path]::GetFullPath((Join-Path $workspace '.powerforge-sdk'))
if ([IO.Path]::GetDirectoryName($target) -ne $workspace.TrimEnd([IO.Path]::DirectorySeparatorChar)) {
    throw 'The staged SDK action must stay inside the caller workspace.'
}
$marker = Join-Path $target '.powerforge-stage-owner'
if ([string]::IsNullOrWhiteSpace($env:GITHUB_RUN_ID) -or [string]::IsNullOrWhiteSpace($env:GITHUB_JOB)) {
    throw 'SDK staging requires the current GitHub job identity.'
}
$owner = "$env:GITHUB_RUN_ID|$env:GITHUB_RUN_ATTEMPT|$env:GITHUB_JOB"
if (Test-Path -LiteralPath $target) {
    $item = Get-Item -LiteralPath $target -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw 'Refusing a linked SDK staging directory.'
    }
    $links = @(Get-ChildItem -LiteralPath $target -Recurse -Force | Where-Object {
        $_.Attributes -band [IO.FileAttributes]::ReparsePoint
    })
    if ($links.Count) { throw 'Refusing linked SDK staging content.' }
    $owned = (Test-Path -LiteralPath $marker -PathType Leaf) -and
        [IO.File]::ReadAllText($marker) -eq $owner
    if (-not $owned) {
        if ($Remove) { return }
        throw 'The SDK staging directory belongs to another workspace operation.'
    }
    if ($Remove) {
        Remove-Item -LiteralPath $target -Recurse -ErrorAction Stop
        return
    }
} elseif ($Remove) {
    return
} else {
    New-Item -ItemType Directory -Path $target | Out-Null
    [IO.File]::WriteAllText($marker, $owner)
}
Get-ChildItem -LiteralPath $PSScriptRoot -File | Copy-Item -Destination $target

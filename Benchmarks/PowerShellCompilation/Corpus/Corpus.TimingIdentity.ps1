# Timing must describe the downloaded source and the payload that actually built.
# A module manifest hash alone cannot identify its executable dependencies.
function Get-CorpusCompilerIdentity {
    param([string] $CliAssemblyPath)

    $directory = Split-Path -Parent $CliAssemblyPath
    $files = @($CliAssemblyPath, (Join-Path $directory 'PowerForge.dll'), (Join-Path $directory 'PowerForge.PowerShell.dll'))
    $records = @($files | ForEach-Object {
        $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($_).ProductVersion
        $commit = if ($version -match '\+([0-9a-f]{40})(?:\.|$)') { $Matches[1] } else { 'NotRecorded' }
        [ordered]@{
            name = [IO.Path]::GetFileName($_)
            productVersion = $version
            sourceCommit = $commit
            sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
    $commits = @($records.sourceCommit | Sort-Object -Unique)
    return [ordered]@{
        sourceCommit = if ($commits.Count -eq 1) { $commits[0] } else { 'NotRecorded' }
        files = $records
    }
}

function Assert-CorpusTimingIdentity {
    param([object] $Entry, [object] $Build, [string] $WorkspacePath, [string] $ArtifactRoot)

    $package = Join-Path $WorkspacePath ('packages/' + $Entry.sha256 + '.nupkg')
    if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant() -cne $Entry.sha256) {
        throw 'Timing source package hash mismatch.'
    }
    $sourceRoot = Join-Path $WorkspacePath ('extract/' + $Entry.sha256)
    $pathComparer = if ($IsWindows) { [StringComparer]::OrdinalIgnoreCase } else { [StringComparer]::Ordinal }
    $sourceFiles = [Collections.Generic.HashSet[string]]::new($pathComparer)
    $archive = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        foreach ($file in $archive.Entries) {
            if ($file.FullName.EndsWith('/', [StringComparison]::Ordinal)) { continue }
            $relative = $file.FullName.Replace('\', '/')
            $sourceFiles.Add($relative) | Out-Null
            $path = Assert-ContainedPath -Root $sourceRoot -Path (Join-Path $sourceRoot $relative) -Label 'Timing source file'
            $stream = $file.Open()
            try { $expected = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
            finally { $stream.Dispose() }
            if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $expected) {
                throw "Timing source payload hash mismatch: $relative"
            }
        }
    } finally { $archive.Dispose() }
    foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($sourceRoot, $file.FullName).Replace('\', '/')
        if ($relative -ne '.powerforge-corpus-package.json' -and -not $sourceFiles.Contains($relative)) {
            throw "Unexpected timing source payload: $relative"
        }
    }
    $inventory = @(Get-CorpusProperty $Build 'artifactFiles' @())
    if ($inventory.Count -eq 0) { throw 'Timing requires the successful build file inventory; rerun the corpus build.' }
    $artifactFiles = [Collections.Generic.HashSet[string]]::new($pathComparer)
    foreach ($file in $inventory) {
        $path = Assert-ContainedPath -Root $ArtifactRoot -Path (Join-Path $ArtifactRoot $file.relativePath) -Label 'Timing artifact file'
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $file.sha256) {
            throw "Timing artifact payload hash mismatch: $($file.relativePath)"
        }
        $artifactFiles.Add([IO.Path]::GetFullPath($path)) | Out-Null
    }
    # The main module is named after the packet entry; its complete directory can
    # contain executable scripts and managed dependencies in addition to the root.
    $moduleRoot = Join-Path $ArtifactRoot $Entry.id
    foreach ($file in Get-ChildItem -LiteralPath $moduleRoot -Recurse -File) {
        if (-not $artifactFiles.Contains($file.FullName)) { throw "Unexpected timing artifact payload: $($file.Name)" }
    }
}

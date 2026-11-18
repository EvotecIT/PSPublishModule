function Get-CorpusCompilerFiles {
    param([string] $Root)

    $items = @(Get-ChildItem -LiteralPath $Root -Recurse -Force)
    if (((Get-Item -LiteralPath $Root -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -or
        @($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) {
        throw 'Compiler snapshots require ordinary files and directories without links.'
    }
    return @($items | Where-Object { -not $_.PSIsContainer } | ForEach-Object {
        [ordered]@{
            relativePath = [IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\', '/')
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    } | Sort-Object relativePath)
}

function Get-CorpusCompilerFilesSha256 {
    param([object[]] $Files)

    $text = ($Files | ForEach-Object { $_.relativePath + "`0" + $_.sha256 }) -join "`n"
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text))).ToLowerInvariant()
}

function New-CorpusCompilerSnapshot {
    param([string] $CliAssemblyPath, [string] $SnapshotPath)

    $source = [IO.Path]::GetFullPath((Split-Path -Parent $CliAssemblyPath))
    $target = [IO.Path]::GetFullPath($SnapshotPath)
    if ($target.Equals($source, (Get-PathComparison)) -or
        $target.StartsWith($source.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, (Get-PathComparison))) {
        throw 'The corpus workspace must be outside the compiler output directory.'
    }
    if (Get-Item -LiteralPath $target -Force -ErrorAction Ignore) { throw 'Compiler snapshot directory already exists.' }
    if (Test-Path -LiteralPath (Join-Path $source '.git')) { throw 'Select a compiler build output, not a source checkout.' }
    $before = @(Get-CorpusCompilerFiles -Root $source)
    $expected = Get-CorpusCompilerFilesSha256 -Files $before
    [IO.Directory]::CreateDirectory($target) | Out-Null
    try {
        foreach ($file in $before) {
            $destination = Assert-ContainedPath -Root $target -Path (Join-Path $target $file.relativePath) -Label 'Compiler snapshot file'
            [IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
            [IO.File]::Copy((Join-Path $source $file.relativePath), $destination, $false)
        }
        $after = @(Get-CorpusCompilerFiles -Root $source)
        $copied = @(Get-CorpusCompilerFiles -Root $target)
        if ($expected -cne (Get-CorpusCompilerFilesSha256 -Files $after) -or
            $expected -cne (Get-CorpusCompilerFilesSha256 -Files $copied)) {
            throw 'Compiler files changed while creating the private snapshot; no corpus build was started.'
        }
        return [pscustomobject]@{
            cliAssemblyPath = Join-Path $target ([IO.Path]::GetFileName($CliAssemblyPath))
            files = $copied
            sha256 = $expected
        }
    } catch {
        Remove-Item -LiteralPath $target -Recurse -ErrorAction Stop
        throw
    }
}

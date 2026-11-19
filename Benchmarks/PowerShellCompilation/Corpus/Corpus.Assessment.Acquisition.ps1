# Shared pinned packet and source acquisition for assessment and artifact execution.
function Read-AssessmentPacket {
    param([string] $Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
    $text = [Text.Encoding]::UTF8.GetString($bytes).TrimStart([char]0xfeff)
    return [pscustomobject]@{ Packet = ($text | ConvertFrom-Json); Sha256 = $hash }
}

function Assert-AssessmentWorkloadId {
    param($Id)
    if ($Id -isnot [string] -or $Id -notmatch '\A[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9_-])?\z' -or
        $Id -match '^(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)') {
        throw 'Assessment workload id must be a portable filename component.'
    }
}

function Get-VerifiedPayload {
    param([object] $Entry, [string] $PayloadCache, [switch] $OfflineMode)
    if ($Entry.acquisition.kind -notin @('File', 'ZipArchive')) { throw "Unsupported acquisition kind for $($Entry.id): $($Entry.acquisition.kind)" }
    return Get-VerifiedCorpusPayload -Uri ([Uri] $Entry.acquisition.url) -Sha256 $Entry.acquisition.sha256 `
        -CacheRoot $PayloadCache -CacheExtension '.payload' -Label "assessment payload $($Entry.id)" -OfflineMode:$OfflineMode
}

function Get-AssessmentSourceRoot {
    param(
        [object] $Entry, [string] $PayloadPath, [string] $ExtractCache,
        [int] $ArchiveEntryLimit = 20000, [long] $ArchiveEntryByteLimit = 268435456,
        [long] $ArchiveTotalByteLimit = 1073741824, [double] $ArchiveCompressionRatioLimit = 200.0
    )
    Assert-AssessmentWorkloadId $Entry.id
    $entryRoot = Assert-ContainedPath -Root $ExtractCache -Path (Join-Path $ExtractCache $Entry.id) -Label 'Assessment workload extraction'
    $target = Assert-ContainedPath -Root $entryRoot -Path (Join-Path $entryRoot $Entry.acquisition.sha256) -Label 'Assessment payload extraction'
    New-Item -ItemType Directory -Path $entryRoot -Force | Out-Null
    if ($Entry.acquisition.kind -eq 'ZipArchive') {
        Expand-VerifiedCorpusArchive -PayloadPath $PayloadPath -Target $target -ContainmentRoot $ExtractCache `
            -Label "assessment payload $($Entry.id)" -EntryLimit $ArchiveEntryLimit -EntryByteLimit $ArchiveEntryByteLimit `
            -TotalByteLimit $ArchiveTotalByteLimit -CompressionRatioLimit $ArchiveCompressionRatioLimit
    } else {
        if (Test-Path -LiteralPath $target) {
            $targetItem = Get-Item -LiteralPath $target -Force
            if (($targetItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Assessment extraction is a symbolic link or junction: $target" }
            Remove-Item -LiteralPath $target -Recurse -Force
        }
        New-Item -ItemType Directory -Path $target | Out-Null
        $destination = Assert-ContainedPath -Root $target -Path (Join-Path $target $Entry.entryPoint) -Label 'Assessment file entry point'
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $PayloadPath -Destination $destination
    }
    return $target
}

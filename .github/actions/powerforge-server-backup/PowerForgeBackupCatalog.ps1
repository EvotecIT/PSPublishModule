function Get-BackupCaptureDirectory {
    param([Parameter(Mandatory)][string] $TargetRoot)

    if (-not (Test-Path -LiteralPath $TargetRoot -PathType Container)) {
        return @()
    }

    $captures = @(Get-ChildItem -LiteralPath $TargetRoot -Directory -Force -ErrorAction Stop |
        Where-Object Name -Match '^(?:\d{14}|\d{8}T\d{6}Z-\d+-\d+)$' |
        Sort-Object Name -Descending)
    foreach ($capture in $captures) {
        if (($capture.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Backup capture directory must not be a link: $($capture.FullName)"
        }
    }

    $captures
}

function Get-BackupFileSummary {
    param(
        [Parameter(Mandatory)][string] $CapturePath,
        [Parameter(Mandatory)][string] $FilePath
    )

    $relative = [IO.Path]::GetRelativePath($CapturePath, $FilePath).Replace('\', '/')
    if ($relative.StartsWith('../', [StringComparison]::Ordinal) -or
        [IO.Path]::IsPathRooted($relative)) {
        throw "Backup file escaped its capture directory: $FilePath"
    }

    $item = Get-Item -LiteralPath $FilePath -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Backup capture files must not be links: $FilePath"
    }
    [ordered]@{
        name      = $relative
        sizeBytes = $item.Length
        sha256    = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

function Update-BackupCatalog {
    param(
        [Parameter(Mandatory)][string] $TargetRoot,
        [Parameter(Mandatory)][string] $TargetRelative,
        [Parameter(Mandatory)][int] $KeepLatestInTree
    )

    $captures = @(Get-BackupCaptureDirectory -TargetRoot $TargetRoot)
    if ($captures.Count -eq 0) {
        throw "Backup target does not contain a completed capture: $TargetRoot"
    }

    $entries = foreach ($capture in $captures) {
        $unsafeLink = Get-ChildItem -LiteralPath $capture.FullName -Recurse -Force -ErrorAction Stop |
            Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 } |
            Select-Object -First 1
        if ($null -ne $unsafeLink) {
            throw "Backup capture content must not contain links: $($unsafeLink.FullName)"
        }

        $summaryPath = Join-Path $capture.FullName 'capture-summary.json'
        $metadataPath = Join-Path $capture.FullName 'capture-metadata.json'
        $summary = if (Test-Path -LiteralPath $summaryPath -PathType Leaf) {
            Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
        }
        $metadata = if (Test-Path -LiteralPath $metadataPath -PathType Leaf) {
            Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
        }
        $createdAtUtc = if ($metadata -and $null -ne $metadata.capturedAtUtc) {
            ([DateTimeOffset]$metadata.capturedAtUtc).ToUniversalTime().ToString('O')
        } elseif ($capture.Name -match '^(\d{4})(\d{2})(\d{2})T?(\d{2})(\d{2})(\d{2})Z?') {
            '{0}-{1}-{2}T{3}:{4}:{5}Z' -f $Matches[1], $Matches[2], $Matches[3], $Matches[4], $Matches[5], $Matches[6]
        }

        [ordered]@{
            stamp        = $capture.Name
            path         = "$($TargetRelative.TrimEnd('/'))/$($capture.Name)"
            createdAtUtc = $createdAtUtc
            commandCount = if ($summary) { @($summary.commandResults).Count } else { 0 }
            warningCount = if ($summary) { @($summary.warnings).Count } else { 0 }
            files        = @(Get-ChildItem -LiteralPath $capture.FullName -File -Recurse -Force |
                Sort-Object FullName |
                ForEach-Object { Get-BackupFileSummary -CapturePath $capture.FullName -FilePath $_.FullName })
        }
    }

    $latest = $captures[0].Name
    Set-Content -LiteralPath (Join-Path $TargetRoot 'LATEST.txt') -Value $latest -Encoding utf8NoBOM
    [ordered]@{
        schemaVersion = 1
        updatedAtUtc  = [DateTimeOffset]::UtcNow.ToString('O')
        latest        = $latest
        retention     = [ordered]@{ keepLatestInTree = $KeepLatestInTree }
        captures      = @($entries)
    } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $TargetRoot 'index.json') -Encoding utf8NoBOM
}

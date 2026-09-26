function Assert-LastExitCode {
    param([Parameter(Mandatory)][string] $Operation)

    if ($LASTEXITCODE -ne 0) {
        throw "$Operation failed with exit code $LASTEXITCODE."
    }
}

function Test-AgeX25519Recipient {
    param([AllowEmptyString()][string] $Recipient)

    $alphabet = 'qpzry9x8gf2tvdw0s3jn54khce6mua7l'
    if ($Recipient.Length -ne 62 -or -not $Recipient.StartsWith('age1', [StringComparison]::Ordinal)) {
        return $false
    }

    function Add-Bech32Digit {
        param([long] $Current, [int] $Digit)
        $high = $Current -shr 25
        $next = (($Current -band 0x1ffffff) -shl 5) -bxor $Digit
        foreach ($entry in @(@(1, 0x3b6a57b2), @(2, 0x26508e6d), @(4, 0x1ea119fa),
                @(8, 0x3d4233dd), @(16, 0x2a1462b3))) {
            if (($high -band $entry[0]) -ne 0) { $next = $next -bxor $entry[1] }
        }
        return [long]$next
    }

    [long]$checksum = 1
    foreach ($character in 'age'.ToCharArray()) { $checksum = Add-Bech32Digit $checksum ([int]$character -shr 5) }
    $checksum = Add-Bech32Digit $checksum 0
    foreach ($character in 'age'.ToCharArray()) { $checksum = Add-Bech32Digit $checksum ([int]$character -band 31) }
    for ($index = 4; $index -lt $Recipient.Length; $index++) {
        $digit = $alphabet.IndexOf($Recipient[$index])
        if ($digit -lt 0) { return $false }
        $checksum = Add-Bech32Digit $checksum $digit
    }
    return $checksum -eq 1 -and ($alphabet.IndexOf($Recipient[55]) -band 0x0f) -eq 0
}

function Add-BackupCaptureToGit {
    param(
        [Parameter(Mandatory)][string] $Checkout,
        [Parameter(Mandatory)][string] $CaptureRelative
    )

    $captureRoot = Join-Path $Checkout $CaptureRelative
    if (-not (Test-Path -LiteralPath $captureRoot -PathType Container)) {
        throw "Recovery capture directory is missing: $CaptureRelative"
    }
    $files = @(Get-ChildItem -LiteralPath $captureRoot -File -Recurse -Force -ErrorAction Stop)
    if ($files.Count -eq 0) { throw 'Recovery capture has no files to stage.' }

    & git -C $Checkout add --force -- $CaptureRelative
    Assert-LastExitCode 'Staging complete recovery capture'
    foreach ($file in $files) {
        $relative = [IO.Path]::GetRelativePath($Checkout, $file.FullName).Replace('\', '/')
        & git -C $Checkout ls-files --error-unmatch -- $relative > $null 2>$null
        if ($LASTEXITCODE -ne 0) { throw "Recovery capture artifact was not staged: $relative" }
    }
}

function Add-BackupCatalogToGit {
    param(
        [Parameter(Mandatory)][string] $Checkout,
        [Parameter(Mandatory)][string[]] $CatalogRelativePaths
    )

    foreach ($relative in $CatalogRelativePaths) {
        if (-not (Test-Path -LiteralPath (Join-Path $Checkout $relative) -PathType Leaf)) {
            throw "Generated backup catalog is missing: $relative"
        }
        & git -C $Checkout add --force -- $relative
        Assert-LastExitCode 'Staging generated backup catalog'
        & git -C $Checkout ls-files --error-unmatch -- $relative > $null 2>$null
        if ($LASTEXITCODE -ne 0) { throw "Generated backup catalog was not staged: $relative" }
    }
}

function Test-BackupPublicationAccepted {
    param(
        [Parameter(Mandatory)][string] $Checkout,
        [Parameter(Mandatory)][string] $Upstream
    )

    & git -C $Checkout merge-base --is-ancestor HEAD $Upstream
    if ($LASTEXITCODE -eq 0) { return $true }
    if ($LASTEXITCODE -eq 1) { return $false }
    throw 'Checking whether the remote backup branch contains this publication failed.'
}

function Invoke-GitWithRetry {
    param(
        [Parameter(Mandatory)][string] $Operation,
        [Parameter(Mandatory)][string[]] $Arguments,
        [string] $ResetPath,
        [ValidateRange(1, 5)][int] $MaxAttempts = 3,
        [ValidateRange(0, 60)][int] $RetryDelaySeconds = 5
    )

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        if (-not [string]::IsNullOrWhiteSpace($ResetPath) -and (Test-Path -LiteralPath $ResetPath)) {
            Remove-Item -LiteralPath $ResetPath -Recurse -Force
        }

        & git @Arguments
        $exitCode = $LASTEXITCODE
        if ($exitCode -eq 0) {
            return
        }
        if ($attempt -lt $MaxAttempts) {
            Write-Warning "$Operation failed with exit code $exitCode; retrying ($attempt/$MaxAttempts)."
            Start-Sleep -Seconds ($attempt * $RetryDelaySeconds)
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($ResetPath) -and (Test-Path -LiteralPath $ResetPath)) {
        Remove-Item -LiteralPath $ResetPath -Recurse -Force
    }
    throw "$Operation failed after $MaxAttempts attempts with exit code $exitCode."
}

function Invoke-BackupPublicationRebase {
    param(
        [Parameter(Mandatory)][string] $Checkout,
        [Parameter(Mandatory)][string] $Upstream,
        [Parameter(Mandatory)][string[]] $GeneratedCatalogPaths
    )

    & git -C $Checkout rebase $Upstream
    while ($LASTEXITCODE -ne 0) {
        $unmerged = @(& git -C $Checkout diff --name-only --diff-filter=U)
        if ($LASTEXITCODE -ne 0 -or $unmerged.Count -eq 0) {
            & git -C $Checkout rebase --abort 2>$null
            throw 'Rebasing the backup publication failed without a resolvable file conflict.'
        }

        $unexpected = @($unmerged | Where-Object { $_ -notin $GeneratedCatalogPaths })
        if ($unexpected.Count -ne 0) {
            & git -C $Checkout rebase --abort 2>$null
            throw "Rebasing the backup publication conflicted outside generated catalogs: $($unexpected -join ', ')"
        }

        # During a rebase HEAD is the refreshed upstream. Preserve its generated catalogs;
        # the caller regenerates both files from the combined capture tree immediately after.
        & git -C $Checkout restore --source=HEAD --staged --worktree -- @unmerged
        if ($LASTEXITCODE -ne 0) {
            & git -C $Checkout rebase --abort 2>$null
            throw 'Restoring upstream generated catalogs during backup rebase failed.'
        }
        & git -C $Checkout -c core.editor=true rebase --continue
    }
}

function Write-ActionOutput {
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][AllowEmptyString()][string] $Value
    )

    "$Name=$Value" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
}

function Write-CaptureFailureDiagnostic {
    param([Parameter(Mandatory)][string] $CaptureRoot)

    foreach ($name in @('plain-files.stderr.txt', 'encrypted-secrets.stderr.txt')) {
        $path = Join-Path $CaptureRoot $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
            continue
        }

        # Only tar/encryption stderr is surfaced. Command captures may contain sensitive service output.
        $diagnostic = ((Get-Content -LiteralPath $path -TotalCount 40) -join [Environment]::NewLine)
        $diagnostic = $diagnostic -replace '[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]', '?'
        if ($diagnostic.Length -gt 4096) {
            $diagnostic = $diagnostic.Substring(0, 4096) + [Environment]::NewLine + '[truncated]'
        }
        $stopToken = [Guid]::NewGuid().ToString('N')
        Write-Host "::group::PowerForge capture diagnostic: $name"
        Write-Host "::stop-commands::$stopToken"
        Write-Host $diagnostic
        Write-Host "::$stopToken::"
        Write-Host '::endgroup::'
    }
}

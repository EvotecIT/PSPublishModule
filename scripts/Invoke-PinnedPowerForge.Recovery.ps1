function Get-ContainedAppleRecoveryPath {
    param([Parameter(Mandatory)][string] $Path, [Parameter(Mandatory)][string] $Root)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not [IO.Path]::IsPathRooted($Path)) {
        throw 'Canonical Apple recovery evidence must contain absolute paths.'
    }
    $full = [IO.Path]::GetFullPath($Path)
    $relative = [IO.Path]::GetRelativePath($Root, $full).Replace('\', '/')
    if ([IO.Path]::IsPathRooted($relative) -or $relative -eq '..' -or $relative.StartsWith('../')) {
        throw "Canonical Apple recovery evidence escapes its verified root: $full"
    }
    return $full
}

function Register-AppleRecoveryEvidence {
    param(
        [Parameter(Mandatory)][string] $SourceCommit,
        [Parameter(Mandatory)][string] $DotNetPath,
        [Parameter(Mandatory)][string] $CliAssembly,
        [Parameter(Mandatory)][string] $NuGetPackagesPath,
        [Parameter(Mandatory)][string] $OutputPath
    )
    if ($ArgumentList[0] -notin @('apple-release', 'apple-testflight')) { return }
    if (Test-AppleWorkerControl) { return }

    $configPath = Resolve-OptionPath -Value (Get-OptionValue -Option '--config')
    $exitCode = Invoke-RedactedProcess `
        -FilePath $DotNetPath `
        -WorkingDirectory $consumer `
        -Arguments @($CliAssembly, 'apple-release', 'evidence', '--config', $configPath, '--source-commit', $SourceCommit, '--output', 'json') `
        -NuGetPackagesPath $NuGetPackagesPath `
        -CaptureStandardOutputPath $OutputPath
    if ($exitCode -ne 0) {
        if (Test-Path -LiteralPath $OutputPath -PathType Leaf) {
            [Console]::Error.Write((Get-Content -LiteralPath $OutputPath -Raw))
        }
        throw "Canonical Apple recovery evidence validation failed with exit code $exitCode."
    }
    if (-not (Test-Path -LiteralPath $OutputPath -PathType Leaf) -or
        (Get-Item -LiteralPath $OutputPath).Length -gt 32MB) {
        throw 'Canonical Apple recovery evidence output is missing or exceeds the 32 MB evidence limit.'
    }
    $evidence = Get-Content -LiteralPath $OutputPath -Raw | ConvertFrom-Json
    if ($evidence.command -ne 'apple-release' -or $evidence.success -ne $true -or
        -not ([string]$evidence.result.sourceCommit).Equals($SourceCommit, [StringComparison]::OrdinalIgnoreCase) -or
        $null -eq $evidence.result.evidencePaths -or $null -eq $evidence.result.archiveLinks) {
        throw 'Canonical Apple recovery evidence does not match the verified consumer source commit.'
    }

    foreach ($path in @($evidence.result.evidencePaths)) {
        if ($path -isnot [string]) { throw 'Canonical Apple recovery evidence must contain file paths.' }
        $full = Get-ContainedAppleRecoveryPath -Path $path -Root $consumer
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
            throw "Canonical Apple recovery evidence file was not found: $full"
        }
        Add-AllowedConsumerEvidencePath -Path $full -Name 'Canonical Apple recovery evidence'
    }
    foreach ($link in @($evidence.result.archiveLinks)) {
        $archiveRoot = Get-ContainedAppleRecoveryPath -Path $link.archiveRoot -Root $consumer
        Assert-UnlinkedDirectory -Path $archiveRoot -Name 'Receipt-bound Apple archive root'
        $path = Get-ContainedAppleRecoveryPath -Path $link.path -Root $archiveRoot
        $resolved = Get-ContainedAppleRecoveryPath -Path $link.resolvedTargetPath -Root $archiveRoot
        Assert-UnlinkedPath -Path (Split-Path -Parent $path) -Name 'Receipt-bound Apple archive link ancestors'
        Assert-UnlinkedPath -Path $resolved -Name 'Receipt-bound Apple archive resolved link target'
        if (-not (Test-Path -LiteralPath $resolved)) {
            throw "Receipt-bound Apple archive link target disappeared after canonical validation: $resolved"
        }
        $item = Get-Item -LiteralPath $path -Force -ErrorAction Stop
        if (-not ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
            $item.PSIsContainer -ne $link.isDirectory -or
            -not ([string]$item.LinkTarget).Equals([string]$link.linkTarget, [StringComparison]::Ordinal)) {
            throw "Receipt-bound Apple archive link changed after canonical validation: $path"
        }
        $null = $script:allowedConsumerEvidencePaths.Add($path)
    }
}

function Test-AppleWorkerRun {
    return $ArgumentList.Count -gt 1 -and $ArgumentList[0] -eq 'apple-testflight' -and $ArgumentList[1] -eq 'run'
}

function Register-AppleWorkerCheckpointEvidence {
    param(
        [Parameter(Mandatory)][string] $DotNetPath,
        [Parameter(Mandatory)][string] $CliAssembly,
        [Parameter(Mandatory)][string] $NuGetPackagesPath,
        [Parameter(Mandatory)][string] $OutputPath
    )
    $script:publisherCheckpointPaths = @()
    if (-not (Test-AppleWorkerRun)) { return }
    $exitCode = Invoke-RedactedProcess -FilePath $DotNetPath -WorkingDirectory $consumer `
        -Arguments @($CliAssembly, 'apple-testflight', 'checkpoint-evidence', '--policy', (Get-OptionValue -Option '--policy'),
            '--config', (Get-OptionValue -Option '--config'), '--output', 'json') `
        -NuGetPackagesPath $NuGetPackagesPath -CaptureStandardOutputPath $OutputPath
    if ($exitCode -ne 0) { throw 'Canonical worker publisher-checkpoint validation failed; preserve the diff for reconciliation.' }
    if (-not (Test-Path -LiteralPath $OutputPath -PathType Leaf) -or (Get-Item -LiteralPath $OutputPath).Length -gt 1MB) {
        throw 'Canonical worker checkpoint evidence is missing or oversized.'
    }
    $evidence = Get-Content -LiteralPath $OutputPath -Raw | ConvertFrom-Json
    if ($evidence.command -ne 'apple-testflight' -or $evidence.success -ne $true -or $null -eq $evidence.result) {
        throw 'Canonical worker checkpoint evidence has an invalid envelope.'
    }
    foreach ($path in @($evidence.result)) {
        if ($path -isnot [string]) { throw 'Canonical worker checkpoint evidence must contain file paths.' }
        $full = Get-ContainedAppleRecoveryPath -Path $path -Root $consumer
        Add-AllowedConsumerEvidencePath -Path $full -Name 'Canonical worker publisher checkpoint'
        $script:publisherCheckpointPaths += $full
    }
}

function Add-AllowedConsumerEvidencePath {
    param([Parameter(Mandatory)][string] $Path, [Parameter(Mandatory)][string] $Name)
    $full = [IO.Path]::GetFullPath($Path)
    $relative = [IO.Path]::GetRelativePath($consumer, $full).Replace('\', '/')
    if ([IO.Path]::IsPathRooted($relative) -or $relative -eq '..' -or $relative.StartsWith('../')) { return }
    if (Test-Path -LiteralPath $full) {
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "$Name must identify a file: $full" }
        Assert-UnlinkedPath -Path $full -Name $Name
    }
    $null = $script:allowedConsumerEvidencePaths.Add($full)
}

function Register-AppleReceiptEvidenceFile {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $SourceCommit,
        [switch] $HistoryEntry,
        [switch] $AllowLegacy
    )
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return }
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.Length -gt 2MB) { throw "Apple release receipt exceeds the 2 MB evidence limit: $Path" }
    Assert-UnlinkedPath -Path $Path -Name 'Apple release receipt'
    try {
        $receipt = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    } catch {
        throw "Apple release receipt is not valid JSON: $Path"
    }
    $receiptSource = [string]$receipt.sourceCommit
    $schemaVersion = [int]$receipt.schemaVersion
    if ($schemaVersion -gt 6) {
        throw "Apple release receipt schema $schemaVersion is not supported: $Path"
    }
    $supportedReceipt = $schemaVersion -in @(5, 6)
    if ($supportedReceipt) {
        if (
            ([string]$receipt.attemptId) -notmatch '^[0-9A-Fa-f]{32}$' -or
            ([string]$receipt.receiptSha256) -notmatch '^[0-9A-Fa-f]{64}$' -or
            (-not [string]::IsNullOrWhiteSpace($receiptSource) -and
             $receiptSource -notmatch '^(?:[0-9A-Fa-f]{40}|[0-9A-Fa-f]{64})$')) {
            throw "Apple release evidence does not satisfy the supported receipt contract: $Path"
        }
    }
    if ($schemaVersion -eq 5) {
        if (([string]$receipt.receiptAuthenticationSha256) -notmatch '^[0-9A-Fa-f]{64}$') {
            throw "Legacy schema-5 Apple release evidence does not satisfy its integrity-key contract: $Path"
        }
        $keyPath = if (-not [string]::IsNullOrWhiteSpace($env:POWERFORGE_APPLE_RECEIPT_AUTH_KEY_PATH)) {
            [IO.Path]::GetFullPath($env:POWERFORGE_APPLE_RECEIPT_AUTH_KEY_PATH)
        } else {
            Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) '.powerforge/apple-receipt-auth.key'
        }
        if (-not (Test-Path -LiteralPath $keyPath -PathType Leaf)) {
            throw "Authenticated Apple release evidence requires the machine-local key '$keyPath'."
        }
        Assert-UnlinkedPath -Path $keyPath -Name 'Apple release receipt authentication key'
        $key = [IO.File]::ReadAllBytes($keyPath)
        if ($key.Length -ne 32) { throw "Apple release receipt authentication key must contain exactly 32 bytes: $keyPath" }
        $hmac = [Security.Cryptography.HMACSHA256]::new($key)
        try {
            $expected = $hmac.ComputeHash([Text.Encoding]::ASCII.GetBytes(([string]$receipt.receiptSha256).ToLowerInvariant()))
        } finally {
            $hmac.Dispose()
        }
        $actual = [Convert]::FromHexString([string]$receipt.receiptAuthenticationSha256)
        if (-not [Security.Cryptography.CryptographicOperations]::FixedTimeEquals($expected, $actual)) {
            throw "Apple release receipt recovery authentication failed: $Path"
        }
    } elseif ($schemaVersion -ne 6) {
        if (-not $AllowLegacy) {
            throw "Apple release receipt is legacy evidence and cannot be admitted without a supported current receipt chain: $Path"
        }
    }
    Add-AllowedConsumerEvidencePath -Path $Path -Name 'Apple release receipt'
    return $supportedReceipt
}

function Get-ForwardedArgumentList {
    param([Parameter(Mandatory)][string] $SourceCommit)
    if ($ArgumentList[0] -ne 'apple-release') { return @($ArgumentList) }
    if ($SourceCommit -notmatch '^(?:[0-9A-Fa-f]{40}|[0-9A-Fa-f]{64})$') {
        throw 'Verified consumer source commit must be a full SHA-1 or SHA-256 Git commit object id.'
    }

    $withoutLocalEvidence = [Collections.Generic.List[string]]::new()
    $seenLocalOptions = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    for ($index = 0; $index -lt $ArgumentList.Count; $index++) {
        $argument = $ArgumentList[$index]
        $localOption = $null
        foreach ($candidate in @('--capture-provenance', '--allowed-root')) {
            if ($argument -eq $candidate -or $argument.StartsWith("$candidate=", [StringComparison]::OrdinalIgnoreCase)) {
                $localOption = $candidate
                break
            }
        }
        if ($localOption) {
            if (-not $seenLocalOptions.Add($localOption)) { throw "$localOption must be specified at most once." }
            if ($argument -eq $localOption) {
                if ($index + 1 -ge $ArgumentList.Count) { throw "Missing value for $localOption." }
                if ([string]::IsNullOrWhiteSpace([string]$ArgumentList[$index + 1]) -or
                    ([string]$ArgumentList[$index + 1]).StartsWith('-', [StringComparison]::Ordinal)) {
                    throw "Missing value for $localOption. Prefix a dash-leading path with './'."
                }
                $index++
            } else {
                $localValue = $argument.Substring($localOption.Length + 1)
                if ([string]::IsNullOrWhiteSpace($localValue) -or $localValue.StartsWith('-', [StringComparison]::Ordinal)) {
                    throw "Missing value for $localOption. Prefix a dash-leading path with './'."
                }
            }
            continue
        }
        $withoutLocalEvidence.Add($argument)
    }

    $result = [Collections.Generic.List[string]]::new()
    $sourceCommitFound = $false
    for ($index = 0; $index -lt $withoutLocalEvidence.Count; $index++) {
        $argument = $withoutLocalEvidence[$index]
        $configuredSourceCommit = $null
        if ($argument -eq '--apple-source-commit') {
            if ($index + 1 -ge $withoutLocalEvidence.Count) { throw 'Missing value for --apple-source-commit.' }
            $configuredSourceCommit = $withoutLocalEvidence[++$index]
        } elseif ($argument.StartsWith('--apple-source-commit=', [StringComparison]::OrdinalIgnoreCase)) {
            $configuredSourceCommit = $argument.Substring('--apple-source-commit='.Length)
        } else {
            $result.Add($argument)
            continue
        }

        if ($sourceCommitFound) { throw '--apple-source-commit must be specified at most once.' }
        $sourceCommitFound = $true
        if ([string]::IsNullOrWhiteSpace($configuredSourceCommit) -or
            $configuredSourceCommit -notmatch '^(?:[0-9A-Fa-f]{40}|[0-9A-Fa-f]{64})$') {
            throw '--apple-source-commit must contain a full SHA-1 or SHA-256 Git commit object id.'
        }
        if (-not $configuredSourceCommit.Equals($SourceCommit, [StringComparison]::OrdinalIgnoreCase)) {
            throw "--apple-source-commit must match the exact consumer HEAD '$SourceCommit'."
        }
        $result.Add('--apple-source-commit')
        $result.Add($SourceCommit.ToLowerInvariant())
    }
    if (-not $sourceCommitFound) {
        $result.Add('--apple-source-commit')
        $result.Add($SourceCommit.ToLowerInvariant())
    }
    return @($result)
}

function Assert-ConsumerRepositoryContent {
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($arguments in @(
        @('diff', '--name-only', 'HEAD', '--'),
        @('ls-files', '--others', '--exclude-standard'),
        @('ls-files', '--others', '--ignored', '--exclude-standard'))) {
        $output = Invoke-GitText -Root $consumer -Arguments $arguments
        foreach ($relative in @($output -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
            if ($relative.StartsWith('"') -or $relative.Contains("`n") -or $relative.Contains("`r")) {
                throw 'Consumer source contains a path that cannot be safely classified as reviewed evidence.'
            }
            $null = $paths.Add($relative.Replace('\', '/'))
        }
    }
    foreach ($relative in $paths) {
        $full = [IO.Path]::GetFullPath((Join-Path $consumer $relative))
        if (-not $script:allowedConsumerEvidencePaths.Contains($full)) {
            throw "Consumer source contains non-reviewed content '$relative'; use a fresh exact checkout."
        }
    }
}

function Register-AppleAutomationEvidence {
    param([Parameter(Mandatory)][string] $SourceCommit)
    if ($ArgumentList[0] -ne 'apple-release') { return }

    $releaseConfigPath = Resolve-OptionPath -Value (Get-OptionValue -Option '--config')
    $release = Get-Content -LiteralPath $releaseConfigPath -Raw | ConvertFrom-Json
    $apple = $release.AppleApps
    $projectRootValue = if ([string]::IsNullOrWhiteSpace([string]$apple.ProjectRoot)) { '.' } else { [string]$apple.ProjectRoot }
    $projectRoot = Resolve-PathFromBase -BasePath (Split-Path -Parent $releaseConfigPath) -Value $projectRootValue
    $automation = $apple.Automation

    $lockValue = if ([string]::IsNullOrWhiteSpace([string]$automation.LockPath)) {
        'build/powerforge/apple/release.lock'
    } else { [string]$automation.LockPath }
    $lockPath = Resolve-PathFromBase -BasePath $projectRoot -Value $lockValue
    if (Test-Path -LiteralPath $lockPath) {
        Add-AllowedConsumerEvidencePath -Path $lockPath -Name 'Apple release operation lock'
    }

    $receiptValue = if ([string]::IsNullOrWhiteSpace([string]$automation.ReceiptPath)) {
        'build/powerforge/apple/release-receipt.json'
    } else { [string]$automation.ReceiptPath }
    $receiptPath = Resolve-PathFromBase -BasePath $projectRoot -Value $receiptValue

    $historyValue = if ([string]::IsNullOrWhiteSpace([string]$automation.ReceiptHistoryPath)) {
        'build/powerforge/apple/receipts'
    } else { [string]$automation.ReceiptHistoryPath }
    $historyPath = Resolve-PathFromBase -BasePath $projectRoot -Value $historyValue
    $receiptEvidenceFiles = [Collections.Generic.List[object]]::new()
    if (Test-Path -LiteralPath $receiptPath) {
        $receiptEvidenceFiles.Add([pscustomobject]@{ Path = $receiptPath; History = $false })
    }
    if (Test-Path -LiteralPath $historyPath) {
        Assert-UnlinkedDirectory -Path $historyPath -Name 'Apple release receipt history'
        foreach ($entry in @(Get-ChildItem -LiteralPath $historyPath -Force)) {
            if ($entry.PSIsContainer -or -not $entry.Name.EndsWith('.json', [StringComparison]::OrdinalIgnoreCase)) {
                throw "Apple release receipt history contains an unsupported entry: $($entry.FullName)"
            }
            $receiptEvidenceFiles.Add([pscustomobject]@{ Path = $entry.FullName; History = $true })
        }
    }
    $classifiedReceipts = foreach ($entry in $receiptEvidenceFiles) {
        try { $value = Get-Content -LiteralPath $entry.Path -Raw | ConvertFrom-Json }
        catch { throw "Apple release receipt is not valid JSON: $($entry.Path)" }
        [pscustomobject]@{ Path = $entry.Path; History = $entry.History; Supported = ([int]$value.schemaVersion -in @(5, 6)) }
    }
    $supportedReceipts = @($classifiedReceipts | Where-Object Supported)
    foreach ($entry in $supportedReceipts) {
        $null = Register-AppleReceiptEvidenceFile -Path $entry.Path -SourceCommit $SourceCommit -HistoryEntry:$entry.History
    }
    foreach ($entry in @($classifiedReceipts | Where-Object { -not $_.Supported })) {
        $null = Register-AppleReceiptEvidenceFile -Path $entry.Path -SourceCommit $SourceCommit -HistoryEntry:$entry.History -AllowLegacy:($supportedReceipts.Count -gt 0)
    }

    $expectedPlanSha256 = Get-OptionValue -Option '--apple-expected-plan-sha256'
    if (-not [string]::IsNullOrWhiteSpace($expectedPlanSha256) -and $expectedPlanSha256 -notmatch '^[0-9A-Fa-f]{64}$') {
        throw '--apple-expected-plan-sha256 must contain exactly 64 hexadecimal characters.'
    }

    $planValue = if ([string]::IsNullOrWhiteSpace([string]$automation.PlanReceiptPath)) {
        'build/powerforge/apple/release-plan.json'
    } else { [string]$automation.PlanReceiptPath }
    $planPath = Resolve-PathFromBase -BasePath $projectRoot -Value $planValue
    if (-not (Test-Path -LiteralPath $planPath -PathType Leaf)) { return }
    $plan = Get-Content -LiteralPath $planPath -Raw | ConvertFrom-Json
    if ($plan.planOnly -ne $true -or
        -not ([string]$plan.sourceCommit).Equals($SourceCommit, [StringComparison]::OrdinalIgnoreCase) -or
        ([string]$plan.planSha256) -notmatch '^[0-9A-Fa-f]{64}$') {
        throw 'Apple release plan receipt is not bounded to the exact consumer source commit.'
    }
    if (-not [string]::IsNullOrWhiteSpace($expectedPlanSha256)) {
        $requestedAction = if ($ArgumentList.Count -gt 1) { [string]$ArgumentList[1] } else { '' }
        if (-not ([string]$plan.action).Equals($requestedAction, [StringComparison]::OrdinalIgnoreCase) -or
            -not ([string]$plan.planSha256).Equals($expectedPlanSha256, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Apple release plan receipt does not match the approved action and plan SHA-256.'
        }
    }
    Add-AllowedConsumerEvidencePath -Path $planPath -Name 'Apple release plan receipt'
}

function Assert-TrackedSourceLinks {
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $rootPrefix = $consumer.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $index = Invoke-GitText -Root $consumer -Arguments @('ls-files', '--stage')
    if (@($index -split '\r?\n' | Where-Object { $_.StartsWith('160000 ', [StringComparison]::Ordinal) }).Count -gt 0) {
        throw 'Tracked Git submodules are forbidden at the pinned local operator boundary; their worktree bytes are not contained by the consumer commit.'
    }
    foreach ($line in @($index -split '\r?\n' | Where-Object { $_.StartsWith('120000 ', [StringComparison]::Ordinal) })) {
        $separator = $line.IndexOf("`t", [StringComparison]::Ordinal)
        if ($separator -lt 0) { throw 'Consumer source contains a tracked link path that cannot be safely classified.' }
        $relative = $line.Substring($separator + 1)
        if ([string]::IsNullOrWhiteSpace($relative) -or $relative.StartsWith('"') -or
            $relative.Contains("`r") -or $relative.Contains("`n")) {
            throw 'Consumer source contains a tracked link path that cannot be safely classified.'
        }
        $linkPath = [IO.Path]::GetFullPath((Join-Path $consumer $relative))
        if (-not $linkPath.StartsWith($rootPrefix, $comparison)) {
            throw "Tracked source link escapes the exact consumer checkout: $relative"
        }
        $visited = [Collections.Generic.HashSet[string]]::new(
            $(if ($IsWindows) { [StringComparer]::OrdinalIgnoreCase } else { [StringComparer]::Ordinal }))
        $current = Get-Item -LiteralPath $linkPath -Force -ErrorAction Stop
        if (-not $current.LinkType) {
            throw "Tracked source link is not materialized as a symbolic link: $relative"
        }
        while ($current.LinkType) {
            if (-not $visited.Add($current.FullName)) { throw "Tracked source link cycle is forbidden: $relative" }
            $target = [string]$current.LinkTarget
            if ([string]::IsNullOrWhiteSpace($target) -or [IO.Path]::IsPathRooted($target)) {
                throw "Tracked source link must use a contained relative target: $relative"
            }
            $next = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $current.FullName) $target))
            if (-not $next.StartsWith($rootPrefix, $comparison)) {
                throw "Tracked source link escapes the exact consumer checkout: $relative"
            }
            $current = Get-Item -LiteralPath $next -Force -ErrorAction Stop
        }
    }
}

function Register-StandaloneScreenshotEvidence {
    if ($ArgumentList[0] -ne 'apple-screenshots' -or $null -eq $script:validatedCaptureProvenance) { return }
    $allowedRoot = Resolve-OptionPath -Value (Get-OptionValue -Option '--allowed-root')
    Assert-UnlinkedDirectory -Path $allowedRoot -Name '--allowed-root'
    $rootPrefix = $allowedRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $inventory = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in @($script:validatedCaptureProvenance.screenshots)) {
        $relative = Assert-CaptureScreenshotEntry -Entry $entry
        $key = Get-ScreenshotInventoryKey -Path $relative -Sha256 ([string]$entry.sha256) -Width ([int]$entry.width) -Height ([int]$entry.height)
        if (-not $inventory.Add($key)) { throw "Capture provenance contains duplicate screenshot inventory entry '$relative'." }
        $path = [IO.Path]::GetFullPath((Join-Path $allowedRoot $relative))
        if (-not $path.StartsWith($rootPrefix, [StringComparison]::Ordinal) -or
            -not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Reviewed screenshot evidence was not found inside --allowed-root: $relative"
        }
        Assert-UnlinkedPath -Path $path -Name 'Reviewed screenshot evidence'
        if (-not (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.Equals([string]$entry.sha256, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Reviewed screenshot evidence does not match retained capture bytes: $relative"
        }
        Add-AllowedConsumerEvidencePath -Path $path -Name 'Reviewed screenshot evidence'
    }

    $operation = if ($ArgumentList.Count -gt 1) { [string]$ArgumentList[1] } else { '' }
    $configPaths = if ($operation -eq 'manifests') {
        $releaseConfigPath = Resolve-OptionPath -Value (Get-OptionValue -Option '--release-config')
        $release = Get-Content -LiteralPath $releaseConfigPath -Raw | ConvertFrom-Json
        $apple = $release.AppleApps
        $projectRootValue = if ([string]::IsNullOrWhiteSpace([string]$apple.ProjectRoot)) { '.' } else { [string]$apple.ProjectRoot }
        $projectRoot = Resolve-PathFromBase -BasePath (Split-Path -Parent $releaseConfigPath) -Value $projectRootValue
        @(@([string]$apple.ScreenshotConfigPath) + @($apple.ScreenshotConfigPaths) |
            Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
            ForEach-Object { Resolve-PathFromBase -BasePath $projectRoot -Value ([string]$_) } |
            Sort-Object -Unique)
    } else {
        @(Resolve-OptionPath -Value (Get-OptionValue -Option '--config'))
    }
    foreach ($configPath in $configPaths) {
        $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
        $configuredOutput = [string]$config.Quality.ApprovalManifestPath
        $outputValue = Get-OptionValue -Option '--out'
        if ([string]::IsNullOrWhiteSpace($outputValue)) {
            $outputValue = if ([string]::IsNullOrWhiteSpace($configuredOutput)) {
                [IO.Path]::GetFileNameWithoutExtension($configPath) + '.approval.json'
            } else { $configuredOutput }
        }
        $outputPath = Resolve-PathFromBase -BasePath (Split-Path -Parent $configPath) -Value $outputValue
        Add-AllowedConsumerEvidencePath -Path $outputPath -Name 'Screenshot approval manifest output'
    }
}

function Get-ScreenshotInventoryKey {
    param([Parameter(Mandatory)][string] $Path, [Parameter(Mandatory)][string] $Sha256, [int] $Width, [int] $Height)
    return "$($Path.Replace('\', '/'))|$($Sha256.ToLowerInvariant())|$Width|$Height"
}

function Assert-CaptureScreenshotEntry {
    param([Parameter(Mandatory)] $Entry)
    $relative = ([string]$Entry.path).Replace('\', '/')
    $sha256 = ([string]$Entry.sha256).ToLowerInvariant()
    if ([string]::IsNullOrWhiteSpace($relative) -or [IO.Path]::IsPathRooted($relative) -or
        @($relative.Split('/') | Where-Object { $_ -in @('.', '..', '') }).Count -gt 0 -or
        $sha256 -notmatch '^[0-9a-f]{64}$' -or [int]$Entry.width -le 0 -or [int]$Entry.height -le 0) {
        throw "Capture provenance contains an invalid screenshot inventory entry for '$relative'."
    }
    return $relative
}

function Test-ScreenshotInventorySubsetCounts {
    param(
        [Parameter(Mandatory)][Collections.Generic.Dictionary[string, int]] $Expected,
        [Parameter(Mandatory)][Collections.Generic.Dictionary[string, int]] $Actual
    )
    if ($Actual.Count -eq 0 -or $Actual.Count -gt $Expected.Count) { return $false }
    foreach ($key in $Actual.Keys) {
        if ([int]$Actual[$key] -ne 1 -or [int]$Expected[$key] -ne 1) { return $false }
    }
    return $true
}

function Assert-ScreenshotPublicationBinding {
    param([Parameter(Mandatory)][string] $SourceCommit)
    if ($ArgumentList[0] -ne 'apple-release' -or $ArgumentList.Count -lt 2) { return }
    $operation = $ArgumentList[1]
    if ($operation -notin @('Screenshots', 'Advance')) { return }

    $releaseConfigPath = Resolve-OptionPath -Value (Get-OptionValue -Option '--config')
    $release = Get-Content -LiteralPath $releaseConfigPath -Raw | ConvertFrom-Json
    $apple = $release.AppleApps
    $projectRootValue = if ([string]::IsNullOrWhiteSpace([string]$apple.ProjectRoot)) { '.' } else { [string]$apple.ProjectRoot }
    $projectRoot = Resolve-PathFromBase -BasePath (Split-Path -Parent $releaseConfigPath) -Value $projectRootValue
    $configValues = @(
        @([string]$apple.ScreenshotConfigPath) + @($apple.ScreenshotConfigPaths) |
            Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
            ForEach-Object { Resolve-PathFromBase -BasePath $projectRoot -Value ([string]$_) } |
            Sort-Object -Unique
    )
    $requiresBinding = $operation -eq 'Screenshots' -or
        ($operation -eq 'Advance' -and $apple.SyncScreenshots -eq $true -and $configValues.Count -gt 0)
    if (-not $requiresBinding) { return }
    $selectedTargets = @((Get-OptionValue -Option '--target') -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $enabledApps = @($apple.Apps | Where-Object { $_.Enabled -ne $false })
    $missingTargets = @($selectedTargets | Where-Object {
        $selectedTarget = $_
        @($enabledApps | Where-Object {
            $selectedTarget -eq ([string]$_.Name).Trim() -or
            $selectedTarget -eq ([string]$_.Scheme).Trim() -or
            $selectedTarget -eq ([string]$_.BundleId).Trim()
        }).Count -eq 0
    })
    if ($missingTargets.Count -gt 0) {
        throw "Unknown Apple app target(s): $($missingTargets -join ', ')"
    }
    $targetedApps = @($enabledApps | Where-Object {
        $selectedTargets.Count -eq 0 -or $selectedTargets -contains ([string]$_.Name).Trim() -or
        $selectedTargets -contains ([string]$_.Scheme).Trim() -or
        $selectedTargets -contains ([string]$_.BundleId).Trim()
    })
    $appStoreApps = @($enabledApps | Where-Object {
        $route = [string]$_.DistributionRoute
        [string]::IsNullOrWhiteSpace($route) -or $route -eq 'AppStore'
    })
    $selectedApps = @($targetedApps | Where-Object {
        $route = [string]$_.DistributionRoute
        [string]::IsNullOrWhiteSpace($route) -or $route -eq 'AppStore'
    })
    if ($selectedApps.Count -eq 0) { return }
    if ($configValues.Count -eq 0) { throw "apple-release $operation requires at least one screenshot configuration." }
    if ($null -eq $script:validatedCaptureProvenance) {
        throw "apple-release $operation requires --capture-provenance so screenshot publication remains bound to the retained capture artifact."
    }

    $provenance = $script:validatedCaptureProvenance
    $provenanceEntries = @($provenance.screenshots)
    $pathComparer = if ($IsWindows) { [StringComparer]::OrdinalIgnoreCase } else { [StringComparer]::Ordinal }
    $inventoryCounts = [Collections.Generic.Dictionary[string, int]]::new($pathComparer)
    $provenancePaths = [Collections.Generic.HashSet[string]]::new($pathComparer)
    foreach ($entry in $provenanceEntries) {
        $relative = Assert-CaptureScreenshotEntry -Entry $entry
        if (-not $provenancePaths.Add($relative)) { throw "Capture provenance contains duplicate screenshot path '$relative'." }
        $key = Get-ScreenshotInventoryKey -Path $relative -Sha256 ([string]$entry.sha256) -Width ([int]$entry.width) -Height ([int]$entry.height)
        if ($inventoryCounts.ContainsKey($key)) { throw "Capture provenance contains duplicate screenshot inventory entry '$relative'." }
        $inventoryCounts[$key] = 1
    }
    $approvedItems = [Collections.Generic.List[object]]::new()
    $approvedPaths = [Collections.Generic.HashSet[string]]::new($pathComparer)
    $manifestPaths = [Collections.Generic.List[string]]::new()
    $activeConfigs = [Collections.Generic.List[object]]::new()
    foreach ($screenshotConfigPath in $configValues) {
        $screenshotConfig = Get-Content -LiteralPath $screenshotConfigPath -Raw | ConvertFrom-Json
        $specVersion = if ([string]::IsNullOrWhiteSpace([string]$screenshotConfig.VersionString)) {
            $null
        } else { ([string]$screenshotConfig.VersionString).Trim() }
        $versionMatches = ($screenshotConfig.UseReleaseVersion -eq $true -and $null -eq $specVersion) -or
            ($null -ne $specVersion -and $specVersion.Equals([string]$provenance.marketingVersion, [StringComparison]::OrdinalIgnoreCase))
        if (-not $versionMatches) { continue }
        $activeConfigs.Add([pscustomobject]@{ Path = $screenshotConfigPath; Spec = $screenshotConfig })
    }
    if ($activeConfigs.Count -eq 0) { throw 'No screenshot configuration matches the selected release targets.' }

    $selectedConfigs = [Collections.Generic.Dictionary[string, object]]::new($pathComparer)
    foreach ($selectedApp in $selectedApps) {
        $platformApps = @($appStoreApps | Where-Object { [string]$_.Platform -eq [string]$selectedApp.Platform })
        $enabledPlatformApps = @($enabledApps | Where-Object { [string]$_.Platform -eq [string]$selectedApp.Platform })
        $configuredAppIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($platformApp in $enabledPlatformApps) {
            if (-not [string]::IsNullOrWhiteSpace([string]$platformApp.AppStoreConnectAppId)) {
                $null = $configuredAppIds.Add(([string]$platformApp.AppStoreConnectAppId).Trim())
            }
        }
        $blankIdApps = @($platformApps | Where-Object { [string]::IsNullOrWhiteSpace([string]$_.AppStoreConnectAppId) })
        $matchingConfigs = @($activeConfigs | Where-Object {
            $candidate = $_.Spec
            if ([string]$candidate.Platform -ne [string]$selectedApp.Platform) { return $false }
            $candidateAppId = ([string]$candidate.AppId).Trim()
            $selectedAppId = ([string]$selectedApp.AppStoreConnectAppId).Trim()
            if (-not [string]::IsNullOrWhiteSpace($selectedAppId)) {
                return [string]::IsNullOrWhiteSpace($candidateAppId) -or
                    $candidateAppId.Equals($selectedAppId, [StringComparison]::OrdinalIgnoreCase)
            }
            if ([string]::IsNullOrWhiteSpace($candidateAppId)) { return $true }
            return $blankIdApps.Count -eq 1 -and -not $configuredAppIds.Contains($candidateAppId)
        })
        if ($matchingConfigs.Count -ne 1) {
            $reason = if ($matchingConfigs.Count -eq 0) { 'No' } else { 'Multiple' }
            throw "$reason screenshot configurations match selected Apple app '$([string]$selectedApp.Name)' version '$([string]$provenance.marketingVersion)' platform '$([string]$selectedApp.Platform)'. Configure AppStoreConnectAppId explicitly when discovery would otherwise be ambiguous."
        }
        $selectedConfigs[$matchingConfigs[0].Path] = $matchingConfigs[0]
    }

    foreach ($selectedConfig in $selectedConfigs.Values) {
        $screenshotConfigPath = [string]$selectedConfig.Path
        $screenshotConfig = $selectedConfig.Spec
        $manifestValue = [string]$screenshotConfig.Quality.ApprovalManifestPath
        if ([string]::IsNullOrWhiteSpace($manifestValue)) {
            throw "Screenshot configuration '$screenshotConfigPath' must name Quality.ApprovalManifestPath."
        }
        $manifestPath = Resolve-PathFromBase -BasePath (Split-Path -Parent $screenshotConfigPath) -Value $manifestValue
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ([string]$manifest.CaptureRunId -ne [string]$provenance.captureRunId -or
            -not ([string]$manifest.CaptureRepository).Equals([string]$provenance.repository, [StringComparison]::OrdinalIgnoreCase) -or
            [string]$manifest.CaptureWorkflowRef -ne [string]$provenance.workflowRef -or
            ([string]$manifest.SourceCommit).ToLowerInvariant() -ne $SourceCommit.ToLowerInvariant() -or
            [string]$manifest.VersionString -ne [string]$provenance.marketingVersion) {
            throw "Screenshot approval manifest '$manifestPath' is not bound to the authoritative capture run, repository, workflow, source commit, and marketing version."
        }
        $manifestPaths.Add($manifestPath)
        foreach ($entry in @($manifest.Screenshots)) {
            $approvedPath = Resolve-PathFromBase -BasePath (Split-Path -Parent $screenshotConfigPath) -Value ([string]$entry.File)
            if (-not (Test-Path -LiteralPath $approvedPath -PathType Leaf)) { throw "Approved screenshot was not found: $approvedPath" }
            Assert-UnlinkedPath -Path $approvedPath -Name 'Approved screenshot'
            if (-not $approvedPaths.Add($approvedPath)) { throw "Screenshot approval manifests contain duplicate approved screenshot path '$approvedPath'." }
            $sha256 = ([string]$entry.Sha256).ToLowerInvariant()
            if (-not (Get-FileHash -LiteralPath $approvedPath -Algorithm SHA256).Hash.Equals($sha256, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Approved screenshot bytes do not match manifest: $approvedPath"
            }
            $approvedItems.Add([pscustomobject]@{ Path = $approvedPath; Sha256 = $sha256; Width = [int]$entry.Width; Height = [int]$entry.Height })
        }
    }

    $retainedRootValue = Get-OptionValue -Option '--allowed-root'
    if ([string]::IsNullOrWhiteSpace([string]$retainedRootValue)) {
        throw "apple-release $operation requires --allowed-root to bind screenshots to the retained capture inventory."
    }
    $retainedRoot = Resolve-OptionPath -Value $retainedRootValue
    if (-not (Test-Path -LiteralPath $retainedRoot -PathType Container)) {
        throw "--allowed-root must identify the retained screenshot capture directory: $retainedRoot"
    }
    Assert-UnlinkedDirectory -Path $retainedRoot -Name '--allowed-root'
    $retainedPrefix = $retainedRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $pathComparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $counts = [Collections.Generic.Dictionary[string, int]]::new($pathComparer)
    foreach ($approved in $approvedItems) {
        if (-not $approved.Path.StartsWith($retainedPrefix, $pathComparison)) {
            throw 'Screenshot approval manifests do not identify one exact retained capture root and approved inventory.'
        }
        $relative = [IO.Path]::GetRelativePath($retainedRoot, $approved.Path).Replace('\', '/')
        $key = Get-ScreenshotInventoryKey -Path $relative -Sha256 $approved.Sha256 -Width $approved.Width -Height $approved.Height
        $counts[$key] = 1 + [int]($counts[$key] ?? 0)
    }
    if (-not (Test-ScreenshotInventorySubsetCounts -Expected $inventoryCounts -Actual $counts)) {
        throw 'Screenshot approval manifests do not identify one exact retained capture root and approved inventory.'
    }
    foreach ($path in $manifestPaths) { Add-AllowedConsumerEvidencePath -Path $path -Name 'Screenshot approval manifest' }
    foreach ($approved in $approvedItems) { Add-AllowedConsumerEvidencePath -Path $approved.Path -Name 'Approved screenshot' }
}

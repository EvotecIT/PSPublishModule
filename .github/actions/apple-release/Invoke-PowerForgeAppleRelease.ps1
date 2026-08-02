$ErrorActionPreference = 'Stop'
$script:RunnerLocalCredentialSecrets = [Collections.Generic.List[string]]::new()

function Add-RunnerLocalCredentialSecret {
    param([AllowEmptyString()] [string] $Value)

    if (-not [string]::IsNullOrWhiteSpace($Value) -and
        -not $script:RunnerLocalCredentialSecrets.Contains($Value)) {
        $script:RunnerLocalCredentialSecrets.Add($Value)
    }
}

function Protect-RunnerLocalCredentialText {
    param([AllowNull()] [object] $Value)

    if ($null -eq $Value) { return $null }
    $text = [string] $Value
    foreach ($secret in @($script:RunnerLocalCredentialSecrets | Sort-Object Length -Descending)) {
        $text = $text.Replace($secret, '[redacted]', [StringComparison]::Ordinal)
    }
    return $text
}

function ConvertTo-ProtectedRunnerLocalCredentialValue {
    param([AllowNull()] [object] $Value)

    if ($null -eq $Value) { return $null }
    if ($Value -is [string]) { return Protect-RunnerLocalCredentialText $Value }
    if ($Value -is [Collections.IDictionary]) {
        $copy = [ordered]@{}
        foreach ($key in $Value.Keys) {
            $copy[$key] = ConvertTo-ProtectedRunnerLocalCredentialValue $Value[$key]
        }
        return $copy
    }
    if ($Value -is [System.Management.Automation.PSCustomObject]) {
        $copy = [ordered]@{}
        foreach ($property in $Value.PSObject.Properties) {
            $copy[$property.Name] = ConvertTo-ProtectedRunnerLocalCredentialValue $property.Value
        }
        return $copy
    }
    if ($Value -is [Collections.IEnumerable]) {
        return @($Value | ForEach-Object { ConvertTo-ProtectedRunnerLocalCredentialValue $_ })
    }
    return $Value
}

function Resolve-ReleasePath {
    param(
        [Parameter(Mandatory)] [string] $ProjectRoot,
        [Parameter(Mandatory)] [string] $Path
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot $Path))
}

function Resolve-SafeReleaseOutputPath {
    param(
        [Parameter(Mandatory)] [string] $ProjectRoot,
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Name
    )

    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $fullRoot = [System.IO.Path]::GetFullPath($ProjectRoot)
    $filesystemRoot = [System.IO.Path]::GetPathRoot($fullRoot)
    $root = if ($fullRoot.Equals($filesystemRoot, $comparison)) {
        $fullRoot
    } else {
        $fullRoot.TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar)
    }
    $candidate = Resolve-ReleasePath -ProjectRoot $root -Path $Path
    $prefix = if ($root.EndsWith([System.IO.Path]::DirectorySeparatorChar) -or
        $root.EndsWith([System.IO.Path]::AltDirectorySeparatorChar)) {
        $root
    } else {
        $root + [System.IO.Path]::DirectorySeparatorChar
    }
    if (-not $candidate.StartsWith($prefix, $comparison)) {
        throw "$Name must resolve inside AppleApps.ProjectRoot: $candidate"
    }

    $current = $candidate
    $isCandidate = $true
    while ($true) {
        $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if ($null -ne $item -and ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Name must not traverse a symbolic link or reparse point: $current"
        }
        if ($null -ne $item) {
            if ($isCandidate -and $item.PSIsContainer) {
                throw "$Name must resolve to a file, not a directory: $candidate"
            }
            if (-not $isCandidate -and -not $item.PSIsContainer) {
                throw "$Name must not traverse a file used as a parent path: $current"
            }
        }
        if ($current.Equals($root, $comparison)) { break }
        $parent = [System.IO.Directory]::GetParent($current)?.FullName
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent.Equals($current, $comparison)) {
            throw "$Name could not be bounded to AppleApps.ProjectRoot: $candidate"
        }
        $current = $parent
        $isCandidate = $false
    }
    return $candidate
}

function Get-ReleaseFileFingerprint {
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $item = Get-Item -LiteralPath $Path -Force
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    return "$hash`:$($item.Length)`:$($item.LastWriteTimeUtc.Ticks)"
}

function Write-ReleaseOutput {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [AllowEmptyString()] [string] $Value
    )

    if ($env:GITHUB_OUTPUT) {
        "$Name=$Value" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    }
}

function Get-FirstNonEmptyString {
    param([AllowNull()] [object[]] $Values)

    foreach ($value in $Values) {
        $text = [string] $value
        if (-not [string]::IsNullOrWhiteSpace($text)) { return $text.Trim() }
    }
    return $null
}

function Invoke-SecretSafeNativeProcess {
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $ArgumentList
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $processArguments = $ArgumentList
    $extension = [System.IO.Path]::GetExtension($FilePath)
    if ($IsWindows -and $extension -in @('.cmd', '.bat', '.ps1')) {
        $startInfo.FileName = Join-Path $PSHOME 'pwsh.exe'
        $argumentJson = $ArgumentList | ConvertTo-Json -Compress -AsArray
        $argumentBase64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($argumentJson))
        $processArguments = @(
            '-NoLogo',
            '-NoProfile',
            '-NonInteractive',
            '-File',
            (Join-Path $PSScriptRoot 'Invoke-CapturedPowerShellTool.ps1'),
            '-ToolPath',
            $FilePath,
            '-ArgumentListBase64',
            $argumentBase64
        )
    } else {
        $startInfo.FileName = $FilePath
    }
    foreach ($argument in $processArguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) { throw 'PowerForge could not be started.' }
        $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
        $standardErrorTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        [System.Threading.Tasks.Task]::WaitAll(@($standardOutputTask, $standardErrorTask))
        return [ordered]@{
            ExitCode = $process.ExitCode
            StandardOutput = [string] $standardOutputTask.Result
            StandardError = [string] $standardErrorTask.Result
        }
    } finally {
        $process.Dispose()
    }
}

function Write-AtomicTextFile {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Value
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    }
    $temporaryPath = Join-Path $directory ".$(Split-Path -Leaf $Path).$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        [System.IO.File]::WriteAllText($temporaryPath, $Value, [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::Move($temporaryPath, $Path, $true)
    } finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Write-AtomicJsonFile {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [object] $Value
    )

    Write-AtomicTextFile -Path $Path -Value ($Value | ConvertTo-Json -Depth 32)
}

function Protect-RunnerLocalCredentialReceipt {
    param([Parameter(Mandatory)] [string] $Path)

    if ($script:RunnerLocalCredentialSecrets.Count -eq 0 -or
        -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return }
    $raw = [System.IO.File]::ReadAllText($Path)
    $protectedRaw = Protect-RunnerLocalCredentialText $raw
    try {
        $document = $protectedRaw | ConvertFrom-Json -Depth 100
    } catch {
        if ($protectedRaw -ne $raw) {
            Write-AtomicTextFile -Path $Path -Value $protectedRaw
        }
        return
    }
    $protected = ConvertTo-ProtectedRunnerLocalCredentialValue $document
    Write-AtomicJsonFile -Path $Path -Value $protected
}

$allowedActions = @(
    'Status', 'Doctor', 'Version', 'Archive', 'Upload', 'UploadExisting', 'Prepare',
    'Screenshots', 'TestFlight', 'Advance', 'SubmitTestFlightReview',
    'SubmitAppReview', 'Release', 'Cleanup'
)
$action = $allowedActions | Where-Object { $_ -ieq $env:INPUT_ACTION } | Select-Object -First 1
if (-not $action) {
    throw "Unsupported Apple release action '$($env:INPUT_ACTION)'."
}

$planOnly = [bool]::Parse($env:INPUT_PLAN_ONLY)
$confirm = [bool]::Parse($env:INPUT_CONFIRM)
$runnerLocalCredentials = if ([string]::IsNullOrWhiteSpace($env:INPUT_RUNNER_LOCAL_CREDENTIALS)) {
    $false
} else {
    [bool]::Parse($env:INPUT_RUNNER_LOCAL_CREDENTIALS)
}
if ($planOnly -and $confirm) {
    throw 'A plan-only run must not carry mutation confirmation.'
}
if ($action -eq 'Version' -and [string]::IsNullOrWhiteSpace($env:INPUT_MARKETING_VERSION)) {
    throw 'Version requires marketing-version.'
}
if ($action -eq 'Version' -and $env:INPUT_MARKETING_VERSION -notmatch '^\d+\.\d+\.\d+$') {
    throw 'Version marketing-version must use x.y.z.'
}

$configPath = (Resolve-Path -LiteralPath $env:INPUT_CONFIG_PATH -ErrorAction Stop).Path
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json -Depth 100
$projectRootSetting = [string] $config.AppleApps.ProjectRoot
if ([string]::IsNullOrWhiteSpace($projectRootSetting)) { $projectRootSetting = '.' }
$projectRoot = if ([System.IO.Path]::IsPathRooted($projectRootSetting)) {
    [System.IO.Path]::GetFullPath($projectRootSetting)
} else {
    [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $configPath) $projectRootSetting))
}
$configuredReceiptPath = if ($planOnly) {
    [string] $config.AppleApps.Automation.PlanReceiptPath
} else {
    [string] $config.AppleApps.Automation.ReceiptPath
}
if ([string]::IsNullOrWhiteSpace($configuredReceiptPath)) {
    $configuredReceiptPath = if ($planOnly) {
        'build/powerforge/apple/release-plan.json'
    } else {
        'build/powerforge/apple/release-receipt.json'
    }
}
$defaultReceiptPath = if ($planOnly) {
    'build/powerforge/apple/release-plan.json'
} else {
    'build/powerforge/apple/release-receipt.json'
}
try {
    $receiptPath = Resolve-SafeReleaseOutputPath `
        -ProjectRoot $projectRoot `
        -Path $configuredReceiptPath `
        -Name ($planOnly ? 'AppleApps.Automation.PlanReceiptPath' : 'AppleApps.Automation.ReceiptPath')
} catch {
    # The engine will report the invalid configured path. Keep the wrapper's
    # fallback receipt inside the project so that failure remains actionable.
    $receiptPath = Resolve-SafeReleaseOutputPath `
        -ProjectRoot $projectRoot `
        -Path $defaultReceiptPath `
        -Name 'Apple failure fallback receipt path'
}

# Expose the expected artifact before invoking PowerForge. GitHub Actions then retains
# the path even when the transition fails after PowerForge writes a failure receipt.
Write-ReleaseOutput -Name 'receipt-path' -Value $receiptPath
$receiptFingerprintBefore = Get-ReleaseFileFingerprint -Path $receiptPath

if ($runnerLocalCredentials) {
    try {
        Add-RunnerLocalCredentialSecret -Value (Join-Path ([string] $env:HOME) '.appstoreconnect')
        $suppliedCredentialValues = @(
            $env:APP_STORE_CONNECT_ISSUER_ID,
            $env:APP_STORE_CONNECT_KEY_ID,
            $env:APP_STORE_CONNECT_PRIVATE_KEY_PATH,
            $config.AppleApps.AppStoreConnectApiKeyPath,
            $config.AppleApps.AppStoreConnectApiKeyId,
            $config.AppleApps.AppStoreConnectApiIssuerId
        ) | Where-Object { -not [string]::IsNullOrWhiteSpace([string] $_) }
        if ($suppliedCredentialValues.Count -gt 0) {
            throw 'Runner-local App Store Connect credentials must not be combined with action inputs or release-config credentials.'
        }
        & (Join-Path $PSScriptRoot 'Resolve-RunnerLocalAppleCredentials.ps1') -Action $action
        $privateKeyContent = [System.IO.File]::ReadAllText($env:APP_STORE_CONNECT_PRIVATE_KEY_PATH)
        Add-RunnerLocalCredentialSecret -Value $privateKeyContent
        $privateKeyBody = [Collections.Generic.List[string]]::new()
        foreach ($privateKeyLine in ($privateKeyContent -split '\r?\n')) {
            if ($privateKeyLine -notmatch '^\s*-----' -and $privateKeyLine.Trim().Length -ge 16) {
                $privateKeyBody.Add($privateKeyLine.Trim())
                Add-RunnerLocalCredentialSecret -Value $privateKeyLine.Trim()
            }
        }
        Add-RunnerLocalCredentialSecret -Value ($privateKeyBody -join '')
        Add-RunnerLocalCredentialSecret -Value ([string] $env:APP_STORE_CONNECT_PRIVATE_KEY_PATH)
        Add-RunnerLocalCredentialSecret -Value ([string] $env:APP_STORE_CONNECT_ISSUER_ID)
        Add-RunnerLocalCredentialSecret -Value ([string] $env:APP_STORE_CONNECT_KEY_ID)
    } catch {
        # Loader failures are intentionally value-free. At this boundary a failed
        # profile read may have revealed only part of the tuple, so exception text
        # (including paths and key identifiers) must never enter logs or receipts.
        $failureMessage = 'Runner-local App Store Connect credential profile is invalid or unavailable.'
        $failureDiagnostic = [ordered]@{
            severity = 'error'
            category = 'credential'
            code = 'APPLE_RUNNER_LOCAL_CREDENTIALS_INVALID'
            summary = $failureMessage
            evidence = $null
            action = 'Repair the private runner-local App Store Connect profile and retry the read-only Doctor action.'
            retryable = $false
        }
        $relativeReceiptPath = [System.IO.Path]::GetRelativePath($projectRoot, $receiptPath).Replace('\', '/')
        Write-AtomicJsonFile -Path $receiptPath -Value ([ordered]@{
            schemaVersion = 3
            action = $action
            sourceCommit = [string] $env:INPUT_SOURCE_COMMIT
            planOnly = $planOnly
            checkedAt = [DateTimeOffset]::UtcNow
            planSha256 = $null
            success = $false
            errorMessage = $failureMessage
            receiptPath = $relativeReceiptPath
            versioning = $null
            targets = @()
            cleanup = [ordered]@{}
            diagnostics = @($failureDiagnostic)
            nextActions = @($failureDiagnostic.action)
        })
        Write-ReleaseOutput -Name 'diagnostics' -Value (@([ordered]@{
            severity = 'error'
            category = 'credential'
            code = 'APPLE_RUNNER_LOCAL_CREDENTIALS_INVALID'
            action = $failureDiagnostic.action
            retryable = $false
        }) | ConvertTo-Json -Compress -AsArray)
        throw "Runner-local App Store Connect credentials are unavailable. The complete failure receipt is retained at '$receiptPath'."
    }
}

$arguments = @('apple-release', $action, '--config', $env:INPUT_CONFIG_PATH, '--summary', '--output', 'json')
if ($planOnly) { $arguments += '--plan' }
if ($confirm) { $arguments += '--confirm-apple-action' }
if (-not [string]::IsNullOrWhiteSpace($env:INPUT_MARKETING_VERSION)) {
    $arguments += @('--apple-version', $env:INPUT_MARKETING_VERSION)
}
if (-not [string]::IsNullOrWhiteSpace($env:INPUT_SOURCE_COMMIT)) {
    if ($env:INPUT_SOURCE_COMMIT -notmatch '^[0-9A-Fa-f]{40}$') {
        throw 'source-commit must be an exact 40-character commit SHA.'
    }
    $arguments += @('--apple-source-commit', $env:INPUT_SOURCE_COMMIT)
}
if (-not [string]::IsNullOrWhiteSpace($env:INPUT_EXPECTED_PLAN_SHA256)) {
    if ($planOnly) {
        throw 'expected-plan-sha256 is valid only for an executing Apple transition.'
    }
    if ($env:INPUT_EXPECTED_PLAN_SHA256 -notmatch '^[0-9A-Fa-f]{64}$') {
        throw 'expected-plan-sha256 must contain exactly 64 hexadecimal characters.'
    }
    $arguments += @('--apple-expected-plan-sha256', $env:INPUT_EXPECTED_PLAN_SHA256)
}
if (-not [string]::IsNullOrWhiteSpace($env:INPUT_TARGET)) {
    $arguments += @('--target', $env:INPUT_TARGET)
}

$invocation = Invoke-SecretSafeNativeProcess -FilePath $env:POWERFORGE_TOOL_PATH -ArgumentList $arguments
$exitCode = [int] $invocation.ExitCode
$receiptFingerprintAfterInvocation = Get-ReleaseFileFingerprint -Path $receiptPath
$json = Protect-RunnerLocalCredentialText $invocation.StandardOutput
$protectedStandardError = Protect-RunnerLocalCredentialText $invocation.StandardError
$envelope = $null
if (-not [string]::IsNullOrWhiteSpace($json)) {
    try {
        $envelope = $json | ConvertFrom-Json -Depth 100
        $envelope = ConvertTo-ProtectedRunnerLocalCredentialValue $envelope
    } catch {
        if ($exitCode -eq 0) { throw }
    }
}

$result = $envelope.result
$planSha256 = [string] $result.planSha256
if ($exitCode -eq 0 -and $planOnly -and $planSha256 -notmatch '^[0-9A-Fa-f]{64}$') {
    throw "PowerForge action '$action' did not return a valid exact plan SHA-256."
}
Write-ReleaseOutput -Name 'plan-sha256' -Value $planSha256
$reportedReceiptPath = [string] $result.receiptPath
if ($exitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($reportedReceiptPath)) {
    $receiptPath = Resolve-SafeReleaseOutputPath `
        -ProjectRoot $projectRoot `
        -Path $reportedReceiptPath `
        -Name 'PowerForge reported receipt path'
    Write-ReleaseOutput -Name 'receipt-path' -Value $receiptPath
}
if ($runnerLocalCredentials -and $exitCode -eq 0) {
    Protect-RunnerLocalCredentialReceipt -Path $receiptPath
}

if ($exitCode -ne 0) {
    # Never echo the complete CLI envelope here. The receipt can contain compact
    # tester feedback and diagnostic evidence that belongs in the retained artifact,
    # not the public Actions log.
    $receiptDiagnostics = @($result.diagnostics | Where-Object {
        -not [string]::IsNullOrWhiteSpace([string] $_.code) -or
        -not [string]::IsNullOrWhiteSpace([string] $_.summary) -or
        -not [string]::IsNullOrWhiteSpace([string] $_.action)
    } | ForEach-Object {
        [ordered]@{
            severity = (Get-FirstNonEmptyString @($_.severity, 'error'))
            category = (Get-FirstNonEmptyString @($_.category, 'automation'))
            code = (Get-FirstNonEmptyString @($_.code, 'APPLE_ACTION_FAILED'))
            summary = (Protect-RunnerLocalCredentialText (Get-FirstNonEmptyString @($_.summary, "PowerForge Apple action '$action' failed.")))
            evidence = (Protect-RunnerLocalCredentialText ([string] $_.evidence))
            action = (Protect-RunnerLocalCredentialText (Get-FirstNonEmptyString @($_.action, 'Inspect the retained failure receipt and run logs, correct the reported condition, then retry.')))
            retryable = [bool] $_.retryable
        }
    })
    $failureMessage = Protect-RunnerLocalCredentialText (Get-FirstNonEmptyString @(
        $result.errorMessage,
        $envelope['error'],
        $protectedStandardError,
        "PowerForge Apple action '$action' failed with exit code $exitCode."
    ))
    if ($receiptDiagnostics.Count -eq 0) {
        $missingAppStoreCredentials =
            $failureMessage -match 'App Store Connect.*requires.*AppStoreConnectApiKeyPath' -or
            $failureMessage -match 'AppStoreConnectApiKeyPath.*AppStoreConnectApiKeyId.*AppStoreConnectApiIssuerId'
        $failureCategory = if ($missingAppStoreCredentials) { 'credential' } else { 'automation' }
        $failureCode = if ($missingAppStoreCredentials) { 'APPLE_APP_STORE_CONNECT_CREDENTIALS_MISSING' } else { 'APPLE_ACTION_FAILED' }
        $failureAction = if ($missingAppStoreCredentials -and $runnerLocalCredentials) {
            'Repair the private runner-local App Store Connect profile and retry the read-only Doctor action.'
        } elseif ($missingAppStoreCredentials) {
            'Configure the complete App Store Connect credential tuple for this explicitly authorized action.'
        } else {
            'Inspect the retained failure receipt and run logs, correct the reported preflight condition, then retry.'
        }
        $receiptDiagnostics = @([ordered]@{
            severity = 'error'
            category = $failureCategory
            code = $failureCode
            summary = $failureMessage
            evidence = $null
            action = $failureAction
            retryable = $false
        })
    }
    $engineWroteCurrentReceipt = $null -ne $receiptFingerprintAfterInvocation -and
        $receiptFingerprintAfterInvocation -ne $receiptFingerprintBefore
    # Replace missing or byte-for-byte stale state. Preserve a file changed by this
    # invocation even when stdout was malformed or did not carry diagnostics.
    if (-not $engineWroteCurrentReceipt) {
        $relativeReceiptPath = [System.IO.Path]::GetRelativePath($projectRoot, $receiptPath).Replace('\', '/')
        Write-AtomicJsonFile -Path $receiptPath -Value ([ordered]@{
            schemaVersion = 3
            action = $action
            sourceCommit = [string] $env:INPUT_SOURCE_COMMIT
            planOnly = $planOnly
            checkedAt = [DateTimeOffset]::UtcNow
            planSha256 = $null
            success = $false
            errorMessage = $failureMessage
            receiptPath = $relativeReceiptPath
            versioning = $null
            targets = @()
            cleanup = [ordered]@{}
            diagnostics = $receiptDiagnostics
            nextActions = @($receiptDiagnostics | ForEach-Object { [string] $_.action } | Select-Object -Unique)
        })
    }
    if ($engineWroteCurrentReceipt -and $runnerLocalCredentials) {
        Protect-RunnerLocalCredentialReceipt -Path $receiptPath
    }
    $safeDiagnostics = @($receiptDiagnostics | ForEach-Object {
        [ordered]@{
            severity = [string] $_.severity
            category = [string] $_.category
            code = [string] $_.code
            action = [string] $_.action
            retryable = [bool] $_.retryable
        }
    })
    $safeDiagnosticsJson = $safeDiagnostics | ConvertTo-Json -Compress -AsArray
    Write-ReleaseOutput -Name 'diagnostics' -Value $safeDiagnosticsJson
    [ordered]@{
        success = $false
        action = $action
        exitCode = $exitCode
        diagnostics = $safeDiagnostics
    } | ConvertTo-Json -Depth 8 | Write-Host
    $receiptHint = if (Test-Path -LiteralPath $receiptPath -PathType Leaf) {
        " The complete failure receipt is retained at '$receiptPath'."
    } else {
        ''
    }
    throw "PowerForge Apple action '$action' failed with exit code $exitCode.$receiptHint"
}

if (-not $envelope.success) {
    throw "PowerForge Apple action '$action' reported failure. Inspect the retained receipt for private diagnostic details."
}
if ([string] $result.action -ine $action) {
    throw "PowerForge returned action '$($result.action)' instead of '$action'."
}

if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) {
    throw "PowerForge action '$action' did not write its required receipt '$receiptPath'."
}
$marketingVersion = [string] $result.versioning.marketingVersion
$buildNumber = [string] $result.versioning.buildNumber
$versionSourcePath = [string] $result.versioning.sourcePath
if ([string]::IsNullOrWhiteSpace($marketingVersion) -and $result.targets.Count -gt 0) {
    $marketingVersion = [string] ($result.targets[0].version ?? $result.targets[0].marketingVersion)
}
if ([string]::IsNullOrWhiteSpace($buildNumber) -and $result.targets.Count -gt 0) {
    $buildNumber = [string] ($result.targets[0].build ?? $result.targets[0].buildNumber)
}

$readiness = @($result.targets | Where-Object { $null -ne $_.readyForSubmission })
$readyForSubmission = if ($readiness.Count -eq 0) { '' } else { [string] (-not ($readiness.readyForSubmission -contains $false)) }
$reportedNextActions = @($result.nextActions | Where-Object { -not [string]::IsNullOrWhiteSpace([string] $_) })
$nextActions = $reportedNextActions | ConvertTo-Json -Compress -AsArray
$reportedDiagnostics = @($result.diagnostics | ForEach-Object {
    [ordered]@{
        severity = [string] $_.severity
        category = [string] $_.category
        code = [string] $_.code
        summary = [string] $_.summary
        action = [string] $_.action
        retryable = [bool] $_.retryable
    }
})
$diagnostics = $reportedDiagnostics | ConvertTo-Json -Compress -AsArray

if ($env:GITHUB_OUTPUT) {
    "action=$action" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    "version-source-path=$versionSourcePath" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    "marketing-version=$marketingVersion" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    "build-number=$buildNumber" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    "ready-for-submission=$readyForSubmission" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    "next-actions=$nextActions" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    "diagnostics=$diagnostics" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
}

if ($env:GITHUB_STEP_SUMMARY) {
    $targetLines = @($result.targets | ForEach-Object {
        $state = if ($_.buildProcessingState) { $_.buildProcessingState } elseif ($_.distributionState) { $_.distributionState } else { 'not reported' }
        "- $($_.name): $($_.platform) $($_.version ?? $_.marketingVersion) ($($_.build ?? $_.buildNumber)) — $state"
    })
    @(
        "## Apple release: $action$($planOnly ? ' plan' : '')",
        '',
        "- PowerForge: $($env:POWERFORGE_VERSION)",
        "- Version/build: $marketingVersion ($buildNumber)",
        "- Receipt: $receiptPath",
        "- Ready for submission: $readyForSubmission",
        '',
        '### Targets',
        $targetLines,
        '',
        '### Next actions',
        ($reportedNextActions | ForEach-Object { "- $_" }),
        '',
        '### Diagnostics',
        ($reportedDiagnostics | ForEach-Object { "- **$($_.code)** [$($_.severity)]: $($_.summary) — $($_.action)" })
    ) | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append
}

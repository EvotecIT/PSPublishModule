[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$operation = $env:INPUT_OPERATION
if ($operation -notin @('Snapshot', 'Validate', 'Plan', 'Apply')) {
    throw 'Apple governance operation must be Snapshot, Validate, Plan, or Apply.'
}
if (-not (Test-Path -LiteralPath $env:POWERFORGE_TOOL_PATH -PathType Leaf)) {
    throw "PowerForge executable was not found: $($env:POWERFORGE_TOOL_PATH)"
}

$arguments = @('apple-governance', $operation.ToLowerInvariant())
if ($operation -eq 'Snapshot') {
    if ([string]::IsNullOrWhiteSpace($env:INPUT_APP_ID) -or [string]::IsNullOrWhiteSpace($env:INPUT_OUTPUT_PATH)) {
        throw 'Snapshot requires app-id and output-path.'
    }
    $arguments += @('--app-id', $env:INPUT_APP_ID, '--out', $env:INPUT_OUTPUT_PATH)
} else {
    if ([string]::IsNullOrWhiteSpace($env:INPUT_CONFIG_PATH)) { throw "$operation requires config-path." }
    $arguments += @('--config', $env:INPUT_CONFIG_PATH)
}
if (-not [string]::IsNullOrWhiteSpace($env:INPUT_RELEASE_CONFIG_PATH)) {
    $arguments += @('--release-config', $env:INPUT_RELEASE_CONFIG_PATH)
}
if ($operation -in @('Plan', 'Apply')) {
    $receiptPath = $env:INPUT_RECEIPT_PATH
    if ([string]::IsNullOrWhiteSpace($receiptPath)) {
        $configPath = [IO.Path]::GetFullPath($env:INPUT_CONFIG_PATH)
        $receiptName = if ($operation -eq 'Plan') { 'governance-plan.json' } else { 'governance-receipt.json' }
        $receiptPath = Join-Path (Split-Path -Parent $configPath) ".powerforge/apple/$receiptName"
    }
    $arguments += @('--receipt', $receiptPath)
}
if ($operation -eq 'Apply') {
    if ($env:INPUT_CONFIRM -ne 'true') { throw 'Apply requires confirm=true.' }
    if ([string]::IsNullOrWhiteSpace($env:INPUT_REVIEWED_PLAN_PATH)) { throw 'Apply requires reviewed-plan-path.' }
    if (-not (Test-Path -LiteralPath $env:INPUT_REVIEWED_PLAN_PATH -PathType Leaf)) {
        throw "Reviewed governance plan was not found: $($env:INPUT_REVIEWED_PLAN_PATH)"
    }
    $arguments += @('--reviewed-plan', $env:INPUT_REVIEWED_PLAN_PATH)
    $arguments += @('--confirm', '--max-changes', $env:INPUT_MAXIMUM_CHANGES)
}
if ($operation -eq 'Plan' -and $env:INPUT_FAIL_ON_DRIFT -eq 'true') { $arguments += '--fail-on-drift' }
$arguments += @('--summary', '--output', 'json')

$output = & $env:POWERFORGE_TOOL_PATH @arguments 2>&1
$exitCode = $LASTEXITCODE
$text = ($output | Out-String).Trim()
try { $envelope = $text | ConvertFrom-Json -Depth 100 } catch { throw "PowerForge returned invalid governance JSON (exit $exitCode)." }
$result = $envelope.result
$finalPlan = if ($operation -in @('Plan', 'Apply')) { $result } else { $null }
$receiptPath = if ($operation -eq 'Snapshot') { $env:INPUT_OUTPUT_PATH } elseif ($operation -in @('Plan', 'Apply')) { $receiptPath } else { '' }
"receipt-path=$receiptPath" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
"drift-count=$($finalPlan.driftCount ?? 0)" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
"blocked-count=$($finalPlan.blockedCount ?? 0)" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
"converged=$(($finalPlan.isConverged ?? $true).ToString().ToLowerInvariant())" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
if ($exitCode -ne 0) {
    $message = if ($envelope.error) { [string] $envelope.error } else { "Apple governance $operation failed with exit code $exitCode." }
    $message = $message -replace '(?i)(issuer|key|token|secret|authorization)\s*[=:]\s*\S+', '$1=[redacted]'
    throw $message
}

#requires -Version 7.4
<#
.SYNOPSIS
Removes the CI compiler workspace only after owned workflow shutdown is recorded.
.DESCRIPTION
Retains both roots and their recovery receipts when a workflow is interrupted or
fixture Stop fails. It never stops processes or treats failed qualification as
successful qualification; a completed failure may still release its workspace.
#>
[CmdletBinding()]
param([Parameter(Mandatory)] [string] $RunnerTemp)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PowerShellWholeWorkflowWorkspace.ps1')
$RunnerTemp = [IO.Path]::GetFullPath($RunnerTemp)
$gateRoot = Assert-ContainedPath -Root $RunnerTemp -Path (Join-Path $RunnerTemp 'powerforge-compiler-gate') -Label 'Compiler gate cleanup'
$decisionPath = Join-Path $RunnerTemp 'powerforge-compiler-gate-cleanup.json'
$workflowRoot = $null
$workflowOwnership = $null
$reasons = [Collections.Generic.List[string]]::new()

function Read-CleanupReceipt([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        $reasons.Add("Missing terminal receipt: $Path")
        return $null
    }
    try { return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -ErrorAction Stop }
    catch { $reasons.Add("Unreadable terminal receipt: $Path"); return $null }
}

try {
    Assert-WorkflowPathWithoutLinks $gateRoot
    $workflowOwnership = Get-OwnedWholeWorkflowWorkspace -RunnerTemp $RunnerTemp
    if ($workflowOwnership) { $workflowRoot = $workflowOwnership.workspacePath }
    if ($workflowRoot) {
        $wholeEvidence = Join-Path $gateRoot 'evidence/whole-workflows'
        $terminal = Read-CleanupReceipt (Join-Path $wholeEvidence 'whole-workflow-gate.json')
        if ($terminal -and ($terminal.schemaVersion -ne 1 -or $terminal.passed -isnot [bool] -or $terminal.plannedLanes -lt 1)) {
            $reasons.Add('Invalid whole-workflow terminal envelope.')
        }
        # A surviving run is intentionally retained or was interrupted before its finally.
        $runs = Join-Path $workflowRoot 'runs'
        if ((Test-Path -LiteralPath $runs) -and @(Get-ChildItem -LiteralPath $runs -Force).Count) {
            $reasons.Add("Owned workflow runs still require release: $runs")
        }
        foreach ($lane in @(Get-ChildItem -LiteralPath $wholeEvidence -Directory -ErrorAction SilentlyContinue)) {
            $process = Read-CleanupReceipt (Join-Path $lane.FullName 'qualifier.process.json')
            $result = Read-CleanupReceipt (Join-Path $lane.FullName 'qualification.json')
            if ($process -and $process.ExitCode -isnot [int] -and $process.ExitCode -isnot [long]) { $reasons.Add("Invalid qualifier terminal process: $($lane.FullName)") }
            if ($result -and ($result.schemaVersion -ne 1 -or $null -eq $result.summary)) { $reasons.Add("Invalid qualification terminal envelope: $($lane.FullName)") }
        }
    }
    foreach ($root in @($gateRoot, $workflowRoot | Where-Object { $_ -and (Test-Path -LiteralPath $_) })) {
        $entries = @(Get-ChildItem -LiteralPath $root -Recurse -Force)
        if (@($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw "Cleanup root contains a link or junction: $root" }
        foreach ($entry in $entries) {
            if ($entry.Name -in @('cleanup-held.json', 'fixture-cleanup-failure.json')) { $reasons.Add("Owned cleanup hold: $($entry.FullName)") }
            if ($entry.Name -eq 'fixture.Start.json') {
                $start = Read-CleanupReceipt $entry.FullName
                $stop = Read-CleanupReceipt (Join-Path $entry.DirectoryName 'fixture.Stop.json')
                $stopProcess = Read-CleanupReceipt (Join-Path $entry.DirectoryName 'fixture.Stop.process.json')
                if ($start -and $stop -and $stopProcess -and
                    ($start.schemaVersion -ne 1 -or $start.operation -cne 'Start' -or $null -eq $start.fixtureIdentity -or
                    $stop.schemaVersion -ne 1 -or $stop.operation -cne 'Stop' -or $stop.stopped -isnot [bool] -or -not $stop.stopped -or
                    $stopProcess.ExitCode -ne 0 -or
                    (ConvertTo-Json -InputObject $start.fixtureIdentity -Depth 30 -Compress) -cne
                    (ConvertTo-Json -InputObject $stop.fixtureIdentity -Depth 30 -Compress))) {
                    $reasons.Add("Fixture Stop did not release its exact Start identity: $($entry.DirectoryName)")
                }
            }
        }
    }
} catch { $reasons.Add($_.Exception.Message) }

$decision = [ordered]@{ schemaVersion=1; removed=$false; gateRoot=$gateRoot; workflowRoot=$workflowRoot; workspaceOwnership=$workflowOwnership; reasons=@($reasons) }
if ($reasons.Count) {
    Write-Utf8Json -Path $decisionPath -Value $decision
    Write-Warning "Compiler workspace retained for owned-state recovery; see $decisionPath"
} else {
    try {
        if ($workflowRoot -and (Test-Path -LiteralPath $workflowRoot)) { Remove-Item -LiteralPath $workflowRoot -Recurse -ErrorAction Stop }
        if (Test-Path -LiteralPath $gateRoot) { Remove-Item -LiteralPath $gateRoot -Recurse -ErrorAction Stop }
        $decision.removed = $true
    } catch {
        $decision.reasons = @($_.Exception.Message)
        Write-Utf8Json -Path $decisionPath -Value $decision
        throw
    }
    Write-Utf8Json -Path $decisionPath -Value $decision
}

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$catalogScript = Join-Path $repositoryRoot '.github/actions/powerforge-server-backup/PowerForgeBackupCatalog.ps1'
. $catalogScript

$testRoot = Join-Path ([IO.Path]::GetTempPath()) "powerforge-backup-catalog-$([Guid]::NewGuid().ToString('N'))"
$targetRoot = Join-Path $testRoot 'backups/example'
try {
    $legacyCapture = Join-Path $targetRoot '20260904102030'
    $workflowCapture = Join-Path $targetRoot '20260905T102030Z-12345-1'
    New-Item -ItemType Directory -Path (Join-Path $legacyCapture 'commands'), (Join-Path $workflowCapture 'commands') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $legacyCapture 'plain-files.tar.gz') -Value 'legacy' -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $legacyCapture 'capture-summary.json') -Value '{"commandResults":[],"warnings":[]}' -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $workflowCapture 'plain-files.tar.gz') -Value 'current' -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $workflowCapture 'commands/0000-example.out.txt') -Value 'captured' -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $workflowCapture 'capture-summary.json') -Value '{"commandResults":[{"id":"example","success":true}],"warnings":[]}' -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $workflowCapture 'capture-metadata.json') -Value '{"capturedAtUtc":"2026-09-05T10:20:31.0000000+00:00"}' -Encoding utf8NoBOM

    Update-BackupCatalog -TargetRoot $targetRoot -TargetRelative 'backups/example' -KeepLatestInTree 24

    $latest = (Get-Content -LiteralPath (Join-Path $targetRoot 'LATEST.txt') -Raw).Trim()
    if ($latest -ne '20260905T102030Z-12345-1') {
        throw "LATEST.txt selected an unexpected capture: $latest"
    }

    $index = Get-Content -LiteralPath (Join-Path $targetRoot 'index.json') -Raw | ConvertFrom-Json
    if ($index.latest -ne $latest -or @($index.captures).Count -ne 2) {
        throw 'The backup index did not include both supported capture-name formats.'
    }
    if ($index.retention.keepLatestInTree -ne 24) {
        throw 'The backup index did not record current-tree retention.'
    }

    $current = @($index.captures | Where-Object stamp -eq $latest)
    if ($current.Count -ne 1 -or $current[0].createdAtUtc -ne '2026-09-05T10:20:31.0000000+00:00') {
        throw 'The backup index did not preserve capture metadata time.'
    }
    if ($current[0].commandCount -ne 1 -or $current[0].warningCount -ne 0) {
        throw 'The backup index did not summarize command results.'
    }
    $indexedCommand = @($current[0].files | Where-Object name -eq 'commands/0000-example.out.txt')
    if ($indexedCommand.Count -ne 1 -or $indexedCommand[0].sizeBytes -le 0 -or $indexedCommand[0].sha256 -notmatch '^[a-f0-9]{64}$') {
        throw 'The backup index did not hash nested capture artifacts.'
    }
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}

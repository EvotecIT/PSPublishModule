function New-ProjectItemBackups {
    <#
    .SYNOPSIS
    Creates backups of items before deletion.

    .DESCRIPTION
    Creates backup copies of files and folders before they are deleted,
    preserving the directory structure.

    .PARAMETER ItemsToProcess
    Array of items to create backups for.

    .PARAMETER BackupDirectory
    Directory to store the backups.

    .PARAMETER ProjectPath
    Root path of the project (for calculating relative paths).

    .PARAMETER Internal
    Whether to use internal (verbose) messaging.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [PSObject[]] $ItemsToProcess,

        [Parameter(Mandatory)]
        [string] $BackupDirectory,

        [Parameter(Mandatory)]
        [string] $ProjectPath,

        [bool] $Internal = $false
    )

    $backupsCreated = 0
    $backupErrors = 0

    foreach ($item in $ItemsToProcess) {
        try {
            $relativePath = $item.RelativePath
            $backupPath = Join-Path $BackupDirectory $relativePath
            $backupParent = Split-Path $backupPath -Parent

            if (-not (Test-Path $backupParent)) {
                New-Item -Path $backupParent -ItemType Directory -Force | Out-Null
            }

            if ($item.Type -eq 'File') {
                Copy-Item -LiteralPath $item.FullPath -Destination $backupPath -Force
            } else {
                Copy-Item -LiteralPath $item.FullPath -Destination $backupPath -Recurse -Force
            }

            $backupsCreated++

            if ($Internal) {
                Write-Verbose "Created backup: $relativePath"
            }

        } catch {
            $backupErrors++
            if ($Internal) {
                Write-Warning "Failed to create backup for $($item.RelativePath): $_"
            } else {
                Write-Warning "Failed to create backup for $($item.RelativePath): $_"
            }
        }
    }

    if ($Internal) {
        Write-Verbose "Created $backupsCreated backups with $backupErrors errors"
    } else {
        Write-Host "Created $backupsCreated backups" -ForegroundColor Green
        if ($backupErrors -gt 0) {
            Write-Host "Backup errors: $backupErrors" -ForegroundColor Yellow
        }
    }
}

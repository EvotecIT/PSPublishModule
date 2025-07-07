function Convert-LineEndingSingle {
    param(
        [string] $FilePath,
        [string] $TargetLineEnding,
        [hashtable] $CurrentInfo,
        [bool] $CreateBackup,
        [bool] $EnsureFinalNewline
    )

    try {
        # Read file content as string
        $content = [System.IO.File]::ReadAllText($FilePath)

        if ([string]::IsNullOrEmpty($content)) {
            return @{
                Status = 'Skipped'
                Reason = 'Empty file'
            }
        }

        # Create backup if requested
        $backupPath = $null
        if ($CreateBackup) {
            $backupPath = "$FilePath.backup"
            $counter = 1
            while (Test-Path $backupPath) {
                $backupPath = "$FilePath.backup$counter"
                $counter++
            }
            $originalBytes = [System.IO.File]::ReadAllBytes($FilePath)
            [System.IO.File]::WriteAllBytes($backupPath, $originalBytes)
        }

        # Normalize line endings first (convert all to LF)
        $normalizedContent = $content -replace "`r`n", "`n" -replace "`r", "`n"

        # Convert to target line ending
        $convertedContent = if ($TargetLineEnding -eq 'CRLF') {
            $normalizedContent -replace "`n", "`r`n"
        } else {
            $normalizedContent
        }

        # Ensure final newline if requested
        if ($EnsureFinalNewline -and -not [string]::IsNullOrEmpty($convertedContent)) {
            $targetNewline = if ($TargetLineEnding -eq 'CRLF') { "`r`n" } else { "`n" }
            if (-not $convertedContent.EndsWith($targetNewline)) {
                $convertedContent += $targetNewline
            }
        }

        # Write the converted content
        $encoding = Get-FileEncoding -Path $FilePath -AsObject
        [System.IO.File]::WriteAllText($FilePath, $convertedContent, $encoding.Encoding)

        $changesMade = @()
        if ($CurrentInfo.LineEnding -ne $TargetLineEnding -and $CurrentInfo.LineEnding -ne 'None') {
            $changesMade += "line endings ($($CurrentInfo.LineEnding) → $TargetLineEnding)"
        }
        if ($EnsureFinalNewline -and -not $CurrentInfo.HasFinalNewline) {
            $changesMade += "added final newline"
        }

        return @{
            Status     = 'Converted'
            Reason     = "Converted: $($changesMade -join ', ')"
            BackupPath = $backupPath
        }

    } catch {
        return @{
            Status     = 'Error'
            Reason     = "Failed to convert: $_"
            BackupPath = $backupPath
        }
    }
}
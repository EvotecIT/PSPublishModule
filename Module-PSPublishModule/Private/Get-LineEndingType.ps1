function Get-LineEndingType {
    param([string] $FilePath)

    try {
        $bytes = [System.IO.File]::ReadAllBytes($FilePath)
        if ($bytes.Length -eq 0) {
            return @{
                LineEnding      = 'None'
                HasFinalNewline = $true  # Empty files are considered OK
                FileSize        = 0
            }
        }

        $crlfCount = 0
        $lfOnlyCount = 0
        $crOnlyCount = 0
        $hasFinalNewline = $false

        # Check if file ends with a newline
        $lastByte = $bytes[$bytes.Length - 1]
        if ($lastByte -eq 10) {
            # Ends with LF
            $hasFinalNewline = $true
            if ($bytes.Length -gt 1 -and $bytes[$bytes.Length - 2] -eq 13) {
                # Actually ends with CRLF
            }
        } elseif ($lastByte -eq 13) {
            # Ends with CR
            $hasFinalNewline = $true
        }

        for ($i = 0; $i -lt $bytes.Length - 1; $i++) {
            if ($bytes[$i] -eq 13 -and $bytes[$i + 1] -eq 10) {
                # CRLF found
                $crlfCount++
                $i++ # Skip the LF part
            } elseif ($bytes[$i] -eq 10) {
                # LF only
                $lfOnlyCount++
            } elseif ($bytes[$i] -eq 13) {
                # CR only (check if not followed by LF)
                if ($i + 1 -lt $bytes.Length -and $bytes[$i + 1] -ne 10) {
                    $crOnlyCount++
                }
            }
        }

        # Check last byte for standalone LF or CR (if not already counted)
        if ($bytes.Length -gt 0) {
            $lastByte = $bytes[$bytes.Length - 1]
            if ($lastByte -eq 10 -and ($bytes.Length -eq 1 -or $bytes[$bytes.Length - 2] -ne 13)) {
                $lfOnlyCount++
            } elseif ($lastByte -eq 13) {
                $crOnlyCount++
            }
        }

        # Determine line ending type
        $typesFound = @()
        if ($crlfCount -gt 0) { $typesFound += 'CRLF' }
        if ($lfOnlyCount -gt 0) { $typesFound += 'LF' }
        if ($crOnlyCount -gt 0) { $typesFound += 'CR' }

        $lineEndingType = if ($typesFound.Count -eq 0) {
            'None'
        } elseif ($typesFound.Count -eq 1) {
            $typesFound[0]
        } else {
            'Mixed'
        }

        return @{
            LineEnding      = $lineEndingType
            HasFinalNewline = $hasFinalNewline
            FileSize        = $bytes.Length
        }

    } catch {
        return @{
            LineEnding      = 'Error'
            HasFinalNewline = $false
            FileSize        = 0
        }
    }
}
function Get-CurrentLineEnding {
    param([string] $FilePath)

    try {
        $bytes = [System.IO.File]::ReadAllBytes($FilePath)
        if ($bytes.Length -eq 0) {
            return @{
                LineEnding      = 'None'
                HasFinalNewline = $true
            }
        }

        $crlfCount = 0
        $lfOnlyCount = 0
        $crOnlyCount = 0
        $hasFinalNewline = $false

        # Check if file ends with a newline
        $lastByte = $bytes[$bytes.Length - 1]
        if ($lastByte -eq 10) {
            $hasFinalNewline = $true
        } elseif ($lastByte -eq 13) {
            $hasFinalNewline = $true
        }

        for ($i = 0; $i -lt $bytes.Length - 1; $i++) {
            if ($bytes[$i] -eq 13 -and $bytes[$i + 1] -eq 10) {
                $crlfCount++
                $i++
            } elseif ($bytes[$i] -eq 10) {
                $lfOnlyCount++
            } elseif ($bytes[$i] -eq 13) {
                if ($i + 1 -lt $bytes.Length -and $bytes[$i + 1] -ne 10) {
                    $crOnlyCount++
                }
            }
        }

        if ($bytes.Length -gt 0) {
            $lastByte = $bytes[$bytes.Length - 1]
            if ($lastByte -eq 10 -and ($bytes.Length -eq 1 -or $bytes[$bytes.Length - 2] -ne 13)) {
                $lfOnlyCount++
            } elseif ($lastByte -eq 13) {
                $crOnlyCount++
            }
        }

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
        }
    } catch {
        return @{
            LineEnding      = 'Error'
            HasFinalNewline = $false
        }
    }
}
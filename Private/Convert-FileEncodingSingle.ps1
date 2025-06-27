function Convert-FileEncodingSingle {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)][string] $FilePath,
        [System.Text.Encoding] $SourceEncoding,
        [System.Text.Encoding] $TargetEncoding,
        [switch] $Force,
        [switch] $NoRollbackOnMismatch
    )

    $bytesBefore = $null

    try {
        $detectedObj = Get-FileEncoding -Path $FilePath -AsObject
        $detected = $detectedObj.Encoding
        if ($detected.WebName -ne $SourceEncoding.WebName -and -not $Force) {
            Write-Verbose "Skipping $FilePath because encoding $($detected.WebName) does not match expected $($SourceEncoding.WebName)."
            return
        }

        if ($detected.WebName -eq $TargetEncoding.WebName) {
            Write-Verbose "Skipping $FilePath because encoding already $($TargetEncoding.WebName)."
            return
        }

        if ($PSCmdlet.ShouldProcess($FilePath, "Convert from $($detected.WebName) to $($TargetEncoding.WebName)")) {
            $content = [System.IO.File]::ReadAllText($FilePath, $detected)
            $bytesBefore = [System.IO.File]::ReadAllBytes($FilePath)

            [System.IO.File]::WriteAllText($FilePath, $content, $TargetEncoding)

            $converted = [System.IO.File]::ReadAllText($FilePath, $TargetEncoding)
            if ($converted -ne $content) {
                Write-Warning "Content changed after converting $FilePath"
                if (-not $NoRollbackOnMismatch) {
                    [System.IO.File]::WriteAllBytes($FilePath, $bytesBefore)
                    Write-Warning "Reverted changes in $FilePath"
                }
            }
        }
    } catch {
        Write-Warning "Failed to convert ${FilePath}: $_"
        if (-not $NoRollbackOnMismatch -and $bytesBefore) {
            try { [System.IO.File]::WriteAllBytes($FilePath, $bytesBefore) } catch {}
        }
    }
}

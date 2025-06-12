function Get-Encoding {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ValueFromPipeline, ValueFromPipelineByPropertyName)][Alias('FullName')][string] $Path
    )
    process {
        if (-not (Test-Path -LiteralPath $Path)) {
            $msg = "Get-Encoding - File not found: $Path"
            if ($ErrorActionPreference -eq 'Stop') { throw $msg }
            Write-Warning $msg
            return
        }

        $fs = [System.IO.FileStream]::new($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read)
        try {
            $bom = [byte[]]::new(4)
            $null = $fs.Read($bom, 0, 4)
            $enc = [System.Text.Encoding]::ASCII

            if     ($bom[0] -eq 0x2b -and $bom[1] -eq 0x2f -and $bom[2] -eq 0x76) { $enc = [System.Text.Encoding]::UTF7 }
            elseif ($bom[0] -eq 0xff -and $bom[1] -eq 0xfe) { $enc = [System.Text.Encoding]::Unicode }
            elseif ($bom[0] -eq 0xfe -and $bom[1] -eq 0xff) { $enc = [System.Text.Encoding]::BigEndianUnicode }
            elseif ($bom[0] -eq 0x00 -and $bom[1] -eq 0x00 -and $bom[2] -eq 0xfe -and $bom[3] -eq 0xff) { $enc = [System.Text.Encoding]::UTF32 }
            elseif ($bom[0] -eq 0xef -and $bom[1] -eq 0xbb -and $bom[2] -eq 0xbf) {
                $enc = [System.Text.UTF8Encoding]::new($true)
            } else {
                $fs.Position = 0
                $byte = [byte[]]::new(1)
                while ($fs.Read($byte, 0, 1) -gt 0) {
                    if ($byte[0] -gt 0x7F) { $enc = [System.Text.UTF8Encoding]::new($false); break }
                }
            }
        } finally {
            $fs.Close()
            $fs.Dispose()
        }

        [PSCustomObject]@{ Encoding = $enc; Path = $Path }
    }
}

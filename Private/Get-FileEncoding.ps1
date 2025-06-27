function Get-FileEncoding {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ValueFromPipeline, ValueFromPipelineByPropertyName)][Alias('FullName')][string] $Path,
        [switch] $AsObject
    )
    process {
        if (-not (Test-Path -LiteralPath $Path)) {
            $msg = "Get-FileEncoding - File not found: $Path"
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

        if ($AsObject) {
            $encName = if ($enc -is [System.Text.UTF8Encoding] -and $enc.GetPreamble().Length -eq 3) { 'UTF8BOM' }
                       elseif ($enc -is [System.Text.UTF8Encoding]) { 'UTF8' }
                       elseif ($enc -is [System.Text.UnicodeEncoding]) { 'Unicode' }
                       elseif ($enc -is [System.Text.UTF7Encoding]) { 'UTF7' }
                       elseif ($enc -is [System.Text.UTF32Encoding]) { 'UTF32' }
                       elseif ($enc -is [System.Text.ASCIIEncoding]) { 'Ascii' }
                       elseif ($enc -is [System.Text.BigEndianUnicodeEncoding]) { 'BigEndianUnicode' }
                       else { $enc.WebName }
            [PSCustomObject]@{
                Path         = $Path
                Encoding     = $enc
                EncodingName = $encName
            }
        } else {
            $enc
        }
    }
}

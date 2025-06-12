function Convert-FileEncodingSingle {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $FilePath,
        [System.Text.Encoding] $SourceEncoding,
        [System.Text.Encoding] $TargetEncoding,
        [switch] $Force
    )

    $detected = (Get-Encoding -Path $FilePath).Encoding
    if ($detected.WebName -ne $SourceEncoding.WebName) {
        if (-not $Force) {
            Write-Verbose "Skipping $FilePath because encoding $($detected.WebName) does not match expected $($SourceEncoding.WebName)."
            return
        }
    }

    $content = [System.IO.File]::ReadAllText($FilePath, $detected)
    [System.IO.File]::WriteAllText($FilePath, $content, $TargetEncoding)
}

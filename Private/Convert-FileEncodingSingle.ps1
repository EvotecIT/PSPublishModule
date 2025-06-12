function Convert-FileEncodingSingle {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $FilePath,
        [System.Text.Encoding] $SourceEncoding,
        [System.Text.Encoding] $TargetEncoding
    )

    $content = [System.IO.File]::ReadAllText($FilePath, $SourceEncoding)
    [System.IO.File]::WriteAllText($FilePath, $content, $TargetEncoding)
}

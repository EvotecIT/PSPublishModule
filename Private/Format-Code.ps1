function Format-Code {
    [cmdletbinding()]
    param(
        [string] $FilePath,
        $FormatCode
    )
    if ($FormatCode.Enabled) {
        if ($FormatCode.RemoveComments) {
            Write-Verbose "Removing Comments - $FilePath"
            $Output = Remove-Comments -FilePath $FilePath
        } else {
            $Output = Get-Content -LiteralPath $FilePath -Raw
        }
        if ($null -eq $FormatCode.FormatterSettings) {
            $FormatCode.FormatterSettings = $Script:FormatterSettings
        }
        Write-Verbose "Formatting - $FilePath"
        $Output = Invoke-Formatter -ScriptDefinition $Output -Settings $FormatCode.FormatterSettings -Verbose:$false

        # Resave
        $Output = foreach ($O in $Output) {
            if ($O.Trim() -ne '') {
                $O.Trim()
            }
        }
        $Output | Out-File -LiteralPath $FilePath -NoNewline -Encoding utf8
    }
}

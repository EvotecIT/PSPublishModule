function Format-PSD1 {
    param(
        [string] $PSD1FilePath,
        $FormatCode
    )
    if ($FormatCode.Enabled) {
        $Output = Get-Content -LiteralPath $PSD1FilePath -Raw
        if ($FormatCode.RemoveComments) {
            Write-Verbose "Removing Comments - $PSD1FilePath"
            # Remove comments
            $Output = Remove-Comments -ScriptContent $Output
        }
        Write-Verbose "Formatting - $PSD1FilePath"

        if ($null -eq $FormatCode.FormatterSettings) {
            $FormatCode.FormatterSettings = $Script:FormatterSettings
        }

        $Output = Invoke-Formatter -ScriptDefinition $Output -Settings $FormatCode.FormatterSettings
        #$Output = foreach ($O in $Output) {
        #    if ($O.Trim() -ne '') {
        #        $O.Trim()
        #    }
        #}
        $Output | Out-File -LiteralPath $PSD1FilePath -NoNewline
    }
}
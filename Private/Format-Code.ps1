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
        try {
            $Output = Invoke-Formatter -ScriptDefinition $Output -Settings $FormatCode.FormatterSettings -Verbose:$false
        } catch {
            $ErrorMessage = $_.Exception.Message
            #Write-Warning "Merge module on file $FilePath failed. Error: $ErrorMessage"
            Write-Error "Format-Code - Formatting on file $FilePath failed. Error: $ErrorMessage"
            Exit
        }
        # Resave
        $Output = foreach ($O in $Output) {
            if ($O.Trim() -ne '') {
                $O.Trim()
            }
        }
        try {
            $Output | Out-File -LiteralPath $FilePath -NoNewline -Encoding utf8
        } catch {
            $ErrorMessage = $_.Exception.Message
            #Write-Warning "Merge module on file $FilePath failed. Error: $ErrorMessage"
            Write-Error "Format-Code - Resaving file $FilePath failed. Error: $ErrorMessage"
            Exit
        }
    }
}

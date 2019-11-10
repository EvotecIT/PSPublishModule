function Format-Code {
    [cmdletbinding()]
    param(
        [string] $FilePath,
        $FormatCode
    )
    if ($FormatCode.Enabled) {
        if ($FormatCode.RemoveComments) {
            # Write-Verbose "Removing Comments"
            $Output = Write-TextWithTime -Text "[+] Removing Comments - $FilePath" {
                Remove-Comments -FilePath $FilePath
            }
        } else {
            $Output = Write-TextWithTime -Text "[+] Reading file content - $FilePath" {
                Get-Content -LiteralPath $FilePath -Raw
            }
        }
        if ($null -eq $FormatCode.FormatterSettings) {
            $FormatCode.FormatterSettings = $Script:FormatterSettings
        }
        $Output = Write-TextWithTime -Text "[+] Formatting file - $FilePath" {
            # Write-Verbose "Formatting - $FilePath"
            try {
                Invoke-Formatter -ScriptDefinition $Output -Settings $FormatCode.FormatterSettings -Verbose:$false
            } catch {
                $ErrorMessage = $_.Exception.Message
                #Write-Warning "Merge module on file $FilePath failed. Error: $ErrorMessage"
                Write-Error "Format-Code - Formatting on file $FilePath failed. Error: $ErrorMessage"
                Exit
            }
        }
        # Resave
        $Output = foreach ($O in $Output) {
            if ($O.Trim() -ne '') {
                $O.Trim()
            }
        }
        Write-TextWithTime -Text "[+] Saving file - $FilePath" {
            try {
                $Output | Out-File -LiteralPath $FilePath -NoNewline -Encoding utf8
            } catch {
                $ErrorMessage = $_.Exception.Message
                #Write-Warning "Merge module on file $FilePath failed. Error: $ErrorMessage"
                Write-Text "[-] Format-Code - Resaving file $FilePath failed. Error: $ErrorMessage" -Color Red
                Exit
            }
        }
    }
}

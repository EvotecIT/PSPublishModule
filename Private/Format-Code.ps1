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
        $Data = Write-TextWithTime -Text "[+] Formatting file - $FilePath" {
            # Write-Verbose "Formatting - $FilePath"
            try {
                Invoke-Formatter -ScriptDefinition $Output -Settings $FormatCode.FormatterSettings -Verbose:$false
            } catch {
                $ErrorMessage = $_.Exception.Message
                #Write-Warning "Merge module on file $FilePath failed. Error: $ErrorMessage"
                Write-Host # This is to add new line, because the first line was opened up.
                Write-Text "[-] Format-Code - Formatting on file $FilePath failed. Error: $ErrorMessage" -Color Red
                Exit
            }
        }
        Write-TextWithTime -Text "[+] Saving file - $FilePath" {
            # Resave
            $Final = foreach ($O in $Data) {
                if ($O.Trim() -ne '') {
                    $O.Trim()
                }
            }
            try {
                $Final | Out-File -LiteralPath $FilePath -NoNewline -Encoding utf8
            } catch {
                $ErrorMessage = $_.Exception.Message
                #Write-Warning "Merge module on file $FilePath failed. Error: $ErrorMessage"
                Write-Host # This is to add new line, because the first line was opened up.
                Write-Text "[-] Format-Code - Resaving file $FilePath failed. Error: $ErrorMessage" -Color Red
                Exit
            }
        }
    }
}

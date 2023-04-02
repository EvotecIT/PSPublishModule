function Format-Code {
    [cmdletbinding()]
    param(
        [string] $FilePath,
        [System.Collections.IDictionary] $FormatCode
    )
    if ($FormatCode.Enabled) {
        if ($FormatCode.RemoveComments) {
            # Write-Verbose "Removing Comments"
            $ContentBefore = Get-Content -LiteralPath $FilePath
            Write-Text "[i] Removing Comments - Lines in code before: $($ContentBefore.Count)" -Color Yellow
            $Output = Write-TextWithTime -Text "[+] Removing Comments - $FilePath" {
                Remove-Comments -FilePath $FilePath
            }
            if ($Output -and $Output.StartPosition -and $Output.StartPosition.EndLine -gt 1) {
                Write-Text "[i] Removing Comments - Lines in code after: $($Output.StartPosition.EndLine)" -Color Yellow
            } else {
                Write-Text "[i] Removing Comments - Lines in code after: 0" -Color Red
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
            try {
                Invoke-Formatter -ScriptDefinition $Output -Settings $FormatCode.FormatterSettings #-Verbose:$false
            } catch {
                $ErrorMessage = $_.Exception.Message
                #Write-Warning "Merge module on file $FilePath failed. Error: $ErrorMessage"
                Write-Host # This is to add new line, because the first line was opened up.
                Write-Text "[-] Format-Code - Formatting on file $FilePath failed." -Color Red
                Write-Text "[-] Format-Code - Error: $ErrorMessage" -Color Red
                Write-Text "[-] Format-Code - This is most likely related to a bug in PSScriptAnalyzer running inside VSCode. Please try running outside of VSCode when using formatting." -Color Red
                return $false
            }
        }
        if ($Data -eq $false) {
            return $false
        }
        Write-TextWithTime -Text "[+] Saving file - $FilePath" {
            # Resave
            $Final = foreach ($O in $Data) {
                if ($O.Trim() -ne '') {
                    $O.Trim()
                }
            }
            try {
                $Final | Out-File -LiteralPath $FilePath -NoNewline -Encoding utf8 -ErrorAction Stop
            } catch {
                $ErrorMessage = $_.Exception.Message
                #Write-Warning "Merge module on file $FilePath failed. Error: $ErrorMessage"
                Write-Host # This is to add new line, because the first line was opened up.
                Write-Text "[-] Format-Code - Resaving file $FilePath failed. Error: $ErrorMessage" -Color Red
                return $false
            }
        }
    }
}

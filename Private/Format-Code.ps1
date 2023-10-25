function Format-Code {
    [cmdletbinding()]
    param(
        [string] $FilePath,
        [System.Collections.IDictionary] $FormatCode
    )

    if ($PSVersionTable.PSVersion.Major -gt 5) {
        $Encoding = 'UTF8BOM'
    } else {
        $Encoding = 'UTF8'
    }

    if ($FormatCode.Enabled) {
        if ($FormatCode.RemoveComments -or $FormatCode.RemoveCommentsInParamBlock -or $FormatCode.RemoveCommentsBeforeParamBlock) {
            $Output = Write-TextWithTime -Text "[+] Removing Comments - $FilePath" {
                $removeCommentsSplat = @{
                    SourceFilePath                 = $FilePath
                    RemoveCommentsInParamBlock     = $FormatCode.RemoveCommentsInParamBlock
                    RemoveCommentsBeforeParamBlock = $FormatCode.RemoveCommentsBeforeParamBlock
                    RemoveAllEmptyLines            = $FormatCode.RemoveAllEmptyLines
                    RemoveEmptyLines               = $FormatCode.RemoveEmptyLines
                }
                Remove-Comments @removeCommentsSplat
            }
        } elseif ($FormatCode.RemoveAllEmptyLines -or $FormatCode.RemoveEmptyLines) {
            $Output = Write-TextWithTime -Text "[+] Removing Empty Lines - $FilePath" {
                $removeEmptyLinesSplat = @{
                    SourceFilePath      = $FilePath
                    RemoveAllEmptyLines = $FormatCode.RemoveAllEmptyLines
                    RemoveEmptyLines    = $FormatCode.RemoveEmptyLines
                }
                Remove-EmptyLines @removeEmptyLinesSplat
            }
        } else {
            $Output = Write-TextWithTime -Text "Reading file content - $FilePath" {
                Get-Content -LiteralPath $FilePath -Raw -Encoding UTF8
            } -PreAppend Plus -SpacesBefore '   '
        }
        if ($null -eq $FormatCode.FormatterSettings) {
            $FormatCode.FormatterSettings = $Script:FormatterSettings
        }
        $Data = Write-TextWithTime -Text "Formatting file - $FilePath" {
            try {
                Invoke-Formatter -ScriptDefinition $Output -Settings $FormatCode.FormatterSettings #-Verbose:$false
            } catch {
                $ErrorMessage = $_.Exception.Message
                #Write-Warning "Merge module on file $FilePath failed. Error: $ErrorMessage"
                Write-Host # This is to add new line, because the first line was opened up.
                Write-Text "   [-] Format-Code - Formatting on file $FilePath failed." -Color Red
                Write-Text "   [-] Format-Code - Error: $ErrorMessage" -Color Red
                Write-Text "   [-] Format-Code - This is most likely related to a bug in PSScriptAnalyzer running inside VSCode. Please try running outside of VSCode when using formatting." -Color Red
                return $false
            }
        } -PreAppend Plus -SpacesBefore '   '
        if ($Data -eq $false) {
            return $false
        }
        Write-TextWithTime -Text "Saving file - $FilePath" {
            # Resave
            $Final = foreach ($O in $Data) {
                if ($O.Trim() -ne '') {
                    $O.Trim()
                }
            }
            try {
                $Final | Out-File -LiteralPath $FilePath -NoNewline -Encoding $Encoding -ErrorAction Stop
            } catch {
                $ErrorMessage = $_.Exception.Message
                #Write-Warning "Merge module on file $FilePath failed. Error: $ErrorMessage"
                Write-Host # This is to add new line, because the first line was opened up.
                Write-Text "[-] Format-Code - Resaving file $FilePath failed. Error: $ErrorMessage" -Color Red
                return $false
            }
        } -PreAppend Plus -SpacesBefore '   '
    }
}

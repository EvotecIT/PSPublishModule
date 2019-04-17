function Merge-Module {
    [CmdletBinding()]
    param (
        [string] $ModuleName,
        [string] $ModulePathSource,
        [string] $ModulePathTarget,
        [Parameter(Mandatory = $false, ValueFromPipeline = $false)]
        [ValidateSet("ASC", "DESC", "NONE")]
        [string] $Sort = 'NONE',
        [string[]] $FunctionsToExport,
        [string[]] $AliasesToExport,
        [Array] $LibrariesCore,
        [Array] $LibrariesDefault,
        $FormatCodePSM1,
        $FormatCodePSD1
    )
    $PSM1FilePath = "$ModulePathTarget\$ModuleName.psm1"
    $PSD1FilePath = "$ModulePathTarget\$ModuleName.psd1"

    $ScriptFunctions = @( Get-ChildItem -Path $ModulePathSource\*.ps1 -ErrorAction SilentlyContinue -Recurse )
    # $ModulePSM = @( Get-ChildItem -Path $ModulePathSource\*.psm1 -ErrorAction SilentlyContinue -Recurse )
    if ($Sort -eq 'ASC') {
        $ScriptFunctions = $ScriptFunctions | Sort-Object -Property Name
    } elseif ($Sort -eq 'DESC') {
        $ScriptFunctions = $ScriptFunctions | Sort-Object -Descending -Property Name
    }

    foreach ($FilePath in $ScriptFunctions) {
        $Content = Get-Content -Path $FilePath -Raw
        $Content = $Content.Replace('$PSScriptRoot\..\..\', '$PSScriptRoot\')
        $Content = $Content.Replace('$PSScriptRoot\..\', '$PSScriptRoot\')
        $Content | Out-File -Append -LiteralPath $PSM1FilePath -Encoding utf8
    }

    New-PSMFile -Path $PSM1FilePath `
        -FunctionNames $FunctionsToExport `
        -FunctionAliaes $AliasesToExport `
        -LibrariesCore $LibrariesCore `
        -LibrariesDefault $LibrariesDefault

    Format-Code -FilePath $PSM1FilePath -FormatCode $FormatCodePSM1
    <#
    if ($FormatCodePSM1.Enabled) {
        $Output = Get-Content -LiteralPath $PSM1FilePath -Raw

        if ($FormatCodePSM1.RemoveComments) {
            Write-Verbose "Removing Comments - $PSM1FilePath"
            $Output = Remove-Comments -Scriptblock $Output
        }
        if ($FormatCodePSM1.FormatterPSM1) {
            Write-Verbose "Formatting - $PSM1FilePath"
            $Output = Invoke-Formatter -ScriptDefinition $Output -Settings $FormatCode.FormatterSettings
        }
        # Resave
        $Output = foreach ($O in $Output) {
            if ($O.Trim() -ne '') {
                $O.Trim()
            }
        }
        $Output | Out-File -LiteralPath $PSM1FilePath -NoNewline
    }
    #>



    <#
    foreach ($FilePath in $ScriptFunctions) {
        $Results = [System.Management.Automation.Language.Parser]::ParseFile($FilePath, [ref]$null, [ref]$null)
        $Functions = $Results.EndBlock.Extent.Text
        $Functions | Add-Content -Path "$ModulePathTarget\$ModuleName.psm1"
    }

    foreach ($FilePath in $ModulePSM) {
        $Content = Get-Content $FilePath
        $Content | Add-Content -Path "$ModulePathTarget\$ModuleName.psm1"
    }
    #>
    #Copy-Item -Path "$ModulePathSource\$ModuleName.psd1" "$ModulePathTarget\$ModuleName.psd1"
    New-PersonalManifest -Configuration $Configuration -ManifestPath $PSD1FilePath
    Format-Code -FilePath $PSD1FilePath -FormatCode $FormatCodePSD1
    <#
    if ($FormatCodePSD1.Enabled) {
        $Output = Get-Content -LiteralPath $PSD1FilePath -Raw
        if ($FormatCodePSD1.RemoveComments) {
            Write-Verbose "Removing Comments - $PSD1FilePath"
            # Remove comments
            $Output = Remove-Comments -Scriptblock $Output
        }
        Write-Verbose "Formatting - $PSD1FilePath"
        $Output = Invoke-Formatter -ScriptDefinition $Output -Settings $FormatCode.FormatterSettings
        $Output = foreach ($O in $Output) {
            if ($O.Trim() -ne '') {
                $O.Trim()
            }
        }
        $Output | Out-File -LiteralPath $PSD1FilePath -NoNewline
    }
    #>
}
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
        [System.Collections.IDictionary] $FormatCodePSM1,
        [System.Collections.IDictionary] $FormatCodePSD1,
        [System.Collections.IDictionary] $Configuration
    )
    $PSM1FilePath = "$ModulePathTarget\$ModuleName.psm1"
    $PSD1FilePath = "$ModulePathTarget\$ModuleName.psd1"

    if ($PSEdition -eq 'Core') {
        $ScriptFunctions = Get-ChildItem -Path $ModulePathSource\*.ps1 -ErrorAction SilentlyContinue -Recurse -FollowSymlink
    } else {
        $ScriptFunctions = Get-ChildItem -Path $ModulePathSource\*.ps1 -ErrorAction SilentlyContinue -Recurse
    }
    if ($Sort -eq 'ASC') {
        $ScriptFunctions = $ScriptFunctions | Sort-Object -Property Name
    } elseif ($Sort -eq 'DESC') {
        $ScriptFunctions = $ScriptFunctions | Sort-Object -Descending -Property Name
    }

    foreach ($FilePath in $ScriptFunctions) {
        $Content = Get-Content -Path $FilePath -Raw
        $Content = $Content.Replace('$PSScriptRoot\..\..\', '$PSScriptRoot\')
        $Content = $Content.Replace('$PSScriptRoot\..\', '$PSScriptRoot\')

        try {
            $Content | Out-File -Append -LiteralPath $PSM1FilePath -Encoding utf8
        } catch {
            $ErrorMessage = $_.Exception.Message
            #Write-Warning "Merge module on file $FilePath failed. Error: $ErrorMessage"
            Write-Error "Merge-Module - Merge on file $FilePath failed. Error: $ErrorMessage"
            Exit
        }
    }


    # Using file is needed if there are 'using namespaces' - this is a workaround provided by seeminglyscience
    $FilePathUsing = "$ModulePathTarget\$ModuleName.Usings.ps1"

    $UsingInPlace = Format-UsingNamespace -FilePath $PSM1FilePath -FilePathUsing $FilePathUsing
    if ($UsingInPlace) {
        Format-Code -FilePath $FilePathUsing -FormatCode $FormatCodePSM1
        $Configuration.UsingInPlace = "$ModuleName.Usings.ps1"
    }

    New-PSMFile -Path $PSM1FilePath `
        -FunctionNames $FunctionsToExport `
        -FunctionAliaes $AliasesToExport `
        -LibrariesCore $LibrariesCore `
        -LibrariesDefault $LibrariesDefault `
        -ModuleName $ModuleName `
        -UsingNamespaces:$UsingInPlace

    Format-Code -FilePath $PSM1FilePath -FormatCode $FormatCodePSM1
    New-PersonalManifest -Configuration $Configuration -ManifestPath $PSD1FilePath -AddUsingsToProcess
    Format-Code -FilePath $PSD1FilePath -FormatCode $FormatCodePSD1

    # cleans up empty directories
    Get-ChildItem $ModulePathTarget -Recurse -Force -Directory | Sort-Object -Property FullName -Descending | `
        Where-Object { $($_ | Get-ChildItem -Force | Select-Object -First 1).Count -eq 0 } | `
        Remove-Item -Verbose
}
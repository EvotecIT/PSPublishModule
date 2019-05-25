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
        $Content | Out-File -Append -LiteralPath $PSM1FilePath -Encoding utf8
    }


    # Using file is needed if there are 'using namespaces' - this is a workaround provided by seeminglyscience
    $FilePathUsing = "$ModulePathTarget\$ModuleName.ps1"

    $UsingInPlace = Format-UsingNamespace -FilePath $PSM1FilePath -FilePathUsing $FilePathUsing
    if ($UsingInPlace) {
        Format-Code -FilePath $FilePathUsing -FormatCode $FormatCodePSM1
    }

    New-PSMFile -Path $PSM1FilePath `
        -FunctionNames $FunctionsToExport `
        -FunctionAliaes $AliasesToExport `
        -LibrariesCore $LibrariesCore `
        -LibrariesDefault $LibrariesDefault `
        -ModuleName $ModuleName `
        -UsingNamespaces:$UsingInPlace

    Format-Code -FilePath $PSM1FilePath -FormatCode $FormatCodePSM1
    New-PersonalManifest -Configuration $Configuration -ManifestPath $PSD1FilePath
    Format-Code -FilePath $PSD1FilePath -FormatCode $FormatCodePSD1


}
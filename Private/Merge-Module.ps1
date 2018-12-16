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
        [Array] $LibrariesDefault
    )
    $ScriptFunctions = @( Get-ChildItem -Path $ModulePathSource\*.ps1 -ErrorAction SilentlyContinue -Recurse )
    # $ModulePSM = @( Get-ChildItem -Path $ModulePathSource\*.psm1 -ErrorAction SilentlyContinue -Recurse )
    if ($Sort -eq 'ASC') {
        $ScriptFunctions = $ScriptFunctions | Sort-Object -Property Name
    } elseif ($Sort -eq 'DESC') {
        $ScriptFunctions = $ScriptFunctions | Sort-Object -Descending -Property Name
    }

    foreach ($FilePath in $ScriptFunctions) {
        $Content = Get-Content -Path $FilePath
        $Content = $Content.Replace('$PSScriptRoot\..\..\', '$PSScriptRoot\')
        $Content = $Content.Replace('$PSScriptRoot\..\', '$PSScriptRoot\')
        $Content | Add-Content $ModulePathTarget\$ModuleName.psm1
    }

    New-PSMFile -Path $ModulePathTarget\$ModuleName.psm1 `
        -FunctionNames $FunctionsToExport `
        -FunctionAliaes $AliasesToExport `
        -LibrariesCore $LibrariesCore `
        -LibrariesDefault $LibrariesDefault

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
    New-PersonalManifest -Configuration $Configuration -ManifestPath "$ModulePathTarget\$ModuleName.psd1"
}
function Add-BinaryImportModule {
    <#
    .SYNOPSIS
    Add code into PSM1 that will import binary modules based on the edition

    .DESCRIPTION
    Add code into PSM1 that will import binary modules based on the edition

    .PARAMETER LibrariesStandard
    Parameter description

    .PARAMETER LibrariesCore
    Parameter description

    .PARAMETER LibrariesDefault
    Parameter description

    .PARAMETER Configuration
    Parameter description

    .EXAMPLE
     Add-BinaryImportModule -Configuration $Configuration -LibrariesStandard $LibrariesStandard -LibrariesCore $LibrariesCore -LibrariesDefault $LibrariesDefault

    .NOTES

    .OUTPUT
    # adds code into PSM1 file similar to this one
    if ($PSEdition -eq 'Core') {
        Import-Module -Name "$PSScriptRoot\Lib\Standard\PSEventViewer.PowerShell.dll" -Force -ErrorAction Stop
    } else {
        Import-Module -Name "$PSScriptRoot\Lib\Default\PSEventViewer.PowerShell.dll" -Force -ErrorAction Stop
    }

    #>
    [CmdletBinding()]
    param(
        [string[]] $LibrariesStandard,
        [string[]] $LibrariesCore,
        [string[]] $LibrariesDefault,
        [System.Collections.IDictionary] $Configuration
    )

    if ($null -ne $Configuration.Steps.BuildLibraries.BinaryModule) {
        foreach ($BinaryModule in $Configuration.Steps.BuildLibraries.BinaryModule) {
            if ($LibrariesStandard.Count -gt 0) {
                foreach ($Library in $LibrariesStandard) {
                    if ($Library -like "*\$BinaryModule") {
                        "Import-Module -Name `"`$PSScriptRoot\$Library`" -Force -ErrorAction Stop"
                    }
                }
            } elseif ($LibrariesCore.Count -gt 0 -and $LibrariesDefault.Count -gt 0) {
                'if ($PSEdition -eq ''Core'') {'
                if ($LibrariesCore.Count -gt 0) {
                    foreach ($Library in $LibrariesCore) {
                        if ($Library -like "*\$BinaryModule") {
                            "Import-Module -Name `"`$PSScriptRoot\$Library`" -Force -ErrorAction Stop"
                        }
                    }
                }
                '} else {'
                if ($LibrariesDefault.Count -gt 0) {
                    foreach ($Library in $LibrariesDefault) {
                        if ($Library -like "*\$BinaryModule") {
                            "Import-Module -Name `"`$PSScriptRoot\$Library`" -Force -ErrorAction Stop"
                        }
                    }
                }
                '}'
            } else {
                if ($LibrariesCore.Count -gt 0) {
                    if ($LibrariesCore.Count -gt 0) {
                        foreach ($Library in $LibrariesCore) {
                            if ($Library -like "*\$BinaryModule") {
                                "Import-Module -Name `"`$PSScriptRoot\$Library`" -Force -ErrorAction Stop"
                            }
                        }
                    }
                }
                if ($LibrariesDefault.Count -gt 0) {
                    foreach ($Library in $LibrariesDefault) {
                        if ($Library -like "*\$BinaryModule") {
                            "Import-Module -Name `"`$PSScriptRoot\$Library`" -Force -ErrorAction Stop"
                        }
                    }
                }
            }
        }
    }
}
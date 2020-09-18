function New-PSMFile {
    [cmdletbinding()]
    param(
        [string] $Path,
        [string[]] $FunctionNames,
        [string[]] $FunctionAliaes,
        [System.Collections.IDictionary] $AliasesAndFunctions,
        [Array] $LibrariesStandard,
        [Array] $LibrariesCore,
        [Array] $LibrariesDefault,
        [string] $ModuleName,
        [switch] $UsingNamespaces,
        [string] $LibariesPath,
        [Array] $InternalModuleDependencies,
        [System.Collections.IDictionary] $CommandModuleDependencies
    )
    try {
        # $Content = Get-Content -LiteralPath $Path -Raw

        $LibraryContent = @(
            if ($LibrariesStandard.Count -gt 0) {
                foreach ($File in $LibrariesStandard) {
                    $Extension = $File.Substring($File.Length - 4, 4)
                    if ($Extension -eq '.dll') {
                        $Output = 'Add-Type -Path $PSScriptRoot\' + $File
                        $Output
                    }
                }
            } elseif ($LibrariesCore.Count -gt 0 -and $LibrariesDefault.Count -gt 0) {

                'if ($PSEdition -eq ''Core'') {'
                foreach ($File in $LibrariesCore) {
                    $Extension = $File.Substring($File.Length - 4, 4)
                    if ($Extension -eq '.dll') {
                        $Output = 'Add-Type -Path $PSScriptRoot\' + $File
                        $Output
                    }
                }
                '} else {'
                foreach ($File in $LibrariesDefault) {
                    $Extension = $File.Substring($File.Length - 4, 4)
                    if ($Extension -eq '.dll') {
                        $Output = 'Add-Type -Path $PSScriptRoot\' + $File
                        $Output
                    }
                }
                '}'

            } elseif ($LibrariesCore.Count -gt 0) {
                foreach ($File in $LibrariesCore) {
                    $Extension = $File.Substring($File.Length - 4, 4)
                    if ($Extension -eq '.dll') {
                        $Output = 'Add-Type -Path $PSScriptRoot\' + $File
                        $Output
                    }
                }
            } elseif ($LibrariesDefault.Count -gt 0) {
                foreach ($File in $LibrariesDefault) {
                    $Extension = $File.Substring($File.Length - 4, 4)
                    if ($Extension -eq '.dll') {
                        $Output = 'Add-Type -Path $PSScriptRoot\' + $File
                        $Output
                    }
                }
            }
        )

        if ($FunctionNames.Count -gt 0) {
            $Functions = ($FunctionNames | Sort-Object -Unique) -join "','"
            $Functions = "'$Functions'"
        } else {
            $Functions = @()
        }

        if ($FunctionAliaes.Count -gt 0) {
            $Aliases = ($FunctionAliaes | Sort-Object -Unique) -join "','"
            $Aliases = "'$Aliases'"
        } else {
            $Aliases = @()
        }

        "" | Add-Content -Path $Path

        if ($LibariesPath) {
            $LibraryContent | Add-Content -Path $LibariesPath
        } else {
            $LibraryContent | Add-Content -Path $Path
        }

        # This allows for loading modules in PSM1 file directly
        if ($InternalModuleDependencies) {
            $ModulesText = "'$($InternalModuleDependencies -join "','")'"
            @"
            `$ModulesOptional = $ModulesText
            foreach (`$Module in `$ModulesOptional) {
                Import-Module -Name `$Module -ErrorAction SilentlyContinue
            }
"@ | Add-Content -Path $Path
        }

        # This allows to export functions only if module loading works correctly
        if ($CommandModuleDependencies) {
            @(
                "`$ModuleFunctions = @{"
                foreach ($Module in $CommandModuleDependencies.Keys) {
                    #$Commands = "'$($CommandModuleDependencies[$Module] -join "','")'"
                    "$Module = @{"

                    foreach ($Command in $($CommandModuleDependencies[$Module])) {
                        #foreach ($Function in $AliasesAndFunctions.Keys) {
                            $Alias = "'$($AliasesAndFunctions[$Command] -join "','")'"
                            "    '$Command' = $Alias"
                        #}
                    }
                    "}"
                }
                "}"

                @"
                [Array] `$FunctionsAll = $Functions
                [Array] `$AliasesAll = $Aliases
                `$AliasesToRemove = [System.Collections.Generic.List[string]]::new()
                `$FunctionsToRemove = [System.Collections.Generic.List[string]]::new()
                foreach (`$Module in `$ModuleFunctions.Keys) {
                    try {
                        Import-Module -Name `$Module -ErrorAction Stop
                    } catch {
                        foreach (`$Function in `$ModuleFunctions[`$Module].Keys) {
                            `$FunctionsToRemove.Add(`$Function)
                            `$ModuleFunctions[`$Module][`$Function] | ForEach-Object {
                                if (`$_) {
                                    `$AliasesToRemove.Add(`$_)
                                }
                            }
                        }
                    }
                }
                `$FunctionsToLoad = foreach (`$Function in `$FunctionsAll) {
                    if (`$Function -notin `$FunctionsToRemove) {
                        `$Function
                    }
                }
                `$AliasesToLoad = foreach (`$Alias in `$AliasesAll) {
                    if (`$Alias -notin `$AliasesToRemove) {
                        `$Alias
                    }
                }

                Export-ModuleMember -Function @(`$FunctionsToLoad) -Alias @(`$AliasesToLoad)
"@
            ) | Add-Content -Path $Path
        } else {
            # this loads functions/aliases as designed
            "" | Add-Content -Path $Path
            "Export-ModuleMember -Function @($Functions) -Alias @($Aliases)" | Add-Content -Path $Path
        }

    } catch {
        $ErrorMessage = $_.Exception.Message
        #Write-Warning "New-PSM1File from $ModuleName failed build. Error: $ErrorMessage"
        Write-Error "New-PSM1File from $ModuleName failed build. Error: $ErrorMessage"
        Exit
    }
}
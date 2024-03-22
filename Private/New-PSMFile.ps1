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
       # [switch] $UsingNamespaces,
        [string] $LibariesPath,
        [Array] $InternalModuleDependencies,
        [System.Collections.IDictionary] $CommandModuleDependencies,
        [string[]] $BinaryModule
    )

    if ($PSVersionTable.PSVersion.Major -gt 5) {
        $Encoding = 'UTF8BOM'
    } else {
        $Encoding = 'UTF8'
    }

    Write-TextWithTime -Text "Adding alises/functions to load in PSM1 file - $Path" -PreAppend Plus {
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

        "" | Out-File -Append -LiteralPath $Path -Encoding $Encoding -ErrorAction Stop

        # This allows for loading modules in PSM1 file directly
        if ($InternalModuleDependencies.Count -gt 0) {
            @(
                "# Added internal module loading to cater for special cases "
                ""
            ) | Out-File -Append -LiteralPath $Path -Encoding $Encoding
            $ModulesText = "'$($InternalModuleDependencies -join "','")'"
            @"
            `$ModulesOptional = $ModulesText
            foreach (`$Module in `$ModulesOptional) {
                Import-Module -Name `$Module -ErrorAction SilentlyContinue
            }
"@ | Out-File -Append -LiteralPath $Path -Encoding $Encoding -ErrorAction Stop
        }

        # This allows to export functions only if module loading works correctly
        if ($CommandModuleDependencies -and $CommandModuleDependencies.Keys.Count -gt 0) {
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
                # Export functions and aliases as required
"@
            ) | Out-File -Append -LiteralPath $Path -Encoding $Encoding

            # for now we just export everything
            # we may need to change it in the future
            if ($BinaryModule.Count -gt 0) {
                "Export-ModuleMember -Function @(`$FunctionsToLoad) -Alias @(`$AliasesToLoad) -Cmdlet '*'" | Out-File -Append -LiteralPath $Path -Encoding $Encoding -ErrorAction Stop
            } else {
                "Export-ModuleMember -Function @(`$FunctionsToLoad) -Alias @(`$AliasesToLoad)" | Out-File -Append -LiteralPath $Path -Encoding $Encoding -ErrorAction Stop
            }

        } else {
            # this loads functions/aliases as designed
            #"" | Out-File -Append -LiteralPath $Path -Encoding utf8
            "# Export functions and aliases as required" | Out-File -Append -LiteralPath $Path -Encoding $Encoding
            if ($BinaryModule.Count -gt 0) {
                # for now we just export everything
                # we may need to change it in the future
                "Export-ModuleMember -Function @($Functions) -Alias @($Aliases) -Cmdlet '*'" | Out-File -Append -LiteralPath $Path -Encoding $Encoding -ErrorAction Stop
            } else {
                "Export-ModuleMember -Function @($Functions) -Alias @($Aliases)" | Out-File -Append -LiteralPath $Path -Encoding $Encoding -ErrorAction Stop
            }
        }

    } -SpacesBefore '   '
}
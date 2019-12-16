function New-PSMFile {
    [cmdletbinding()]
    param(
        [string] $Path,
        [string[]] $FunctionNames,
        [string[]] $FunctionAliaes,
        [Array] $LibrariesCore,
        [Array] $LibrariesDefault,
        [string] $ModuleName,
        [switch] $UsingNamespaces,
        [string] $LibariesPath
    )
    try {
        # $Content = Get-Content -LiteralPath $Path -Raw

        $LibraryContent = @(
            if ($LibrariesCore.Count -gt 0 -and $LibrariesDefault.Count -gt 0) {

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




        #if ($UsingNamespaces) {
        #    '. $PSScriptRoot\' + "$ModuleName.ps1" | Add-Content -Path $Path
        #}
        "" | Add-Content -Path $Path

        if ($LibariesPath) {
            $LibraryContent | Add-Content -Path $LibariesPath
        } else {
            $LibraryContent | Add-Content -Path $Path
        }

        "" | Add-Content -Path $Path
        "Export-ModuleMember -Function @($Functions) -Alias @($Aliases)" | Add-Content -Path $Path


    } catch {
        $ErrorMessage = $_.Exception.Message
        #Write-Warning "New-PSM1File from $ModuleName failed build. Error: $ErrorMessage"
        Write-Error "New-PSM1File from $ModuleName failed build. Error: $ErrorMessage"
        Exit
    }
}
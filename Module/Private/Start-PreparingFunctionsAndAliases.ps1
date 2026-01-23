function Start-PreparingFunctionsAndAliases {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration,
        $FullProjectPath,
        $Files,
        [System.Collections.IDictionary] $CmdletsAliases
    )
    if ($Configuration.Information.Manifest.FunctionsToExport -and $Configuration.Information.Manifest.AliasesToExport -and $Configuration.Information.Manifest.CmdletsToExport) {
        return [ordered] @{ }
    }

    $AliasesAndFunctions = Write-TextWithTime -Text 'Preparing function and aliases names' {
        Get-FunctionAliasesFromFolder -FullProjectPath $FullProjectPath -Files $Files -FunctionsToExport $Configuration.Information.FunctionsToExport -AliasesToExport $Configuration.Information.AliasesToExport
    } -PreAppend Information

    Write-TextWithTime -Text "Checking for duplicates in funcions, aliases and cmdlets" {
        # if user hasn't defined functions we will use auto-detected functions
        if ($null -eq $Configuration.Information.Manifest.FunctionsToExport) {
            $Configuration.Information.Manifest.FunctionsToExport = $AliasesAndFunctions.Keys | Where-Object { $_ }
            if (-not $Configuration.Information.Manifest.FunctionsToExport) {
                $Configuration.Information.Manifest.FunctionsToExport = @()
            }
        }
        # if user hasn't defined aliases we will use auto-detected aliases
        if ($null -eq $Configuration.Information.Manifest.AliasesToExport) {
            # Aliases to export from this module, for best performance, do not use wildcards and do not delete the entry, use an empty array if there are no aliases to export.
            $Configuration.Information.Manifest.AliasesToExport = @(
                $AliasesAndFunctions.Values | ForEach-Object { $_ } | Where-Object { $_ }
                # cmdlets aliases will have duplicates if there is core/default folder
                $CmdletsAliases.Values.AliasesToExport | ForEach-Object { $_ } | Where-Object { $_ }
            )
            if (-not $Configuration.Information.Manifest.AliasesToExport) {
                $Configuration.Information.Manifest.AliasesToExport = @()
            }
        }
        # if user hasn't defined cmdlets we will use auto-detected cmdlets
        if ($null -eq $Configuration.Information.Manifest.CmdletsToExport) {
            $Configuration.Information.Manifest.CmdletsToExport = @(
                $CmdletsAliases.Values.CmdletsToExport | ForEach-Object { $_ } | Where-Object { $_ }
            )
            if (-not $Configuration.Information.Manifest.CmdletsToExport) {
                $Configuration.Information.Manifest.CmdletsToExport = @()
            } else {
                $Configuration.Information.Manifest.CmdletsToExport = $Configuration.Information.Manifest.CmdletsToExport
            }
        }
        $FoundDuplicateAliases = $false
        if ($Configuration.Information.Manifest.AliasesToExport) {
            $UniqueAliases = $Configuration.Information.Manifest.AliasesToExport | Select-Object -Unique
            $DiffrenceAliases = Compare-Object -ReferenceObject $Configuration.Information.Manifest.AliasesToExport -DifferenceObject $UniqueAliases
            foreach ($Alias in $Configuration.Information.Manifest.AliasesToExport) {
                if ($Alias -in $Configuration.Information.Manifest.FunctionsToExport) {
                    Write-Text "   [-] Alias $Alias is also used as function name. Fix it!" -Color Red
                    $FoundDuplicateAliases = $true
                }
                if ($Alias -in $Configuration.Information.Manifest.CmdletsToExport) {
                    Write-Text "   [-] Alias $Alias is also used as cmdlet name. Fix it!" -Color Red
                    $FoundDuplicateAliases = $true
                }
            }
            foreach ($Alias in $DiffrenceAliases.InputObject) {
                Write-TextWithTime -Text "   [-] Alias $Alias is used multiple times. Fix it!" -Color Red
                $FoundDuplicateAliases = $true
            }
            if ($FoundDuplicateAliases) {
                return $false
            }
        }
        $FoundDuplicateCmdlets = $false
        if ($Configuration.Information.Manifest.CmdletsToExport) {
            $UniqueCmdlets = $Configuration.Information.Manifest.CmdletsToExport | Select-Object -Unique
            $DiffrenceCmdlets = Compare-Object -ReferenceObject $Configuration.Information.Manifest.CmdletsToExport -DifferenceObject $UniqueCmdlets
            foreach ($Cmdlet in $Configuration.Information.Manifest.CmdletsToExport) {
                if ($Cmdlet -in $Configuration.Information.Manifest.FunctionsToExport) {
                    Write-Text "   [-] Cmdlet $Cmdlet is also used as function name. Fix it!" -Color Red
                    $FoundDuplicateCmdlets = $true
                }
            }
            foreach ($Cmdlet in $DiffrenceCmdlets.InputObject) {
                Write-TextWithTime -Text "   [-] Cmdlet $Cmdlet is used multiple times. Fix it!" -Color Red
                $FoundDuplicateCmdlets = $true
            }
            if ($FoundDuplicateCmdlets) {
                return $false
            }
        }
        if (-not [string]::IsNullOrWhiteSpace($Configuration.Information.ScriptsToProcess)) {
            if ($null -eq $IsWindows -or $IsWindows -eq $true) {
                $StartsWithEnums = "$($Configuration.Information.ScriptsToProcess)\"
            } else {
                $StartsWithEnums = "$($Configuration.Information.ScriptsToProcess)/"
            }
            $FilesEnums = @(
                $LinkPrivatePublicFiles | Where-Object { ($_).StartsWith($StartsWithEnums) }
            )

            if ($FilesEnums.Count -gt 0) {
                Write-TextWithTime -Text "ScriptsToProcess export $FilesEnums" -PreAppend Plus -SpacesBefore '   '
                $Configuration.Information.Manifest.ScriptsToProcess = $FilesEnums
            }
        }
    } -PreAppend Information
    $AliasesAndFunctions
}

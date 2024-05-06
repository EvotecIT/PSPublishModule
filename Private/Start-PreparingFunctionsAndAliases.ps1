function Start-PreparingFunctionsAndAliases {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration,
        $FullProjectPath,
        $Files
    )
    if ($Configuration.Information.Manifest.FunctionsToExport -and $Configuration.Information.Manifest.AliasesToExport) {
        return $true
    }

    $AliasesAndFunctions = Write-TextWithTime -Text 'Preparing function and aliases names' {
        Get-FunctionAliasesFromFolder -FullProjectPath $FullProjectPath -Files $Files #-Folder $Configuration.Information.AliasesToExport
    } -PreAppend Information
    Write-TextWithTime -Text "Checking for duplicates in funcions and aliases" {
        # if ($AliasesAndFunctions -is [System.Collections.IDictionary]) {

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
            $Configuration.Information.Manifest.AliasesToExport = $AliasesAndFunctions.Values | ForEach-Object { $_ } | Where-Object { $_ }
            if (-not $Configuration.Information.Manifest.AliasesToExport) {
                $Configuration.Information.Manifest.AliasesToExport = @()
            }
        }
        # } else {
        # this is not used, as we're using Hashtable above, but maybe if we change mind we can go back
        # $Configuration.Information.Manifest.FunctionsToExport = $AliasesAndFunctions.Name | Where-Object { $_ }
        # if (-not $Configuration.Information.Manifest.FunctionsToExport) {
        #     $Configuration.Information.Manifest.FunctionsToExport = @()
        # }
        # $Configuration.Information.Manifest.AliasesToExport = $AliasesAndFunctions.Alias | ForEach-Object { $_ } | Where-Object { $_ }
        # if (-not $Configuration.Information.Manifest.AliasesToExport) {
        #     $Configuration.Information.Manifest.AliasesToExport = @()
        # }
        # }
        $FoundDuplicateAliases = $false
        if ($Configuration.Information.Manifest.AliasesToExport) {
            $UniqueAliases = $Configuration.Information.Manifest.AliasesToExport | Select-Object -Unique
            $DiffrenceAliases = Compare-Object -ReferenceObject $Configuration.Information.Manifest.AliasesToExport -DifferenceObject $UniqueAliases
            foreach ($Alias in $Configuration.Information.Manifest.AliasesToExport) {
                if ($Alias -in $Configuration.Information.Manifest.FunctionsToExport) {
                    Write-Text "   [-] Alias $Alias is also used as function name. Fix it!" -Color Red
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
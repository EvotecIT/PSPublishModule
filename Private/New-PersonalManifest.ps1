function New-PersonalManifest {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration,
        [string] $ManifestPath,
        [switch] $AddScriptsToProcess,
        [switch] $AddUsingsToProcess,
        [string] $ScriptsToProcessLibrary,
        [switch] $UseWildcardForFunctions,
        [switch] $OnMerge,
        [string[]] $BinaryModule
    )

    $TemporaryManifest = [ordered] @{ }
    $Manifest = $Configuration.Information.Manifest

    if ($UseWildcardForFunctions) {
        $Manifest.FunctionsToExport = @("*")
        $Manifest.AliasesToExport = @("*")
    }

    $Manifest.Path = $ManifestPath

    if (-not $AddScriptsToProcess) {
        $Manifest.ScriptsToProcess = @()
    }
    if ($AddUsingsToProcess -and $Configuration.UsingInPlace -and -not $ScriptsToProcessLibrary) {
        $Manifest.ScriptsToProcess = @($Configuration.UsingInPlace)
    } elseif ($AddUsingsToProcess -and $Configuration.UsingInPlace -and $ScriptsToProcessLibrary) {
        $Manifest.ScriptsToProcess = @($Configuration.UsingInPlace, $ScriptsToProcessLibrary)
    } elseif ($ScriptsToProcessLibrary) {
        $Manifest.ScriptsToProcess = @($ScriptsToProcessLibrary)
    }
    if ($Manifest.Contains('ExternalModuleDependencies')) {
        $TemporaryManifest.ExternalModuleDependencies = $Manifest.ExternalModuleDependencies
        $Manifest.Remove('ExternalModuleDependencies')
    }
    if ($Manifest.Contains('InternalModuleDependencies')) {
        $TemporaryManifest.InternalModuleDependencies = $Manifest.InternalModuleDependencies
        $Manifest.Remove('InternalModuleDependencies')
    }
    if ($Manifest.Contains('CommandModuleDependencies')) {
        $TemporaryManifest.CommandModuleDependencies = $Manifest.CommandModuleDependencies
        $Manifest.Remove('CommandModuleDependencies')
    }
    if ($Manifest.PreRelease) {
        $Configuration.CurrentSettings.PreRelease = $Manifest.PreRelease
    }
    if ($OnMerge) {
        if ($Configuration.Options.Merge.Style.PSD1) {
            $PSD1Style = $Configuration.Options.Merge.Style.PSD1
        }
    } else {
        if ($Configuration.Options.Standard.Style.PSD1) {
            $PSD1Style = $Configuration.Options.Standard.Style.PSD1
        }
    }
    if (-not $PSD1Style) {
        if ($Configuration.Options.Style.PSD1) {
            $PSD1Style = $Configuration.Options.Style.PSD1
        } else {
            $PSD1Style = 'Minimal'
        }
    }

    if ($PSD1Style -eq 'Native' -and $Configuration.Steps.PublishModule.Prerelease -eq '' -and (-not $TemporaryManifest.ExternalModuleDependencies)) {
        if ($Manifest.ModuleVersion) {
            New-ModuleManifest @Manifest
        } else {
            Write-Text -Text '[-] Module version is not available. Terminating.' -Color Red
            return $false
        }
        Write-TextWithTime -Text "[i] Converting $($ManifestPath) UTF8 without BOM" {
            (Get-Content -Path $ManifestPath -Raw -Encoding utf8) | Out-FileUtf8NoBom $ManifestPath
        }
    } else {
        if ($PSD1Style -eq 'Native') {
            Write-Text -Text '[-] Native PSD1 style is not available when using PreRelease or ExternalModuleDependencies. Switching to Minimal.' -Color Yellow
        }
        #if ($Data.ScriptsToProcess.Count -eq 0) {
        #$Data.Remove('ScriptsToProcess')
        #}
        #if ($Data.CmdletsToExport.Count -eq 0) {
        # $Data.Remove('CmdletsToExport')
        #}
        $Data = $Manifest
        $Data.PrivateData = @{
            PSData = [ordered]@{}
        }
        if ($Data.Path) {
            $Data.Remove('Path')
        }
        $ValidateEntriesPrivateData = @('Tags', 'LicenseUri', 'ProjectURI', 'IconUri', 'ReleaseNotes', 'Prerelease', 'RequireLicenseAcceptance', 'ExternalModuleDependencies')
        foreach ($Entry in [string[]] $Data.Keys) {
            if ($Entry -in $ValidateEntriesPrivateData) {
                $Data.PrivateData.PSData.$Entry = $Data.$Entry
                $Data.Remove($Entry)
            }
        }
        $ValidDataEntries = @('ModuleToProcess', 'NestedModules', 'GUID', 'Author', 'CompanyName', 'Copyright', 'ModuleVersion', 'Description', 'PowerShellVersion', 'PowerShellHostName', 'PowerShellHostVersion', 'CLRVersion', 'DotNetFrameworkVersion', 'ProcessorArchitecture', 'RequiredModules', 'TypesToProcess', 'FormatsToProcess', 'ScriptsToProcess', 'PrivateData', 'RequiredAssemblies', 'ModuleList', 'FileList', 'FunctionsToExport', 'VariablesToExport', 'AliasesToExport', 'CmdletsToExport', 'DscResourcesToExport', 'CompatiblePSEditions', 'HelpInfoURI', 'RootModule', 'DefaultCommandPrefix')
        foreach ($Entry in [string[]] $Data.Keys) {
            if ($Entry -notin $ValidDataEntries) {
                Write-Text -Text "[-] Removing wrong entries from PSD1 - $Entry" -Color Red
                $Data.Remove($Entry)
            }
        }
        foreach ($Entry in [string[]] $Data.PrivateData.PSData.Keys) {
            if ($Entry -notin $ValidateEntriesPrivateData) {
                Write-Text -Text "[-] Removing wrong entries from PSD1 Private Data - $Entry" -Color Red
                $Data.PrivateData.PSData.Remove($Entry)
            }
        }

        # Old way of setting prerelease
        if ($Configuration.Steps.PublishModule.Prerelease) {
            $Data.PrivateData.PSData.Prerelease = $Configuration.Steps.PublishModule.Prerelease
        }
        if ($TemporaryManifest.ExternalModuleDependencies) {
            # Add External Module Dependencies
            $Data.PrivateData.PSData.ExternalModuleDependencies = $TemporaryManifest.ExternalModuleDependencies
            # Make sure Required Modules contains ExternalModuleDependencies
            $Data.RequiredModules = @(
                foreach ($Module in $Manifest.RequiredModules) {
                    if ($Module -is [System.Collections.IDictionary]) {
                        # Lets rewrite module to retain proper order always
                        $Module = [ordered] @{
                            ModuleName    = $Module.ModuleName
                            ModuleVersion = $Module.ModuleVersion
                            Guid          = $Module.Guid
                        }
                        Remove-EmptyValue -Hashtable $Module
                        $Module
                    } else {
                        $Module
                    }
                }
                foreach ($Module in $TemporaryManifest.ExternalModuleDependencies) {
                    if ($Module -is [System.Collections.IDictionary]) {
                        # Lets rewrite module to retain proper order always
                        $Module = [ordered] @{
                            ModuleName    = $Module.ModuleName
                            ModuleVersion = $Module.ModuleVersion
                            Guid          = $Module.Guid
                        }
                        Remove-EmptyValue -Hashtable $Module
                        $Module
                    } else {
                        $Module
                    }
                }
            )
        }
        if (-not $Data.RequiredModules) {
            $Data.Remove('RequiredModules')
        }
        # we export all cmdlets until we have a better way to handle this
        # if module is hybrid (binary + script we need to ddo this)
        if ($BinaryModule.Count -gt 0) {
            #$Data.NestedModules = @($BinaryModule)
            $Data.CmdletsToExport = @("*")
        }
        $Data | Export-PSData -DataFile $ManifestPath -Sort
    }
}
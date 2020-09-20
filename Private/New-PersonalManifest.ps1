function New-PersonalManifest {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration,
        [string] $ManifestPath,
        [switch] $AddScriptsToProcess,
        [switch] $AddUsingsToProcess,
        [string] $ScriptsToProcessLibrary
    )

    $TemporaryManifest = @{ }
    $Manifest = $Configuration.Information.Manifest
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

    if ($Manifest.Contains('RequiredModules')) {
        foreach ($SubModule in $Manifest.RequiredModules) {
            if ($SubModule.ModuleVersion -eq 'Latest') {
                [Array] $AvailableModule = Get-Module -ListAvailable $SubModule.ModuleName -Verbose:$false
                if ($AvailableModule) {
                    $SubModule.ModuleVersion = $AvailableModule[0].Version
                } else {
                    Write-Text -Text "[-] Module $($SubModule.ModuleName) is not available, but defined as required with last version. Terminating." -Color Red
                    Exit
                }
            }
        }
    }

    if ($Configuration.Steps.PublishModule.Prerelease -eq '' -and (-not $TemporaryManifest.ExternalModuleDependencies) -or $Configuration.Options.Style.PSD1 -eq 'Native') {
        if ($Manifest.ModuleVersion) {
            New-ModuleManifest @Manifest
        } else {
            Write-Text -Text '[-] Module version is not available. Terminating.' -Color Red
            Exit
        }
        Write-TextWithTime -Text "[+] Converting $($ManifestPath) UTF8 without BOM" {
            (Get-Content -Path $ManifestPath -Raw) | Out-FileUtf8NoBom $ManifestPath
        }
    } else {
        # if ($Configuration.Steps.PublishModule.Prerelease -ne '' -or $TemporaryManifest.ExternalModuleDependencies -or $Configuration.Options.Style.PSD1 -ne 'Native') {
        #[string] $PSD1Path = $Configuration.Information.Manifest.Path
        #$FilePathPSD1 = Get-Item -Path $Configuration.Information.Manifest.Path
        #$Data = Import-PowerShellDataFile -Path $Configuration.Information.Manifest.Path
        if ($Data.ScriptsToProcess.Count -eq 0) {
            #$Data.Remove('ScriptsToProcess')
        }
        if ($Data.CmdletsToExport.Count -eq 0) {
            # $Data.Remove('CmdletsToExport')
        }
        $Data = $Manifest
        $Data.PrivateData = @{
            PSData = [ordered]@{}
        }
        if ($Data.Path) {
            $Data.Remove('Path')
        }
        if ($Data.Tags) {
            $Data.PrivateData.PSData.Tags = $Data.Tags
            $Data.Remove('Tags')
        }
        if ($Data.LicenseUri) {
            $Data.PrivateData.PSData.LicenseUri = $Data.LicenseUri
            $Data.Remove('LicenseUri')
        }
        if ($Data.ProjectUri) {
            $Data.PrivateData.PSData.ProjectUri = $Data.ProjectUri
            $Data.Remove('ProjectUri')
        }
        if ($Data.IconUri) {
            $Data.PrivateData.PSData.IconUri = $Data.IconUri
            $Data.Remove('IconUri')
        }
        if ($Data.ReleaseNotes) {
            $Data.PrivateData.PSData.ReleaseNotes = $Data.ReleaseNotes
            $Data.Remove('ReleaseNotes')
        }
        $ValidDataEntries = @('ModuleToProcess', 'NestedModules', 'GUID', 'Author', 'CompanyName', 'Copyright', 'ModuleVersion', 'Description', 'PowerShellVersion', 'PowerShellHostName', 'PowerShellHostVersion', 'CLRVersion', 'DotNetFrameworkVersion', 'ProcessorArchitecture', 'RequiredModules', 'TypesToProcess', 'FormatsToProcess', 'ScriptsToProcess', 'PrivateData', 'RequiredAssemblies', 'ModuleList', 'FileList', 'FunctionsToExport', 'VariablesToExport', 'AliasesToExport', 'CmdletsToExport', 'DscResourcesToExport', 'CompatiblePSEditions', 'HelpInfoURI', 'RootModule', 'DefaultCommandPrefix')
        foreach ($Entry in [string[]] $Data.Keys) {
            if ($Entry -notin $ValidDataEntries) {
                Write-Text -Text "[-] Removing wrong entries from PSD1 - $Entry" -Color Red
                $Data.Remove($Entry)
            }
        }
        $ValidateEntriesPrivateData = @('Tags', 'LicenseUri', 'ProjectURI', 'IconUri', 'ReleaseNotes', 'Prerelease', 'RequireLicenseAcceptance', 'ExternalModuleDependencies')
        foreach ($Entry in [string[]] $Data.PrivateData.PSData.Keys) {
            if ($Entry -notin $ValidateEntriesPrivateData) {
                Write-Text -Text "[-] Removing wrong entries from PSD1 Private Data - $Entry" -Color Red
                $Data.PrivateData.PSData.Remove($Entry)
            }
        }

        if ($Configuration.Steps.PublishModule.Prerelease) {
            $Data.PrivateData.PSData.Prerelease = $Configuration.Steps.PublishModule.Prerelease
        }
        if ($TemporaryManifest.ExternalModuleDependencies) {
            # Add External Module Dependencies
            $Data.PrivateData.PSData.ExternalModuleDependencies = $TemporaryManifest.ExternalModuleDependencies
            # Make sure Required Modules contains ExternalModuleDependencies
            $Data.RequiredModules = @(
                foreach ($Module in $Manifest.RequiredModules) {
                    $Module
                }
                foreach ($Module in $TemporaryManifest.ExternalModuleDependencies) {
                    $Module
                }
            )
        }
        $Data | Export-PSData -DataFile $ManifestPath -Sort
    }
}

#[-] [Error: The 'C:\Users\przemyslaw.klys\Documents\WindowsPowerShell\Modules\PSPublishModule\PSPublishModule.psd1'
#module cannot be imported because its manifest contains one or more members that are not valid. The valid manifest members are
#('ModuleToProcess', 'NestedModules', 'GUID', 'Author', 'CompanyName', 'Copyright', 'ModuleVersion', 'Description', 'PowerShellVersion', 'PowerShellHostName', 'PowerShellHostVersion', 'CLRVersion', 'DotNetFrameworkVersion', 'ProcessorArchitecture', 'RequiredModules', 'TypesToProcess', 'FormatsToProcess', 'ScriptsToProcess', 'PrivateData', 'RequiredAssemblies', 'ModuleList', 'FileList', 'FunctionsToExport', 'VariablesToExport', 'AliasesToExport', 'CmdletsToExport', 'DscResourcesToExport', 'CompatiblePSEditions', 'HelpInfoURI', 'RootModule', 'DefaultCommandPrefix'). Remove the members that are not valid ('Path', 'IconUri'), then try to import the module again.]
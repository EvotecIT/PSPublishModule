function Start-ModuleBuilding {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration,
        [string] $PathToProject
    )
    $DestinationPaths = [ordered] @{ }
    if ($Configuration.Information.Manifest.CompatiblePSEditions) {
        if ($Configuration.Information.Manifest.CompatiblePSEditions -contains 'Desktop') {
            $DestinationPaths.Desktop = [IO.path]::Combine($Configuration.Information.DirectoryModules, $Configuration.Information.ModuleName)
        }
        if ($Configuration.Information.Manifest.CompatiblePSEditions -contains 'Core') {
            $DestinationPaths.Core = [IO.path]::Combine($Configuration.Information.DirectoryModulesCore, $Configuration.Information.ModuleName)
        }
    } else {
        # Means missing from config - send to both
        $DestinationPaths.Desktop = [IO.path]::Combine($Configuration.Information.DirectoryModules, $Configuration.Information.ModuleName)
        $DestinationPaths.Core = [IO.path]::Combine($Configuration.Information.DirectoryModulesCore, $Configuration.Information.ModuleName)
    }

    [string] $Random = Get-Random 10000000000
    [string] $FullModuleTemporaryPath = [IO.path]::GetTempPath() + '' + $Configuration.Information.ModuleName
    [string] $FullTemporaryPath = [IO.path]::GetTempPath() + '' + $Configuration.Information.ModuleName + "_TEMP_$Random"
    if ($Configuration.Information.DirectoryProjects) {
        [string] $FullProjectPath = [IO.Path]::Combine($Configuration.Information.DirectoryProjects, $Configuration.Information.ModuleName)
    } else {
        [string] $FullProjectPath = $PathToProject
    }
    [string] $ProjectName = $Configuration.Information.ModuleName

    $PSD1FilePath = [System.IO.Path]::Combine($FullProjectPath, "$ProjectName.psd1")
    $PSM1FilePath = [System.IO.Path]::Combine($FullProjectPath, "$ProjectName.psm1")

    if ($Configuration.Information.Manifest.ModuleVersion) {
        if ($Configuration.Steps.BuildModule.LocalVersion) {
            $Versioning = Step-Version -Module $Configuration.Information.ModuleName -ExpectedVersion $Configuration.Information.Manifest.ModuleVersion -Advanced -LocalPSD1 $PSD1FilePath
        } else {
            $Versioning = Step-Version -Module $Configuration.Information.ModuleName -ExpectedVersion $Configuration.Information.Manifest.ModuleVersion -Advanced
        }
        $Configuration.Information.Manifest.ModuleVersion = $Versioning.Version
    } else {
        # lets fake the version if there's no PSD1, and there's no version in config
        $Configuration.Information.Manifest.ModuleVersion = 1.0.0
    }
    Write-Text '----------------------------------------------------'
    Write-Text "[i] Project/Module Name: $ProjectName" -Color Yellow
    if ($Configuration.Steps.BuildModule.LocalVersion) {
        Write-Text "[i] Current Local Version: $($Versioning.CurrentVersion)" -Color Yellow
    } else {
        Write-Text "[i] Current PSGallery Version: $($Versioning.CurrentVersion)" -Color Yellow
    }
    Write-Text "[i] Expected Version: $($Configuration.Information.Manifest.ModuleVersion)" -Color Yellow
    Write-Text "[i] Full module temporary path: $FullModuleTemporaryPath" -Color Yellow
    Write-Text "[i] Full project path: $FullProjectPath" -Color Yellow
    Write-Text "[i] Full temporary path: $FullTemporaryPath" -Color Yellow
    Write-Text "[i] PSScriptRoot: $PSScriptRoot" -Color Yellow
    Write-Text "[i] Current PSEdition: $PSEdition" -Color Yellow
    Write-Text "[i] Destination Desktop: $($DestinationPaths.Desktop)" -Color Yellow
    Write-Text "[i] Destination Core: $($DestinationPaths.Core)" -Color Yellow
    Write-Text '----------------------------------------------------'

    if (-not $Configuration.Steps.BuildModule) {
        Write-Text '[-] Section BuildModule is missing. Terminating.' -Color Red
        return $false
    }
    # We need to make sure module name is set, otherwise bad things will happen
    if (-not $Configuration.Information.ModuleName) {
        Write-Text '[-] Section Information.ModuleName is missing. Terminating.' -Color Red
        return $false
    }
    # check if project exists
    if (-not (Test-Path -Path $FullProjectPath)) {
        Write-Text "[-] Project path doesn't exists $FullProjectPath. Terminating" -Color Red
        return $false
    }


    $CmdletsAliases = [ordered] @{}

    $startLibraryBuildingSplat = @{
        RootDirectory        = $FullProjectPath
        Version              = $Configuration.Information.Manifest.ModuleVersion
        ModuleName           = $ProjectName
        LibraryConfiguration = $Configuration.Steps.BuildLibraries
        CmdletsAliases       = $CmdletsAliases
    }

    $Success = Start-LibraryBuilding @startLibraryBuildingSplat
    if ($Success -eq $false) {
        return $false
    }

    # Verify if manifest contains required modules and fix it if nessecary
    $Success = Convert-RequiredModules -Configuration $Configuration
    if ($Success -eq $false) {
        return $false
    }

    if ($Configuration.Steps.BuildModule.Enable -eq $true) {
        $CurrentLocation = (Get-Location).Path

        $Success = Start-PreparingStructure -Configuration $Configuration -FullProjectPath $FullProjectPath -FullTemporaryPath $FullTemporaryPath -FullModuleTemporaryPath $FullModuleTemporaryPath -DestinationPaths $DestinationPaths
        if ($Success -eq $false) {
            return $false
        }

        $Variables = Start-PreparingVariables -Configuration $Configuration -FullProjectPath $FullProjectPath
        if ($Variables -eq $false) {
            return $false
        }

        # lets build variables for later use
        $LinkDirectories = $Variables.LinkDirectories
        $LinkFilesRoot = $Variables.LinkFilesRoot
        $LinkPrivatePublicFiles = $Variables.LinkPrivatePublicFiles
        $DirectoriesWithClasses = $Variables.DirectoriesWithClasses
        $DirectoriesWithPS1 = $Variables.DirectoriesWithPS1
        $Files = $Variables.Files

        $startPreparingFunctionsAndAliasesSplat = @{
            Configuration   = $Configuration
            FullProjectPath = $FullProjectPath
            Files           = $Files
            CmdletsAliases  = $CmdletsAliases
        }

        $AliasesAndFunctions = Start-PreparingFunctionsAndAliases @startPreparingFunctionsAndAliasesSplat
        if ($AliasesAndFunctions -contains $false) {
            return $false
        }

        # Copy Configuration
        $SaveConfiguration = Copy-DictionaryManual -Dictionary $Configuration

        $newPersonalManifestSplat = @{
            Configuration       = $Configuration
            ManifestPath        = $PSD1FilePath
            AddScriptsToProcess = $true
        }
        if ($Configuration.Steps.BuildModule.UseWildcardForFunctions) {
            $newPersonalManifestSplat.UseWildcardForFunctions = $Configuration.Steps.BuildModule.UseWildcardForFunctions
        }
        # if this is hybrid or binary module, we need to make changes to PSD1
        if ($Configuration.Steps.BuildLibraries.BinaryModule) {
            $newPersonalManifestSplat.BinaryModule = $Configuration.Steps.BuildLibraries.BinaryModule
        }
        $Success = New-PersonalManifest @newPersonalManifestSplat
        if ($Success -eq $false) {
            return $false
        }

        Write-TextWithTime -Text "Verifying created PSD1 is readable" -PreAppend Information {
            if (Test-Path -LiteralPath $PSD1FilePath) {
                try {
                    $null = Import-PowerShellDataFile -Path $PSD1FilePath -ErrorAction Stop
                } catch {
                    Write-Text "[-] PSD1 Reading $PSD1FilePath failed. Error: $($_.Exception.Message)" -Color Red
                    return $false
                }
            } else {
                Write-Text "[-] PSD1 Reading $PSD1FilePath failed. File not created..." -Color Red
                return $false
            }
        } -ColorBefore Yellow -ColorTime Yellow -Color Yellow

        # Restore configuration, as some PersonalManifest plays with those
        $Configuration = $SaveConfiguration

        $Success = Format-Code -FilePath $PSD1FilePath -FormatCode $Configuration.Options.Standard.FormatCodePSD1
        if ($Success -eq $false) {
            return $false
        }
        $Success = Format-Code -FilePath $PSM1FilePath -FormatCode $Configuration.Options.Standard.FormatCodePSM1
        if ($Success -eq $false) {
            return $false
        }

        if ($Configuration.Steps.BuildModule.RefreshPSD1Only) {
            return
        }

        $startModuleMergingSplat = @{
            Configuration           = $Configuration
            ProjectName             = $ProjectName
            FullTemporaryPath       = $FullTemporaryPath
            FullModuleTemporaryPath = $FullModuleTemporaryPath
            FullProjectPath         = $FullProjectPath
            LinkDirectories         = $LinkDirectories
            LinkFilesRoot           = $LinkFilesRoot
            LinkPrivatePublicFiles  = $LinkPrivatePublicFiles
            DirectoriesWithPS1      = $DirectoriesWithPS1
            DirectoriesWithClasses  = $DirectoriesWithClasses
            AliasesAndFunction      = $AliasesAndFunctions
            CmdletsAliases          = $CmdletsAliases
        }

        $Success = Start-ModuleMerging @startModuleMergingSplat
        if ($Success -contains $false) {
            return $false
        }

        # Revers Path to current locatikon
        Set-Location -Path $CurrentLocation

        $Success = if ($Configuration.Steps.BuildModule.Enable) {
            if ($DestinationPaths.Desktop) {
                Write-TextWithTime -Text "Copy module to PowerShell 5 destination: $($DestinationPaths.Desktop)" {
                    $Success = Remove-Directory -Directory $DestinationPaths.Desktop
                    if ($Success -eq $false) {
                        return $false
                    }
                    Add-Directory -Directory $DestinationPaths.Desktop
                    Get-ChildItem -LiteralPath $FullModuleTemporaryPath | Copy-Item -Destination $DestinationPaths.Desktop -Recurse
                    # cleans up empty directories
                    Get-ChildItem $DestinationPaths.Desktop -Recurse -Force -Directory | Sort-Object -Property FullName -Descending | `
                        Where-Object { $($_ | Get-ChildItem -Force | Select-Object -First 1).Count -eq 0 } | `
                        Remove-Item #-Verbose
                } -PreAppend Plus
            }
            if ($DestinationPaths.Core) {
                Write-TextWithTime -Text "Copy module to PowerShell 6/7 destination: $($DestinationPaths.Core)" {
                    $Success = Remove-Directory -Directory $DestinationPaths.Core
                    if ($Success -eq $false) {
                        return $false
                    }
                    Add-Directory -Directory $DestinationPaths.Core
                    Get-ChildItem -LiteralPath $FullModuleTemporaryPath | Copy-Item -Destination $DestinationPaths.Core -Recurse
                    # cleans up empty directories
                    Get-ChildItem $DestinationPaths.Core -Recurse -Force -Directory | Sort-Object -Property FullName -Descending | `
                        Where-Object { $($_ | Get-ChildItem -Force | Select-Object -First 1).Count -eq 0 } | `
                        Remove-Item #-Verbose
                } -PreAppend Plus
            }
        }
        if ($Success -contains $false) {
            return $false
        }
        $Success = Write-TextWithTime -Text "Building artefacts" -PreAppend Information {
            # Old configuration still supported
            $Success = Start-ArtefactsBuilding -Configuration $Configuration -FullProjectPath $FullProjectPath -DestinationPaths $DestinationPaths -Type 'Releases'
            if ($Success -eq $false) {
                return $false
            }
            # Old configuration still supported
            $Success = Start-ArtefactsBuilding -Configuration $Configuration -FullProjectPath $FullProjectPath -DestinationPaths $DestinationPaths -Type 'ReleasesUnpacked'
            if ($Success -eq $false) {
                return $false
            }
            # new configuration building multiple artefacts
            foreach ($Artefact in  $Configuration.Steps.BuildModule.Artefacts) {
                $Success = Start-ArtefactsBuilding -Configuration $Configuration -FullProjectPath $FullProjectPath -DestinationPaths $DestinationPaths -ChosenArtefact $Artefact
                if ($Success -contains $false) {
                    return $false
                }
            }
        } -ColorBefore Yellow -ColorTime Yellow -Color Yellow
        if ($Success -contains $false) {
            return $false
        }
    }

    # Import Modules Section, useful to check before publishing
    if ($Configuration.Steps.ImportModules) {
        $ImportSuccess = Start-ImportingModules -Configuration $Configuration -ProjectName $ProjectName
        if ($ImportSuccess -contains $false) {
            return $false
        }
    }

    if ($Configuration.Options.TestsAfterMerge) {
        $TestsSuccess = Initialize-InternalTests -Configuration $Configuration -Type 'TestsAfterMerge'
        if ($TestsSuccess -eq $false) {
            return $false
        }
    }

    # Publish Module Section (old configuration)
    if ($Configuration.Steps.PublishModule.Enabled) {
        $Publishing = Start-PublishingGallery -Configuration $Configuration
        if ($Publishing -eq $false) {
            return $false
        }
    }
    # Publish Module Section to GitHub (old configuration)
    if ($Configuration.Steps.PublishModule.GitHub) {
        $Publishing = Start-PublishingGitHub -Configuration $Configuration -ProjectName $ProjectName
        if ($Publishing -eq $false) {
            return $false
        }
    }

    # new configuration allowing multiple galleries
    foreach ($ChosenNuget in $Configuration.Steps.BuildModule.GalleryNugets) {
        $Success = Start-PublishingGallery -Configuration $Configuration -ChosenNuget $ChosenNuget
        if ($Success -eq $false) {
            return $false
        }
    }
    # new configuration allowing multiple githubs/releases
    foreach ($ChosenNuget in $Configuration.Steps.BuildModule.GitHubNugets) {
        $Success = Start-PublishingGitHub -Configuration $Configuration -ChosenNuget $ChosenNuget -ProjectName $ProjectName
        if ($Success -eq $false) {
            return $false
        }
    }

    if ($Configuration.Steps.BuildDocumentation) {
        Start-DocumentationBuilding -Configuration $Configuration -FullProjectPath $FullProjectPath -ProjectName $ProjectName
    }

    # Cleanup temp directory
    Write-Text "[+] Cleaning up directories created in TEMP directory" -Color Yellow
    $Success = Remove-Directory $FullModuleTemporaryPath
    if ($Success -eq $false) {
        return $false
    }
    $Success = Remove-Directory $FullTemporaryPath
    if ($Success -eq $false) {
        return $false
    }
}
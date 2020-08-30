function Start-ModuleBuilding {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration
    )

    $DestinationPaths = @{ }
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
    $Versioning = Step-Version -Module $Configuration.Information.ModuleName -ExpectedVersion $Configuration.Information.Manifest.ModuleVersion -Advanced

    $Configuration.Information.Manifest.ModuleVersion = $Versioning.Version

    [string] $Random = Get-Random 10000000000
    [string] $FullModuleTemporaryPath = [IO.path]::GetTempPath() + '' + $Configuration.Information.ModuleName
    [string] $FullTemporaryPath = [IO.path]::GetTempPath() + '' + $Configuration.Information.ModuleName + "_TEMP_$Random"
    [string] $FullProjectPath = [IO.Path]::Combine($Configuration.Information.DirectoryProjects, $Configuration.Information.ModuleName)
    [string] $ProjectName = $Configuration.Information.ModuleName

    Write-Text '----------------------------------------------------'
    Write-Text "[i] Project Name: $ProjectName" -Color Yellow
    Write-Text "[i] PSGallery Version: $($Versioning.PSGalleryVersion)" -Color Yellow
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
        return
    }

    # check if project exists
    if (-not (Test-Path -Path $FullProjectPath)) {
        Write-Text "[-] Project path doesn't exists $FullProjectPath. Terminating" -Color Red
        return
    }

    if ($Configuration.Steps.BuildModule.Enable -eq $true) {

        if ($Configuration.Steps.BuildModule.DeleteBefore -eq $true) {
            Remove-Directory $($DestinationPaths.Desktop)
            Remove-Directory $($DestinationPaths.Core)
        }

        $CurrentLocation = (Get-Location).Path
        Set-Location -Path $FullProjectPath

        Remove-Directory $FullModuleTemporaryPath
        Remove-Directory $FullTemporaryPath
        Add-Directory $FullModuleTemporaryPath
        Add-Directory $FullTemporaryPath

        # $DirectoryTypes = 'Public', 'Private', 'Lib', 'Bin', 'Enums', 'Images', 'Templates', 'Resources'

        $LinkDirectories = @()
        $LinkPrivatePublicFiles = @()

        # Fix required fields:
        $Configuration.Information.Manifest.RootModule = "$($ProjectName).psm1"
        $Configuration.Information.Manifest.FunctionsToExport = @() #$FunctionToExport
        # Cmdlets to export from this module, for best performance, do not use wildcards and do not delete the entry, use an empty array if there are no cmdlets to export.
        $Configuration.Information.Manifest.CmdletsToExport = @()
        # Variables to export from this module
        $Configuration.Information.Manifest.VariablesToExport = @()
        # Aliases to export from this module, for best performance, do not use wildcards and do not delete the entry, use an empty array if there are no aliases to export.
        $Configuration.Information.Manifest.AliasesToExport = @()

        if ($Configuration.Information.Exclude) {
            $Exclude = $Configuration.Information.Exclude
        } else {
            $Exclude = '.*', 'Ignore', 'Examples', 'package.json', 'Publish', 'Docs'
        }
        if ($Configuration.Information.IncludeRoot) {
            $IncludeFilesRoot = $Configuration.Information.IncludeRoot
        } else {
            $IncludeFilesRoot = '*.psm1', '*.psd1', 'License*'
        }
        if ($Configuration.Information.IncludePS1) {
            $DirectoriesWithPS1 = $Configuration.Information.IncludePS1
        } else {
            $DirectoriesWithPS1 = 'Private', 'Public', 'Enums'
        }
        if ($Configuration.Information.IncludeAll) {
            $DirectoriesWithAll = $Configuration.Information.IncludeAll
        } else {
            $DirectoriesWithAll = 'Images\', 'Resources\', 'Templates\', 'Bin\', 'Lib\'
        }


        if ($Configuration.Steps.BuildModule.Enable -eq $true) {
            $PreparingFilesTime = Write-Text "[+] Preparing files and folders" -Start

            if ($PSEdition -eq 'core') {
                $Directories = @(
                    $TempDirectories = Get-ChildItem -Path $FullProjectPath -Directory -Exclude $Exclude -FollowSymlink
                    @(
                        $TempDirectories
                        $TempDirectories | Get-ChildItem -Directory -Recurse -FollowSymlink
                    )
                )
                $Files = Get-ChildItem -Path $FullProjectPath -Exclude $Exclude -FollowSymlink | Get-ChildItem -File -Recurse -FollowSymlink
                $FilesRoot = Get-ChildItem -Path "$FullProjectPath\*" -Include $IncludeFilesRoot -File -FollowSymlink
            } else {
                $Directories = @(
                    $TempDirectories = Get-ChildItem -Path $FullProjectPath -Directory -Exclude $Exclude
                    @(
                        $TempDirectories
                        $TempDirectories | Get-ChildItem -Directory -Recurse
                    )
                )
                $Files = Get-ChildItem -Path $FullProjectPath -Exclude $Exclude | Get-ChildItem -File -Recurse
                $FilesRoot = Get-ChildItem -Path "$FullProjectPath\*" -Include $IncludeFilesRoot -File
            }
            $LinkDirectories = @(
                foreach ($directory in $Directories) {
                    $RelativeDirectoryPath = (Resolve-Path -LiteralPath $directory.FullName -Relative).Replace('.\', '')
                    $RelativeDirectoryPath = "$RelativeDirectoryPath\"
                    $RelativeDirectoryPath
                }
            )
            $AllFiles = foreach ($File in $Files) {
                $RelativeFilePath = (Resolve-Path -LiteralPath $File.FullName -Relative).Replace('.\', '')
                $RelativeFilePath
            }
            $RootFiles = foreach ($File in $FilesRoot) {
                $RelativeFilePath = (Resolve-Path -LiteralPath $File.FullName -Relative).Replace('.\', '')
                $RelativeFilePath
            }
            # Link only files in Root Directory
            $LinkFilesRoot = @(
                foreach ($File in $RootFiles | Sort-Object -Unique) {
                    switch -Wildcard ($file) {
                        '*.psd1' {
                            $File
                        }
                        '*.psm1' {
                            $File
                        }
                        'License*' {
                            $File
                        }
                    }
                }
            )

            # Link only files from subfolers
            $LinkPrivatePublicFiles = @(
                foreach ($file in $AllFiles | Sort-Object -Unique) {
                    switch -Wildcard ($file) {
                        '*.ps1' {
                            foreach ($dir in $DirectoriesWithPS1) {
                                if ($file -like "$dir*") {
                                    $file
                                }
                            }
                            # Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory $DirectoriesWithPS1
                            continue
                        }
                        '*.*' {
                            #Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory $DirectoriesWithAll
                            foreach ($dir in $DirectoriesWithAll) {
                                if ($file -like "$dir*") {
                                    $file
                                }
                            }
                            continue
                        }
                    }
                }
            )
            $LinkPrivatePublicFiles = $LinkPrivatePublicFiles | Select-Object -Unique

            Write-Text -End -Time $PreparingFilesTime
            $AliasesAndFunctions = Write-TextWithTime -Text '[+] Preparing function and aliases names' {
                Get-FunctionAliasesFromFolder -FullProjectPath $FullProjectPath -Files $Files #-Folder $Configuration.Information.AliasesToExport
            }

            if ($AliasesAndFunctions.Function) {
                $Configuration.Information.Manifest.FunctionsToExport = $AliasesAndFunctions.Function
            }
            if ($AliasesAndFunctions.Alias) {
                $Configuration.Information.Manifest.AliasesToExport = $AliasesAndFunctions.Alias
            }

            if (-not [string]::IsNullOrWhiteSpace($Configuration.Information.ScriptsToProcess)) {
                $StartsWithEnums = "$($Configuration.Information.ScriptsToProcess)\"
                $FilesEnums = @(
                    $LinkPrivatePublicFiles | Where-Object { ($_).StartsWith($StartsWithEnums) }
                )

                if ($FilesEnums.Count -gt 0) {
                    Write-TextWithTime -Text "[+] ScriptsToProcess export $FilesEnums"
                    $Configuration.Information.Manifest.ScriptsToProcess = $FilesEnums
                }
                #}
            }

            $PSD1FilePath = "$FullProjectPath\$ProjectName.psd1"

            # Copy Configuration
            $SaveConfiguration = Copy-InternalDictionary -Dictionary $Configuration

            New-PersonalManifest -Configuration $Configuration -ManifestPath $PSD1FilePath -AddScriptsToProcess

            # Restore configuration, as some PersonalManifest plays with those
            $Configuration = $SaveConfiguration

            Format-Code -FilePath $PSD1FilePath -FormatCode $Configuration.Options.Standard.FormatCodePSD1

            if ($Configuration.Steps.BuildModule.RefreshPSD1Only) {
                Exit
            }
        }
        if ($Configuration.Steps.BuildModule.Enable -and $Configuration.Steps.BuildModule.Merge) {
            foreach ($Directory in $LinkDirectories) {
                $Dir = "$FullTemporaryPath\$Directory"
                Add-Directory $Dir
            }
            # Workaround to link files that are not ps1/psd1
            [Array] $CompareWorkaround = foreach ($_ in $DirectoriesWithPS1) {
                -join ($_, '\')
            }

            $LinkDirectoriesWithSupportFiles = $LinkDirectories | Where-Object { $_ -notin $CompareWorkaround }
            #$LinkDirectoriesWithSupportFiles = $LinkDirectories | Where-Object { $_ -ne 'Public\' -and $_ -ne 'Private\' }
            foreach ($Directory in $LinkDirectoriesWithSupportFiles) {
                $Dir = "$FullModuleTemporaryPath\$Directory"
                Add-Directory $Dir
            }

            $LinkingFilesTime = Write-Text "[+] Linking files from root and sub directories" -Start
            Set-LinkedFiles -LinkFiles $LinkFilesRoot -FullModulePath $FullTemporaryPath -FullProjectPath $FullProjectPath
            Set-LinkedFiles -LinkFiles $LinkPrivatePublicFiles -FullModulePath $FullTemporaryPath -FullProjectPath $FullProjectPath
            Write-Text -End -Time $LinkingFilesTime

            # Workaround to link files that are not ps1/psd1
            $FilesToLink = $LinkPrivatePublicFiles | Where-Object { $_ -notlike '*.ps1' -and $_ -notlike '*.psd1' }
            Set-LinkedFiles -LinkFiles $FilesToLink -FullModulePath $FullModuleTemporaryPath -FullProjectPath $FullProjectPath

            if (-not [string]::IsNullOrWhiteSpace($Configuration.Information.LibrariesCore)) {
                $StartsWithCore = "$($Configuration.Information.LibrariesCore)\"
                $FilesLibrariesCore = $LinkPrivatePublicFiles | Where-Object { ($_).StartsWith($StartsWithCore) }
            }
            if (-not [string]::IsNullOrWhiteSpace($Configuration.Information.LibrariesDefault)) {
                $StartsWithDefault = "$($Configuration.Information.LibrariesDefault)\"
                $FilesLibrariesDefault = $LinkPrivatePublicFiles | Where-Object { ($_).StartsWith($StartsWithDefault) }
            }
            if (-not [string]::IsNullOrWhiteSpace($Configuration.Information.LibrariesStandard)) {
                $StartsWithStandard = "$($Configuration.Information.LibrariesDefault)\"
                $FilesLibrariesStandard = $LinkPrivatePublicFiles | Where-Object { ($_).StartsWith($StartsWithStandard) }
            }

            Merge-Module -ModuleName $ProjectName `
                -ModulePathSource $FullTemporaryPath `
                -ModulePathTarget $FullModuleTemporaryPath `
                -Sort $Configuration.Options.Merge.Sort `
                -FunctionsToExport $Configuration.Information.Manifest.FunctionsToExport `
                -AliasesToExport $Configuration.Information.Manifest.AliasesToExport `
                -LibrariesStandard $FilesLibrariesStandard `
                -LibrariesCore $FilesLibrariesCore `
                -LibrariesDefault $FilesLibrariesDefault `
                -FormatCodePSM1 $Configuration.Options.Merge.FormatCodePSM1 `
                -FormatCodePSD1 $Configuration.Options.Merge.FormatCodePSD1 `
                -Configuration $Configuration

            if ($Configuration.Steps.BuildModule.CreateFileCatalog) {
                # Something is wrong here for folders other than root, need investigation
                $TimeToExecuteSign = [System.Diagnostics.Stopwatch]::StartNew()
                Write-Text "[+] 7th stage creating file catalog" -Color Blue
                $TimeToExecuteSign = [System.Diagnostics.Stopwatch]::StartNew()
                $CategoryPaths = @(
                    $FullModuleTemporaryPath
                    $NotEmptyPaths = (Get-ChildItem -Directory -Path $FullModuleTemporaryPath -Recurse).FullName
                    if ($NotEmptyPaths) {
                        $NotEmptyPaths
                    }
                )
                foreach ($CatPath in $CategoryPaths) {
                    $CatalogFile = [io.path]::Combine($CatPath, "$ProjectName.cat")
                    $FileCreated = New-FileCatalog -Path $CatPath -CatalogFilePath $CatalogFile -CatalogVersion 2.0
                    if ($FileCreated) {
                        Write-Text "   [>] Catalog file covering $CatPath was created $($FileCreated.Name)" -Color Yellow
                    }
                }
                $TimeToExecuteSign.Stop()
                Write-Text "[+] 7th stage creating file catalog [Time: $($($TimeToExecuteSign.Elapsed).Tostring())]" -Color Blue
            }
            if ($Configuration.Steps.BuildModule.SignMerged) {
                $TimeToExecuteSign = [System.Diagnostics.Stopwatch]::StartNew()
                Write-Text "[+] 8th stage signing files" -Color Blue
                $SignedFiles = Register-Certificate -LocalStore CurrentUser -Path $FullModuleTemporaryPath -Include @('*.ps1', '*.psd1', '*.psm1', '*.dll', '*.cat') -TimeStampServer 'http://timestamp.digicert.com'
                foreach ($File in $SignedFiles) {
                    Write-Text "   [>] File $($File.Path) with status: $($File.StatusMessage)" -Color Yellow
                }
                $TimeToExecuteSign.Stop()
                Write-Text "[+] 8th stage signing files [Time: $($($TimeToExecuteSign.Elapsed).Tostring())]" -Color Blue
            }
        }
        if ($Configuration.Steps.BuildModule.Enable -and (-not $Configuration.Steps.BuildModule.Merge)) {
            foreach ($Directory in $LinkDirectories) {
                $Dir = "$FullModuleTemporaryPath\$Directory"
                Add-Directory $Dir
            }
            $LinkingFilesTime = Write-Text "[+] Linking files from root and sub directories" -Start
            Set-LinkedFiles -LinkFiles $LinkFilesRoot -FullModulePath $FullModuleTemporaryPath -FullProjectPath $FullProjectPath
            Set-LinkedFiles -LinkFiles $LinkPrivatePublicFiles -FullModulePath $FullModuleTemporaryPath -FullProjectPath $FullProjectPath
            Write-Text -End -Time $LinkingFilesTime
        }


        # Revers Path to current locatikon
        Set-Location -Path $CurrentLocation

        if ($Configuration.Steps.BuildModule.Enable) {
            if ($DestinationPaths.Desktop) {
                Write-TextWithTime -Text "[+] Copy module to PowerShell 5 destination: $($DestinationPaths.Desktop)" {
                    Remove-Directory -Directory $DestinationPaths.Desktop
                    Add-Directory -Directory $DestinationPaths.Desktop
                    Get-ChildItem -LiteralPath $FullModuleTemporaryPath | Copy-Item -Destination $DestinationPaths.Desktop -Recurse
                    # cleans up empty directories
                    Get-ChildItem $DestinationPaths.Desktop -Recurse -Force -Directory | Sort-Object -Property FullName -Descending | `
                        Where-Object { $($_ | Get-ChildItem -Force | Select-Object -First 1).Count -eq 0 } | `
                        Remove-Item #-Verbose
                }


            }
            if ($DestinationPaths.Core) {
                Write-TextWithTime -Text "[+] Copy module to PowerShell 6/7 destination: $($DestinationPaths.Core)" {
                    Remove-Directory -Directory $DestinationPaths.Core
                    Add-Directory -Directory $DestinationPaths.Core
                    Get-ChildItem -LiteralPath $FullModuleTemporaryPath | Copy-Item -Destination $DestinationPaths.Core -Recurse
                    # cleans up empty directories
                    Get-ChildItem $DestinationPaths.Core -Recurse -Force -Directory | Sort-Object -Property FullName -Descending | `
                        Where-Object { $($_ | Get-ChildItem -Force | Select-Object -First 1).Count -eq 0 } | `
                        Remove-Item #-Verbose
                }
            }
        }
    }
    if ($Configuration.Steps.BuildModule.Enable) {
        if ($Configuration.Steps.BuildModule.Releases -or $Configuration.Steps.BuildModule.ReleasesUnpacked) {
            $TagName = "v$($Configuration.Information.Manifest.ModuleVersion)"
            $FileName = -join ("$TagName", '.zip')
            $FolderPathReleases = [System.IO.Path]::Combine($FullProjectPath, 'Releases')
            $ZipPath = [System.IO.Path]::Combine($FullProjectPath, 'Releases', $FileName)

            Write-TextWithTime -Text "[+] Compressing final merged release $ZipPath" {
                $null = New-Item -ItemType Directory -Path $FolderPathReleases -Force
                if ($DestinationPaths.Desktop) {
                    $CompressPath = [System.IO.Path]::Combine($DestinationPaths.Desktop, '*')
                    Compress-Archive -Path $CompressPath -DestinationPath $ZipPath -Force
                }
                if ($DestinationPaths.Core -and -not $DestinationPaths.Desktop) {
                    $CompressPath = [System.IO.Path]::Combine($DestinationPaths.Core, '*')
                    Compress-Archive -Path $CompressPath -DestinationPath $ZipPath -Force
                }
            }
            if ($Configuration.Steps.BuildModule.ReleasesUnpacked) {
                $FolderPathReleasesUnpacked = [System.IO.Path]::Combine($FullProjectPath, 'ReleasesUnpacked', $TagName )
                Write-TextWithTime -Text "[+] Copying final merged release to $FolderPathReleasesUnpacked" {
                    try {
                        if (Test-Path -Path $FolderPathReleasesUnpacked) {
                            Remove-Item -LiteralPath $FolderPathReleasesUnpacked -Force -Confirm:$false -Recurse
                        }
                        $null = New-Item -ItemType Directory -Path $FolderPathReleasesUnpacked -Force
                        if ($DestinationPaths.Desktop) {
                            Copy-Item -LiteralPath $DestinationPaths.Desktop -Recurse -Destination $FolderPathReleasesUnpacked -Force
                        }
                        if ($DestinationPaths.Core -and -not $DestinationPaths.Desktop) {
                            Copy-Item -LiteralPath $DestinationPaths.Core -Recurse -Destination $FolderPathReleasesUnpacked -Force
                        }
                    } catch {
                        $ErrorMessage = $_.Exception.Message
                        #Write-Warning "Merge module on file $FilePath failed. Error: $ErrorMessage"
                        Write-Host # This is to add new line, because the first line was opened up.
                        Write-Text "[-] Format-Code - Copying final merged release to $FolderPathReleasesUnpacked failed. Error: $ErrorMessage" -Color Red
                        Exit
                    }
                }
            }
        }
    }

    # Import Modules Section, useful to check before publishing
    if ($Configuration.Steps.ImportModules) {
        $TemporaryVerbosePreference = $VerbosePreference
        $VerbosePreference = $false

        if ($Configuration.Steps.ImportModules.RequiredModules) {
            Write-TextWithTime -Text '[+] Importing modules - REQUIRED' {
                foreach ($Module in $Configuration.Information.Manifest.RequiredModules) {
                    Import-Module -Name $Module -Force -ErrorAction Stop -Verbose:$false
                }
            }
        }
        if ($Configuration.Steps.ImportModules.Self) {
            Write-TextWithTime -Text '[+] Importing module - SELF' {
                Import-Module -Name $ProjectName -Force -ErrorAction Stop -Verbose:$false
            }
        }
        $VerbosePreference = $TemporaryVerbosePreference
    }

    if ($Configuration.Steps.PublishModule.Enabled) {
        Write-TextWithTime -Text "[+] Publishing Module to PowerShellGallery" {
            try {
                if ($Configuration.Options.PowerShellGallery.FromFile) {
                    $ApiKey = Get-Content -Path $Configuration.Options.PowerShellGallery.ApiKey
                    #New-PublishModule -ProjectName $Configuration.Information.ModuleName -ApiKey $ApiKey -RequireForce $Configuration.Steps.PublishModule.RequireForce
                    Publish-Module -Name $Configuration.Information.ModuleName -Repository PSGallery -NuGetApiKey $ApiKey -Force:$Configuration.Steps.PublishModule.RequireForce -Verbose -ErrorAction Stop
                } else {
                    #New-PublishModule -ProjectName $Configuration.Information.ModuleName -ApiKey $Configuration.Options.PowerShellGallery.ApiKey -RequireForce $Configuration.Steps.PublishModule.RequireForce
                    Publish-Module -Name $Configuration.Information.ModuleName -Repository PSGallery -NuGetApiKey $Configuration.Options.PowerShellGallery.ApiKey -Force:$Configuration.Steps.PublishModule.RequireForce -Verbose -ErrorAction Stop
                }
            } catch {
                $ErrorMessage = $_.Exception.Message
                Write-Host # This is to add new line, because the first line was opened up.
                Write-Text "[-] Publishing Module - failed. Error: $ErrorMessage" -Color Red
                Exit
            }
        }
    }

    if ($Configuration.Steps.PublishModule.GitHub) {
        $TagName = "v$($Configuration.Information.Manifest.ModuleVersion)"
        $FileName = -join ("$TagName", '.zip')
        $FolderPathReleases = [System.IO.Path]::Combine($FullProjectPath, 'Releases')
        $ZipPath = [System.IO.Path]::Combine($FullProjectPath, 'Releases', $FileName)

        if ($Configuration.Options.GitHub.FromFile) {
            $GitHubAccessToken = Get-Content -LiteralPath $Configuration.Options.GitHub.ApiKey
        } else {
            $GitHubAccessToken = $Configuration.Options.GitHub.ApiKey
        }
        if ($GitHubAccessToken) {
            if ($Configuration.Options.GitHub.RepositoryName) {
                $GitHubRepositoryName = $Configuration.Options.GitHub.RepositoryName
            } else {
                $GitHubRepositoryName = $ProjectName
            }
            if (Test-Path -LiteralPath $ZipPath) {
                if ($Configuration.Steps.PublishModule.Prerelease -ne '') {
                    $IsPreRelease = $true
                } else {
                    $IsPreRelease = $false
                }

                $StatusGithub = New-GitHubRelease -GitHubUsername $Configuration.Options.GitHub.UserName -GitHubRepositoryName $GitHubRepositoryName -GitHubAccessToken $GitHubAccessToken -TagName $TagName -AssetFilePaths $ZipPath -IsPreRelease $IsPreRelease
                if ($StatusGithub.ReleaseCreationSucceeded -and $statusGithub.Succeeded) {
                    $GithubColor = 'Green'
                    $GitHubText = '+'
                } else {
                    $GithubColor = 'Red'
                    $GitHubText = '-'
                }

                Write-Text "[$GitHubText] GitHub Release Creation Status: $($StatusGithub.ReleaseCreationSucceeded)" -Color $GithubColor
                Write-Text "[$GitHubText] GitHub Release Succeeded: $($statusGithub.Succeeded)" -Color $GithubColor
                Write-Text "[$GitHubText] GitHub Release Asset Upload Succeeded: $($statusGithub.AllAssetUploadsSucceeded)" -Color $GithubColor
                Write-Text "[$GitHubText] GitHub Release URL: $($statusGitHub.ReleaseUrl)" -Color $GithubColor
                if ($statusGithub.ErrorMessage) {
                    Write-Text "[$GitHubText] GitHub Release ErrorMessage: $($statusGithub.ErrorMessage)" -Color $GithubColor
                }
            }
        }
    }
    if ($Configuration.Steps.BuildDocumentation) {
        $WarningVariablesMarkdown = @()
        $DocumentationPath = "$FullProjectPath\$($Configuration.Options.Documentation.Path)"
        $ReadMePath = "$FullProjectPath\$($Configuration.Options.Documentation.PathReadme)"
        Write-Text "[+] Generating documentation to $DocumentationPath with $ReadMePath" -Color Yellow

        if (-not (Test-Path -Path $DocumentationPath)) {
            $null = New-Item -Path "$FullProjectPath\Docs" -ItemType Directory -Force
        }
        [Array] $Files = Get-ChildItem -Path $DocumentationPath
        if ($Files.Count -gt 0) {
            try {
                $null = Update-MarkdownHelpModule $DocumentationPath -RefreshModulePage -ModulePagePath $ReadMePath -ErrorAction Stop -WarningVariable +WarningVariablesMarkdown -WarningAction SilentlyContinue
            } catch {
                Write-Text "[-] Documentation warning: $($_.Exception.Message)" -Color Yellow
            }
        } else {
            try {
                $null = New-MarkdownHelp -Module $ProjectName -WithModulePage -OutputFolder $DocumentationPath -ErrorAction Stop -WarningVariable +WarningVariablesMarkdown -WarningAction SilentlyContinue
            } catch {
                Write-Text "[-] Documentation warning: $($_.Exception.Message)" -Color Yellow
            }
            $null = Move-Item -Path "$DocumentationPath\$ProjectName.md" -Destination $ReadMePath
            #Start-Sleep -Seconds 1
            # this is temporary workaround - due to diff output on update
            if ($Configuration.Options.Documentation.UpdateWhenNew) {
                try {
                    $null = Update-MarkdownHelpModule $DocumentationPath -RefreshModulePage -ModulePagePath $ReadMePath -ErrorAction Stop -WarningVariable +WarningVariablesMarkdown -WarningAction SilentlyContinue
                } catch {
                    Write-Text "[-] Documentation warning: $($_.Exception.Message)" -Color Yellow
                }
            }
            #
        }
        foreach ($_ in $WarningVariablesMarkdown) {
            Write-Text "[-] Documentation warning: $_" -Color Yellow
        }
    }

    # Cleanup temp directory
    Write-Text "[+] Cleaning up directories created in TEMP directory" -Color Yellow
    Remove-Directory $FullModuleTemporaryPath
    Remove-Directory $FullTemporaryPath
}